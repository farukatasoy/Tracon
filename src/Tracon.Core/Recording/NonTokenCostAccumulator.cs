namespace Tracon;

/// <summary>
/// Adds up the non-token amounts a run reported — what its workflow function nodes
/// reported and what its tools measured — into the run's <see cref="RunNonTokenCost"/>.
/// </summary>
/// <remarks>
/// <para>
/// One instance lives for one run, in <see cref="AgentRunScope.NonTokenCost"/>. A
/// reporter writes from the code it runs in (a node, a tool); the run reads the
/// total once, when it closes (<see cref="Seal"/>). Reports arrive from several
/// threads at once (a node that fans out, concurrent tool calls), so every change
/// happens under one lock.
/// </para>
/// <para>
/// <strong>A function node's report is an item; a tool's is a measurement of one call.</strong>
/// Every <c>ReportUsageAsync</c> is a new item. A tool reports under its call identity and
/// the LAST report of a call stands (<see cref="TraconToolUsage.Report"/>: a tool that
/// calls its provider more than once reports the total at the end), so a second report
/// replaces the first and the amount is never counted twice.
/// </para>
/// <para>
/// <strong>The one rule of what adds.</strong> An entry adds to the total when it has
/// an amount and that amount is in the run's currency (the entry's empty label is
/// <c>Tracon:Pricing:Currency</c>). Every other entry is counted
/// (<see cref="RunNonTokenCost.UnsummedItemCount"/>) and listed, never guessed at: a
/// sum across currencies is not an amount, and a missing price is unknown, not zero.
/// The rule is here and nowhere else.
/// </para>
/// <para>
/// <strong>The budget hears about an amount the moment it is reported</strong>
/// (<see cref="AgentRunBudget.RecordUsage(long, decimal?)"/>), so a tree whose renders
/// and speech already spent its cost ceiling stops at its next model call, the same
/// as one that spent it on tokens. This is why a tool's measurement enters here when
/// the tool reports it, not when the call settles: a non-streaming run reads its tool
/// records only after the last model turn.
/// </para>
/// </remarks>
internal sealed class NonTokenCostAccumulator
{
    /// <summary>The most items a function node can report in one run.</summary>
    public const int MaxNodeItems = 1_000;

    /// <summary>The longest <see cref="ToolCallUsage.Unit"/>, as the <c>usage_unit</c> column holds it.</summary>
    public const int MaxUnitLength = 64;

    /// <summary>The longest <see cref="ToolCallUsage.Currency"/>, as the <c>cost_currency</c> column holds it.</summary>
    public const int MaxCurrencyLength = 16;

    /// <summary>The longest <see cref="ToolCallUsage.Provider"/>.</summary>
    public const int MaxProviderLength = 100;

    /// <summary>The longest <see cref="ToolCallUsage.Model"/>.</summary>
    public const int MaxModelLength = 200;

    /// <summary>
    /// The largest quantity one item can carry: what a <c>numeric(20,10)</c> column
    /// holds. A larger value fails the insert, and a failed insert switches the run's
    /// recording off.
    /// </summary>
    public const decimal MaxQuantity = 9_999_999_999m;

    /// <summary>
    /// The largest amount one item can carry. Below the column's limit because the
    /// budget counts nano-units in a <see cref="long"/>.
    /// </summary>
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>
    /// The largest total a run can hold: what the snapshot's <c>numeric(20,10)</c> column
    /// holds. A sum past it is not truncated and not written: the item is counted as one
    /// the total does not cover.
    /// </summary>
    public const decimal MaxTotal = 9_999_999_999m;

    // A private type, not `object`: System.Threading.Lock does not exist on net8.0.
    private readonly Gate _gate = new();
    private readonly string? _currency;
    private readonly AgentRunBudget? _budget;

    // Both collections are created on the first report: most runs report nothing, and
    // every run builds one accumulator.
    private List<Entry>? _nodeEntries;
    private Dictionary<string, Entry>? _toolEntries;
    private bool _sealed;

    /// <summary>Creates the accumulator of one run.</summary>
    /// <param name="currency">
    /// The currency the run adds in: <c>Tracon:Pricing:Currency</c>, which may be
    /// <see langword="null"/> when none is configured.
    /// </param>
    /// <param name="budget">
    /// The tree's budget. An amount that adds is recorded into it. <see langword="null"/>
    /// when the run has none.
    /// </param>
    public NonTokenCostAccumulator(string? currency, AgentRunBudget? budget = null)
    {
        _currency = string.IsNullOrWhiteSpace(currency) ? null : currency;
        _budget = budget;
    }

    /// <summary>Checks an item a reporter wants to record.</summary>
    /// <param name="usage">The item.</param>
    /// <returns>Why the item cannot be recorded, or <see langword="null"/> when it can.</returns>
    /// <remarks>
    /// The limits are what the columns hold. Refusing here keeps one bad item from
    /// failing the insert and with it the recording of the whole run.
    /// </remarks>
    public static string? Validate(ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (string.IsNullOrWhiteSpace(usage.Unit))
        {
            return "The unit is empty.";
        }

        if (usage.Unit.Length > MaxUnitLength)
        {
            return $"The unit is longer than {MaxUnitLength} characters.";
        }

        if (usage.Quantity is < 0m or > MaxQuantity)
        {
            return $"The quantity is outside 0 to {MaxQuantity:0}.";
        }

        if (usage.Cost is < 0m or > MaxAmount)
        {
            return $"The amount is outside 0 to {MaxAmount:0}.";
        }

        if (usage.Currency is { Length: > MaxCurrencyLength })
        {
            return $"The currency is longer than {MaxCurrencyLength} characters.";
        }

        if (usage.Provider is { Length: > MaxProviderLength })
        {
            return $"The provider is longer than {MaxProviderLength} characters.";
        }

        if (usage.Model is { Length: > MaxModelLength })
        {
            return $"The model is longer than {MaxModelLength} characters.";
        }

        return null;
    }

