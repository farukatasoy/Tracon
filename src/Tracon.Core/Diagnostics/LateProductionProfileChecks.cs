namespace Tracon;

/// <summary>
/// Holds the production profile checks that can only be built after the
/// container is, so <see cref="ProductionProfileValidator"/> reads them next to
/// the ones the container registered.
/// </summary>
/// <remarks>
/// <para>
/// Exists for ONE caller today: <c>MapTracon</c> learns which role policies are
/// registered only when it runs, after <c>Build()</c>, and that answer is a
/// startup question. Use it for a decision whose answer is known only once the
/// container exists. A decision a container registration can answer belongs in
/// <c>IProductionProfileCheck</c> registered with <c>TryAddEnumerable</c>; moving
/// those here would make a second, unlisted way to contribute a check.
/// </para>
/// <para>
/// Thread safe: <c>MapTracon</c> and the host's start may run on different
/// threads. A check added after the validator has read the list is not seen, and
/// the validator reads it once, while the host starts.
/// </para>
/// </remarks>
internal sealed class LateProductionProfileChecks
{
    // Locks on the list itself: Core also targets net8.0, which has no System.Threading.Lock,
    // and the list never leaves this class.
    private readonly List<IProductionProfileCheck> _checks = [];

    /// <summary>Adds a check.</summary>
    /// <param name="check">The check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="check"/> is <see langword="null"/>.</exception>
    public void Add(IProductionProfileCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        lock (_checks)
        {
            _checks.Add(check);
        }
    }

    /// <summary>Takes a copy of the checks added so far.</summary>
    /// <returns>The checks, in the order they were added.</returns>
    public IReadOnlyList<IProductionProfileCheck> Snapshot()
    {
        lock (_checks)
        {
            return [.. _checks];
        }
    }
}
