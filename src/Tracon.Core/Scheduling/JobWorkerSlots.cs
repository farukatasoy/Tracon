using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// The slots of one worker: a shared pool plus one dedicated pool per lane that has its own budget.
/// </summary>
/// <remarks>
/// A dedicated pool's size is fixed at the value seen the first time its lane is requested: the
/// same "fixed for this run of the worker" rule the shared pool has.
/// </remarks>
/// <param name="maxConcurrentJobs">The size of the shared pool.</param>
internal sealed class JobWorkerSlots(int maxConcurrentJobs)
{
    private readonly ConcurrentDictionary<string, JobSlotPool> _perLane = new(StringComparer.Ordinal);

    /// <summary>Gets the pool shared by every lane without a budget of its own.</summary>
    public JobSlotPool Shared { get; } = new(Math.Max(1, maxConcurrentJobs));

    /// <summary>Gets the pool of one lane, created on first use.</summary>
    /// <param name="lane">The lane.</param>
    /// <param name="max">The lane's budget; used only when the pool is created.</param>
    /// <returns>The lane's pool.</returns>
    public JobSlotPool ForLane(string lane, int max)
        => _perLane.GetOrAdd(lane, static (_, m) => new JobSlotPool(Math.Max(1, m)), max);

    /// <summary>Closes every pool: no new slot is handed out, and each pool ends with its last returned slot.</summary>
    public void Close()
    {
        Shared.Dispose();

        foreach (var pool in _perLane.Values)
        {
            pool.Dispose();
        }
    }

    /// <summary>Waits until every pool is closed and every slot it handed out has come back.</summary>
    /// <returns>The completion task.</returns>
    public Task WaitForDrainedAsync()
        => Task.WhenAll(_perLane.Values.Select(static pool => pool.Drained).Append(Shared.Drained));
}
