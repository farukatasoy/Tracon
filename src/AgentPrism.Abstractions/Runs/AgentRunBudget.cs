namespace AgentPrism;

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
/// The budget blocks <strong>new</strong> child runs; it does not interrupt a
/// run in progress. A child run cut off midway would leave the model with an
/// incomplete context and would also corrupt the root run.
/// </para>
/// </remarks>
public sealed class AgentRunBudget
{
    private long _consumedTokens;
    private int _startedRuns;

    /// <summary>
    /// The maximum tokens spendable across the tree. If <see langword="null"/>,
    /// there is no token limit.
    /// </summary>
    public long? MaxTotalTokens { get; init; }

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

    /// <summary>The tokens spent across the tree so far.</summary>
    public long ConsumedTokens => Interlocked.Read(ref _consumedTokens);

    /// <summary>The number of child runs started so far.</summary>
    public int StartedRuns => Volatile.Read(ref _startedRuns);

    /// <summary>Whether the token limit has been exceeded.</summary>
    public bool IsTokenBudgetExhausted
        => MaxTotalTokens is { } max && ConsumedTokens >= max;

    /// <summary>
    /// Reserves budget room for a new child run.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if room was reserved; <see langword="false"/> if
    /// the token or count limit has been exceeded.
    /// </returns>
    /// <remarks>
    /// The counter increments only when room is reserved. If a failed attempt
    /// incremented the counter, every new attempt in a tree that has hit its
    /// limit would keep growing the count, and the UI would show runs that
    /// never actually started.
    /// </remarks>
    public bool TryReserveRun()
    {
        if (IsTokenBudgetExhausted)
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
    public void RecordUsage(long tokens)
    {
        if (tokens <= 0)
        {
            return;
        }

        Interlocked.Add(ref _consumedTokens, tokens);
    }

    /// <summary>Produces a user-facing text describing the limit that was exceeded.</summary>
    /// <returns>Text stating which limit was exceeded.</returns>
    /// <remarks>
    /// The text is returned to the model as a tool result. Saying "budget
    /// exhausted" is not enough; unless the exceeded limit is named, the user
    /// cannot see which setting to raise.
    /// </remarks>
    public string DescribeExhaustion()
        => IsTokenBudgetExhausted
            ? $"The run tree's token budget is exhausted ({ConsumedTokens}/{MaxTotalTokens}). " +
              "A new child run cannot be started."
            : $"The run tree's child-run limit is reached ({StartedRuns}/{MaxTotalRuns}). " +
              "A new child run cannot be started.";
}
