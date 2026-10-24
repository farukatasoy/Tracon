using System.Globalization;

namespace Tracon;

/// <summary>
/// A run budget shared across an entire call tree.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This type is deliberately a <c>class</c>, not a <c>record</c>.</strong>
/// The budget is shared mutable state: every run in the tree uses the
/// <em>same</em> instance. A <c>record</c> invites copying; a copied budget
/// would give each branch its own limit, and the limit would lose its meaning.
/// </para>
/// <para>
/// Counters increment lock-free (<see cref="Interlocked"/>). Child runs start
/// concurrently: Microsoft Agent Framework's background agents run without
/// blocking, so the same budget is read and written from multiple threads.
/// </para>
/// <para>
/// <see cref="TryReserveRun"/> (the count and depth dimensions) only blocks a
/// <strong>new</strong> child run from starting; it never interrupts a child
/// run already in progress — cutting one off midway would leave the model
/// with an incomplete context and would also corrupt the root run.
/// </para>
/// <para>
/// The token, cost, and duration dimensions are enforced the same way, by a
/// decorator installed inside the tool-call loop (<c>RunBudgetChatClient</c>,
/// internal to <c>Tracon.Core</c>): once <see cref="IsExhausted"/> is
/// <see langword="true"/>, that decorator refuses the tree's <strong>next</strong>
/// model call, so a long-running tool loop is cut off mid-run rather than
/// only at its next child call. The cutoff always lands between two model
/// turns, never inside one — the decorator only ever refuses a call it has
/// not yet made. A tool that itself runs long is <strong>not</strong> interrupted;
/// the cutoff waits for that tool call to finish and only then refuses the
/// model call that would follow it.
/// </para>
/// <para>
/// A workflow function node is gated at its <strong>start</strong>, on two of the
/// three: once <see cref="IsCostBudgetExhausted"/> or <see cref="IsDurationBudgetExhausted"/>
/// is <see langword="true"/> the node does not begin and the run ends
/// <c>Failed</c> with <see cref="TraconRunBudgetExceededException"/>. The token
/// dimension does not stop a node: it is on by default and a function node spends
/// no tokens. A node that is already running is never stopped, so the tree can
/// pass a ceiling by as much as its last node spends; the ceiling is not absolute.
/// </para>
/// <para>
/// The budget of a workflow run that continues earlier runs starts from what they spent
/// (<see cref="ConsumedTokens"/>, <see cref="ConsumedCost"/>, <see cref="StartedRuns"/>) and has
/// a <see cref="Deadline"/> that is as far behind a fresh one as the earlier runs were executing:
/// the ceiling is the logical run's, and the time a workflow waited for a person is not counted.
/// </para>
/// </remarks>
public sealed class AgentRunBudget
{
    // Cost is tracked as a fixed-point integer (nano-units: 1/1_000_000_000 of
    // the currency unit) because Interlocked has no decimal overload. Token
    // pricing runs to small fractions of a cent per token (example: $0.15 per
    // million tokens = $0.00000015/token); six decimal digits of precision
    // would round a single-turn charge to zero. Nine digits keeps that from
    // happening while a long still holds a total spend up to ~9.2 billion
    // currency units before overflow.
    private const decimal CostScale = 1_000_000_000m;

    // The largest cost, in currency units, the nano-unit counter holds.
    private const decimal MaxRecordableCost = long.MaxValue / CostScale;

    // What a refused function node's text adds: the limit stops a start, never a running node.
    private const string NodeKeepsRunning = "A node that is already running is not stopped.";

    private readonly TimeProvider _timeProvider;

    private long _consumedTokens;
    private long _consumedCostNanoUnits;
    private int _startedRuns;

    /// <summary>Creates a new run budget.</summary>
    /// <param name="maxDuration">
    /// The wall-clock time the whole tree may take, taken at face value: unlike
    /// the four <see langword="init"/> dimensions below, <see langword="null"/>
    /// is the only value that means "no limit" — <see cref="TimeSpan.Zero"/> or
    /// a negative value produces a <see cref="Deadline"/> that has already
    /// passed, the same way a raw <c>MaxTotalTokens = 0</c> reads as
    /// "exhausted from the start" rather than "unlimited". The "zero/negative
    /// means unlimited" convention is applied one layer up, in
    /// <c>TraconAgentGraphOptions.CreateBudget</c> (<c>Tracon.Core</c>).
    /// </param>
    /// <param name="timeProvider">
    /// The time source <see cref="Deadline"/> is computed from and every later
    /// expiry check reads. Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    public AgentRunBudget(TimeSpan? maxDuration = null, TimeProvider? timeProvider = null)
        : this(maxDuration, timeProvider, BudgetCarryOver.None)
    {
    }

