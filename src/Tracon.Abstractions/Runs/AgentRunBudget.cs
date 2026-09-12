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
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        MaxDuration = maxDuration;

        if (maxDuration is { } duration)
        {
            var now = _timeProvider.GetUtcNow();

            // A caller-supplied TimeSpan.MaxValue (or anything close to it) would
            // overflow DateTimeOffset's year-9999 ceiling; clamped rather than
            // thrown, since an absurd duration should behave as "practically
            // unlimited", not crash run creation.
            Deadline = duration <= DateTimeOffset.MaxValue - now ? now + duration : DateTimeOffset.MaxValue;
        }
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
    public TimeSpan? MaxDuration { get; }

    /// <summary>
    /// The instant the tree's time budget runs out. Computed once, when this
    /// budget was constructed; every run in the tree shares the same value.
    /// <see langword="null"/> when <see cref="MaxDuration"/> is <see langword="null"/>.
    /// </summary>
    public DateTimeOffset? Deadline { get; }

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
            Interlocked.Add(ref _consumedTokens, tokens);
        }

        if (cost is { } value && value > 0)
        {
            Interlocked.Add(ref _consumedCostNanoUnits, (long)Math.Round(value * CostScale, MidpointRounding.AwayFromZero));
        }
    }

    /// <summary>Produces a user-facing text describing the limit that was exceeded.</summary>
    /// <returns>Text stating which limit was exceeded.</returns>
    /// <remarks>
    /// The text is returned to the model as a tool result. Saying "budget
    /// exhausted" is not enough; unless the exceeded limit is named, the user
    /// cannot see which setting to raise.
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

    private string DescribeExceededLimit()
    {
        if (IsTokenBudgetExhausted)
        {
            return $"The run tree's token budget is exhausted ({ConsumedTokens}/{MaxTotalTokens}). " +
                   "Raise Tracon:AgentGraph:MaxTotalTokens to allow more.";
        }

        if (IsCostBudgetExhausted)
        {
            return string.Create(
                       CultureInfo.InvariantCulture,
                       $"The run tree's cost budget is exhausted ({ConsumedCost:0.000000}/{MaxTotalCost:0.000000}). ") +
                   "Raise Tracon:AgentGraph:MaxTotalCost to allow more.";
        }

        if (IsDurationBudgetExhausted)
        {
            var elapsed = _timeProvider.GetUtcNow() - (Deadline!.Value - MaxDuration!.Value);

            return $"The run tree's time budget is exhausted ({elapsed}/{MaxDuration}). " +
                   "Raise Tracon:AgentGraph:MaxDuration to allow more. This is a cutoff between " +
                   "model turns, not a hard timeout: a tool call already in progress is not interrupted.";
        }

        return $"The run tree's child-run limit is reached ({StartedRuns}/{MaxTotalRuns}). " +
               "Raise Tracon:AgentGraph:MaxTotalRuns to allow more.";
    }
}
