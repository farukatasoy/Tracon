namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// Races saves of one entry against deletes of it, for an in-memory store whose write finds the entry that is there and
/// puts it back under the identifier it found.
/// </summary>
/// <remarks>
/// <para>
/// A write of that kind has two steps: it reads the entry, then it writes the entry back. A delete that lands between the
/// two removes the entry and reports <see langword="true"/>, and the write then puts the entry back under the identifier
/// the delete had just freed. No serial order of the two calls gives that: delete then write creates a new entry under
/// the writer's own identifier (or fails when the write needs an entry), and write then delete leaves nothing.
/// </para>
/// <para>
/// The window is a few hundred nanoseconds wide, so a test cannot place the delete inside it. Two dedicated threads start
/// together and each makes a fixed number of calls: one writes the entry, the other lists the entries and deletes what
/// the listing shows. The deleting thread keeps every identifier whose delete returned <see langword="true"/> and checks
/// every later listing against that set. The check cannot fail on a store that makes the read and the write one step with
/// the delete: an identifier a delete freed is never written again, because a write that finds no entry uses an
/// identifier that was never used before.
/// </para>
/// </remarks>
internal static class StoreDeleteRace
{
    private const int Steps = 20_000;

    /// <summary>Runs the writes and the deletes at the same time.</summary>
    /// <param name="write">One call of the write. It carries an identifier of its own, new on every call.</param>
    /// <param name="list">The identifiers of the entries the store lists now.</param>
    /// <param name="delete">One call of the delete for the entry with the given identifier.</param>
    /// <returns>The identifiers a listing showed after a delete of them had returned <see langword="true"/>; empty when none came back.</returns>
    public static async Task<IReadOnlyCollection<Guid>> RunAsync(
        Func<Task> write,
        Func<Task<IReadOnlyList<Guid>>> list,
        Func<Guid, Task<bool>> delete)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(delete);

        using var barrier = new Barrier(2);

        var writing = Dedicated(() => WriteRepeatedlyAsync(write, barrier));
        var deleting = Dedicated(() => DeleteRepeatedlyAsync(list, delete, barrier));

        await Task.WhenAll(writing, deleting);

        return await deleting;
    }

    /// <summary>Says which identifiers came back, for a failure message.</summary>
    /// <param name="reappeared">The identifiers <see cref="RunAsync"/> returned.</param>
    /// <returns>The text of the failure message.</returns>
    public static string Describe(IReadOnlyCollection<Guid> reappeared)
        => "a delete reported that it removed these entries, and a later listing showed them again: "
           + string.Join(", ", reappeared);

    private static async Task WriteRepeatedlyAsync(Func<Task> write, Barrier barrier)
    {
        barrier.SignalAndWait();

        for (var step = 0; step < Steps; step++)
        {
            await write();
        }
    }

    private static async Task<IReadOnlyCollection<Guid>> DeleteRepeatedlyAsync(
        Func<Task<IReadOnlyList<Guid>>> list,
        Func<Guid, Task<bool>> delete,
        Barrier barrier)
    {
        var removed = new HashSet<Guid>();
        var reappeared = new HashSet<Guid>();

        barrier.SignalAndWait();

        for (var step = 0; step < Steps; step++)
        {
            await DeleteListedAsync(list, delete, removed, reappeared);
        }

        // The last write may have put an entry back after the last pass listed the store.
        foreach (var id in await list())
        {
            if (removed.Contains(id))
            {
                reappeared.Add(id);
            }
        }

        return reappeared;
    }

    private static async Task DeleteListedAsync(
        Func<Task<IReadOnlyList<Guid>>> list,
        Func<Guid, Task<bool>> delete,
        HashSet<Guid> removed,
        HashSet<Guid> reappeared)
    {
        foreach (var id in await list())
        {
            if (removed.Contains(id))
            {
                // Listed after its own delete returned true: the entry came back.
                reappeared.Add(id);
                continue;
            }

            if (await delete(id))
            {
                removed.Add(id);
            }
        }
    }

    // The thread is dedicated, so the two callers start together even when the pool has no free worker. Every call the
    // callers make completes inline, so the continuations stay on that thread.
    private static Task<T> Dedicated<T>(Func<Task<T>> body)
        => Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private static Task Dedicated(Func<Task> body)
        => Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
}