    /// <summary>
    /// Creates the budget of a run that continues earlier runs of the same workflow: it starts from what they spent.
    /// </summary>
    /// <param name="maxDuration">The wall-clock time the whole chain may take; <see langword="null"/> for no limit.</param>
    /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="carryOver">What the earlier runs spent and how long they were executing.</param>
    internal AgentRunBudget(TimeSpan? maxDuration, TimeProvider? timeProvider, BudgetCarryOver carryOver)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        MaxDuration = maxDuration;

        if (maxDuration is { } duration)
        {
            var now = _timeProvider.GetUtcNow();

            // The chain's clock began when its first run began; it has been running for
            // ActiveDuration of it, so the deadline is that far behind a fresh one. The
            // person's wait between two runs is not in ActiveDuration. A chain that used all
            // of the time has a deadline that has already passed: the first node refuses.
            var used = carryOver.ActiveDuration > TimeSpan.Zero ? carryOver.ActiveDuration : TimeSpan.Zero;
            var start = used <= now - DateTimeOffset.MinValue ? now - used : DateTimeOffset.MinValue;

            // A caller-supplied TimeSpan.MaxValue (or anything close to it) would
            // overflow DateTimeOffset's year-9999 ceiling; clamped rather than
            // thrown, since an absurd duration should behave as "practically
            // unlimited", not crash run creation.
            Deadline = duration <= DateTimeOffset.MaxValue - start ? start + duration : DateTimeOffset.MaxValue;
        }