    /// <summary>Records an item a function node reported.</summary>
    /// <param name="usage">The item. Checked with <see cref="Validate"/> by the caller.</param>
    /// <returns>What happened to the item.</returns>
    /// <remarks>
    /// The number of items one run can report is bounded: a loop that reports per chunk
    /// is told so (<see cref="NonTokenAddStatus.Rejected"/>) instead of filling the table.
    /// </remarks>
    public NonTokenAddStatus AddNodeItem(ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        lock (_gate)
        {
            if (_sealed)
            {
                return NonTokenAddStatus.Late;
            }

            if (_nodeEntries is { Count: >= MaxNodeItems })
            {
                return NonTokenAddStatus.Rejected;
            }

            var entry = ToEntry(usage);
            (_nodeEntries ??= []).Add(entry);
            RecordIntoBudget(entry.Amount ?? 0m);

            return NonTokenAddStatus.Added;
        }
    }

    /// <summary>Records the measurement a tool reported for one of its calls.</summary>
    /// <param name="callId">The call identity. A second report of the same call replaces the first.</param>
    /// <param name="usage">The measurement.</param>
    /// <remarks>
    /// An entry that fails <see cref="Validate"/> cannot be added, and it is not dropped:
    /// it is counted as one the total does not cover. After <see cref="Seal"/> the
    /// measurement stays on the call's record only.
    /// </remarks>
    public void ReportToolMeasurement(string callId, ToolCallUsage usage)
    {
        ArgumentException.ThrowIfNullOrEmpty(callId);
        ArgumentNullException.ThrowIfNull(usage);

        lock (_gate)
        {
            if (_sealed)
            {
                return;
            }

            var entry = ToEntry(usage);

            _toolEntries ??= new Dictionary<string, Entry>(StringComparer.Ordinal);
            _toolEntries.TryGetValue(callId, out var previous);
            _toolEntries[callId] = entry;

            // Only the increase reaches the budget: the earlier report of the same call
            // was already recorded, and a budget never takes an amount back.
            RecordIntoBudget((entry.Amount ?? 0m) - (previous.Amount ?? 0m));
        }
    }

    /// <summary>
    /// Closes the accumulator and returns the run's total. Reports that arrive later
    /// leave it unchanged.
    /// </summary>
    /// <returns>
    /// The total, or <see langword="null"/> when the run reported nothing. Asking twice
    /// returns the same value.
    /// </returns>
    public RunNonTokenCost? Seal()
    {
        lock (_gate)
        {
            _sealed = true;

            if ((_nodeEntries?.Count ?? 0) + (_toolEntries?.Count ?? 0) == 0)
            {
                return null;
            }

            var total = 0m;
            var hasAmount = false;
            var estimated = false;
            var unsummed = 0;

            foreach (var entry in (_nodeEntries ?? []).Concat(_toolEntries?.Values ?? (IEnumerable<Entry>)[]))
            {
                // 1,000 node items of MaxAmount already pass MaxTotal, and tools are not
                // bounded in number either. An amount the column cannot hold is counted,
                // never truncated.
                if (entry.Amount is not { } amount || amount > MaxTotal - total)
                {
                    unsummed++;

                    continue;
                }

                total += amount;
                hasAmount = true;
                estimated |= entry.Estimated;
            }

            return new RunNonTokenCost
            {
                Amount = hasAmount ? total : null,
                Currency = _currency,
                IsEstimated = estimated,
                UnsummedItemCount = unsummed,
            };
        }
    }

    private Entry ToEntry(ToolCallUsage usage)
        => usage.Cost is { } cost && Validate(usage) is null && InRunCurrency(usage.Currency)
            ? new Entry(cost, usage.IsEstimated)
            : default;

    private void RecordIntoBudget(decimal increase)
    {
        if (increase > 0m)
        {
            _budget?.RecordUsage(0, increase);
        }
    }

    private bool InRunCurrency(string? label)
        => string.IsNullOrWhiteSpace(label) || string.Equals(label, _currency, StringComparison.OrdinalIgnoreCase);

    private sealed class Gate;

    // Amount is the entry's counted amount, null when it adds nothing; Estimated is true
    // only for an entry that adds.
    private readonly record struct Entry(decimal? Amount, bool Estimated);
}

/// <summary>What happened to an item handed to <see cref="NonTokenCostAccumulator"/>.</summary>
internal enum NonTokenAddStatus
{
    /// <summary>The item counted toward the run's total (it adds, or it is counted as not added).</summary>
    Added,

    /// <summary>
    /// The run's total was already read. The item belongs in the run's detail and does not
    /// change the total.
    /// </summary>
    Late,

    /// <summary>The run already reported the most items it can.</summary>
    Rejected,
}