        _consumedTokens = Math.Max(carryOver.Tokens, 0);
        _consumedCostNanoUnits = carryOver.Cost > 0m ? ToNanoUnits(carryOver.Cost) : 0;
        _startedRuns = Math.Max(carryOver.Runs, 0);
    }

    /// <summary>
    /// The maximum tokens spendable across the tree. If <see langword="null"/>,
    /// there is no token limit.
    /// </summary>
    public long? MaxTotalTokens { get; init; }

    /// <summary>
    /// The maximum amount spendable across the tree. If <see langword="null"/>,
    /// there is no cost limit.
    /// </summary>
    /// <remarks>
    /// Cannot be enforced when a model call's pricing is undefined
    /// (<see cref="PricingSource.Unknown"/>); the token limit applies instead,
    /// the same fallback <see cref="QuotaDefinition.MaxCost"/> uses.
    /// </remarks>
    public decimal? MaxTotalCost { get; init; }

    /// <summary>
    /// The maximum number of <em>child</em> runs that may start. The root run
    /// is not counted toward this number. If <see langword="null"/>, there is
    /// no count limit.
    /// </summary>
    public int? MaxTotalRuns { get; init; }

    /// <summary>
    /// The maximum allowed call depth. The root run is 0, so the default
    /// value allows a three-layer tree.
    /// </summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>
    /// The wall-clock time the whole tree may take, counted from when this
    /// budget was constructed. If <see langword="null"/>, there is no time limit.
    /// </summary>
    /// <remarks>
    /// A workflow run that continues an earlier one (a person's answer, an operator's resume,
    /// a recovery after an interruption) counts the time the earlier runs were executing too,
    /// and not the time the workflow waited for a person: the limit is the logical run's.
    /// </remarks>
    public TimeSpan? MaxDuration { get; }

    /// <summary>
    /// The instant the tree's time budget runs out. Computed once, when this
    /// budget was constructed; every run in the tree shares the same value.
    /// <see langword="null"/> when <see cref="MaxDuration"/> is <see langword="null"/>.
    /// For a run that continues an earlier one it is earlier than a fresh budget's
    /// by the time the earlier runs were executing.
    /// </summary>
    public DateTimeOffset? Deadline { get; }

    /// <summary>
    /// Whose quota counter the tree's consumption is written to. Set by whatever opens
    /// the tree's root, read by every run that ends in the tree.
    /// </summary>
    /// <remarks>
    /// Lives here because the budget is the one object a child run, a consumer-built
    /// child (<c>TraconRunOptions.Budget</c>) and a tool call that outlives its run all
    /// already hold; the ambient run scope is not reachable from a task that ends after
    /// its root. A root assigns it afresh, so a budget a consumer reuses for a second root
    /// names that root and never the earlier one's tenant.
    /// </remarks>
    internal TreeAccount? Account
    {
        get;
        set
        {
            field = value;

            // What the budget holds now is not this root's to write: the earlier runs of a
            // continued workflow wrote their own share when they ended (or, on a reused
            // budget, the earlier root did). A check that counted it again would count it twice.
            value?.Ledger.Rebase(ConsumedTokens, Interlocked.Read(ref _consumedCostNanoUnits));
        }
    }

    /// <summary>
    /// Gets the spend of this tree its quota counter has not seen yet: the tokens and the amount the
    /// budget holds, less what the tree held when it named its account, less what it has written.
    /// </summary>
    /// <returns>The tokens and the amount; both zero when the tree has no account.</returns>
    /// <remarks>
    /// A run writes its own consumption when it ends, so a tree that runs for hours has spent more than its
    /// counter shows. A quota check at the start of new work adds this to the counter. It is never negative:
    /// an amount a run wrote that the budget did not measure (a model call priced in another currency than
    /// the installation's) cannot make a running tree look cheaper than the counter already says.
    /// </remarks>
    internal (long Tokens, decimal Cost) UnrecordedSpend()
    {
        if (Account?.Ledger is not { } ledger)
        {
            return (0, 0m);
        }

        var tokens = Math.Max(0, ConsumedTokens - ledger.BaselineTokens - ledger.RecordedTokens);
        var costNanoUnits = Math.Max(
            0,
            Interlocked.Read(ref _consumedCostNanoUnits) - ledger.BaselineCostNanoUnits - ledger.RecordedCostNanoUnits);

        return (tokens, costNanoUnits / CostScale);
    }

    /// <summary>
    /// Gets whether a model call's price must be worked out: a cost limit reads it, and so does a quota
    /// check, which needs the part of the tree's spend its counter has not seen.
    /// </summary>
    internal bool MeasuresCost => MaxTotalCost is not null || Account is { Quota.IsEnabled: true };

    /// <summary>The tokens spent across the tree so far.</summary>
    public long ConsumedTokens => Interlocked.Read(ref _consumedTokens);

    /// <summary>The amount spent across the tree so far.</summary>
    public decimal ConsumedCost => Interlocked.Read(ref _consumedCostNanoUnits) / CostScale;

    /// <summary>The number of child runs started so far.</summary>
    public int StartedRuns => Volatile.Read(ref _startedRuns);

    /// <summary>Whether the token limit has been exceeded.</summary>
    public bool IsTokenBudgetExhausted
        => MaxTotalTokens is { } max && ConsumedTokens >= max;

    /// <summary>Whether the cost limit has been exceeded.</summary>
    public bool IsCostBudgetExhausted
        => MaxTotalCost is { } max && ConsumedCost >= max;

    /// <summary>Whether the tree's <see cref="Deadline"/> has passed.</summary>
    public bool IsDurationBudgetExhausted
        => Deadline is { } deadline && _timeProvider.GetUtcNow() >= deadline;

    /// <summary>
    /// Whether the tree has spent past its token, cost, or time limit. Does
    /// <strong>not</strong> reflect <see cref="MaxTotalRuns"/>: the run-count
    /// limit only blocks starting a <em>new</em> child run
    /// (<see cref="TryReserveRun"/>), it says nothing about whether the
    /// current run may keep calling its model.
    /// </summary>
    public bool IsExhausted => IsTokenBudgetExhausted || IsCostBudgetExhausted || IsDurationBudgetExhausted;

    /// <summary>
    /// Reserves budget room for a new child run.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if room was reserved; <see langword="false"/> if
    /// the token, cost, time, or count limit has been exceeded.
    /// </returns>
    /// <remarks>
    /// The counter increments only when room is reserved. If a failed attempt
    /// incremented the counter, every new attempt in a tree that has hit its
    /// limit would keep growing the count, and the UI would show runs that
    /// never actually started.
    /// </remarks>
    public bool TryReserveRun()
    {
        if (IsExhausted)
        {
            return false;
        }

        if (MaxTotalRuns is not { } maxRuns)
        {
            Interlocked.Increment(ref _startedRuns);
            return true;
        }

        // CAS loop: when two concurrent child calls request the last slot of
        // the limit at the same time, only one must win.
        var current = Volatile.Read(ref _startedRuns);

        while (current < maxRuns)
        {
            var previous = Interlocked.CompareExchange(ref _startedRuns, current + 1, current);

            if (previous == current)
            {
                return true;
            }

            current = previous;
        }

        return false;
    }

    /// <summary>Records the number of tokens spent into the budget.</summary>
    /// <param name="tokens">The number of tokens to add. A negative value is ignored.</param>
    public void RecordUsage(long tokens) => RecordUsage(tokens, cost: null);

    /// <summary>Records the tokens and cost spent in one model turn into the budget.</summary>
    /// <param name="tokens">The number of tokens to add. A negative value is ignored.</param>
    /// <param name="cost">
    /// The cost to add, or <see langword="null"/> when the turn's price is
    /// undefined (<see cref="PricingSource.Unknown"/>) — nothing is added to
    /// <see cref="ConsumedCost"/> in that case, so an installation with an
    /// unpriced model never trips a cost limit it cannot actually measure.
    /// </param>
    public void RecordUsage(long tokens, decimal? cost)
    {
        if (tokens > 0)
        {
            AddSaturating(ref _consumedTokens, tokens);
        }

        if (cost is { } value && value > 0)
        {
            AddSaturating(ref _consumedCostNanoUnits, ToNanoUnits(value));
        }
    }

    // An amount the counter cannot hold is the counter's maximum, not a wrap.
    internal static long ToNanoUnits(decimal value)
        => value >= MaxRecordableCost
            ? long.MaxValue
            : (long)Math.Round(value * CostScale, MidpointRounding.AwayFromZero);

    // A counter that wraps turns a spent budget into a fresh one: a limit passed long ago
    // would stop being passed. The total stays at the maximum instead, so an exhausted
    // budget stays exhausted.
    private static void AddSaturating(ref long location, long amount)
    {
        var current = Interlocked.Read(ref location);

        while (true)
        {
            var next = long.MaxValue - current < amount ? long.MaxValue : current + amount;
            var observed = Interlocked.CompareExchange(ref location, next, current);

            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    /// <summary>Produces a user-facing text describing the limit that was exceeded.</summary>
    /// <returns>Text stating which limit was exceeded.</returns>
    /// <remarks>
    /// For agent delegation the text is returned to the model as a tool result;
    /// for a workflow step it is the message of the
    /// <see cref="TraconRunBudgetExceededException"/> that fails the run. Saying
    /// "budget exhausted" is not enough; unless the exceeded limit is named, the
    /// user cannot see which setting to raise.
    /// </remarks>
    public string DescribeExhaustion()
        => $"{DescribeExceededLimit()} A new child run cannot be started.";

    /// <summary>
    /// Produces a user-facing text describing the token, cost, or time limit
    /// that cut a model call short mid-run.
    /// </summary>
    /// <remarks>
    /// Only called once <see cref="IsExhausted"/> is <see langword="true"/>;
    /// the run-count limit does not apply here (see <see cref="IsExhausted"/>).
    /// </remarks>
    public string DescribeModelCallExhaustion()
        => $"{DescribeExceededLimit()} No further model calls can be made in this run tree.";

    /// <summary>
    /// Whether a workflow function node may not start: the cost or the time limit is
    /// spent. The token and run-count limits are not read, because a function node
    /// spends no tokens and opens no child run.
    /// </summary>
    internal bool IsNodeStartExhausted => IsCostBudgetExhausted || IsDurationBudgetExhausted;

    /// <summary>
    /// Produces the text for a function node that was not started, naming the one
    /// limit that stopped it: cost first, then time, the order <see cref="DescribeExhaustion"/>
    /// uses. The token limit is never named, since it did not stop the node.
    /// </summary>
    /// <returns>Text stating which limit was exceeded and that running nodes are not stopped.</returns>
    /// <remarks>Only called once <see cref="IsNodeStartExhausted"/> is <see langword="true"/>.</remarks>
    internal string DescribeNodeStartExhaustion()
        => IsCostBudgetExhausted ? $"{DescribeCostLimit()} {NodeKeepsRunning}"
            : IsDurationBudgetExhausted ? $"{DescribeTimeLimit()} {NodeKeepsRunning}"
            : DescribeExceededLimit();

    private string DescribeExceededLimit()
    {
        if (IsTokenBudgetExhausted)
        {
            return $"The run tree's token budget is exhausted ({ConsumedTokens}/{MaxTotalTokens}). " +
                   "Raise Tracon:AgentGraph:MaxTotalTokens to allow more.";
        }

        if (IsCostBudgetExhausted)
        {
            return DescribeCostLimit();
        }

        if (IsDurationBudgetExhausted)
        {
            return $"{DescribeTimeLimit()} This is a cutoff between model turns, not a hard timeout: " +
                   "a tool call already in progress is not interrupted.";
        }

        return $"The run tree's child-run limit is reached ({StartedRuns}/{MaxTotalRuns}). " +
               "Raise Tracon:AgentGraph:MaxTotalRuns to allow more.";
    }

    // The cost and time sentences are written once: the model call, the child run and the
    // function node say the same thing about the same limit.
    private string DescribeCostLimit()
        => string.Create(
               CultureInfo.InvariantCulture,
               $"The run tree's cost budget is exhausted ({ConsumedCost:0.000000}/{MaxTotalCost:0.000000}). ") +
           "Raise Tracon:AgentGraph:MaxTotalCost to allow more.";

    private string DescribeTimeLimit()
    {
        var elapsed = _timeProvider.GetUtcNow() - (Deadline!.Value - MaxDuration!.Value);

        return $"The run tree's time budget is exhausted ({elapsed}/{MaxDuration}). " +
               "Raise Tracon:AgentGraph:MaxDuration to allow more.";
    }
}
