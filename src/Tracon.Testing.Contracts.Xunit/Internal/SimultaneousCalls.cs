namespace Tracon.Testing.Contracts.Internal;

/// <summary>
/// Runs a delegate from several callers that are released at the same instant.
/// </summary>
/// <remarks>
/// <para>
/// Dedicated threads rather than <c>Task.Run</c>, and the
/// reason is measured rather than stylistic. A barrier makes every participant
/// wait for the last one, so the thread pool has to hold all of them at once;
/// once the demand passes the pool's minimum it injects new threads at roughly
/// one per second, and the probe spends that ramp asleep instead of measuring
/// anything. Measured on a 10-core machine: 32 pooled participants took 22-23
/// seconds each, three such probes accounted for 69 of the 70 seconds a
/// provider's contract suite spent, and every provider package paid it.
/// </para>
/// <para>
/// The threads also make the probe sharper: they are all released together,
/// which is the simultaneity the contract claims to test. With pooled callers
/// the early ones had been parked for twenty seconds by the time the last
/// arrived.
/// </para>
/// <para>
/// The release is a flag the callers spin on, not a kernel wait. A thread that
/// sleeps on a barrier wakes tens of microseconds apart from the others, and
/// the window two overlapping calls need against a store that keeps its rows in
/// memory is a few instructions wide; threads that spin see the flag within a
/// cache line transfer of each other. Measured against in-memory stores made
/// non-atomic, with the same threads and rounds, five cases that a barrier
/// caught in 5 to 18 of 20 runs were caught in 14 to 20.
/// </para>
/// </remarks>
internal static class SimultaneousCalls
{
    /// <summary>Invokes <paramref name="call"/> from <paramref name="count"/> threads released together.</summary>
    /// <param name="count">How many callers run at once.</param>
    /// <param name="call">The work each caller performs, given its index.</param>
    public static void Run(int count, Action<int> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        Run(
            count,
            index =>
            {
                call(index);

                return 0;
            });
    }

    /// <summary>Invokes <paramref name="call"/> from <paramref name="count"/> threads released together.</summary>
    /// <typeparam name="T">What each call returns.</typeparam>
    /// <param name="count">How many callers run at once.</param>
    /// <param name="call">The work each caller performs, given its index.</param>
    /// <returns>Each caller's result, in caller order.</returns>
    public static T[] Run<T>(int count, Func<int, T> call)
    {
        var results = new T[count];
        var failures = new Exception?[count];
        var threads = new Thread[count];
        var arrived = 0;
        var released = 0;

        for (var i = 0; i < count; i++)
        {
            var index = i;
            threads[index] = new Thread(() =>
            {
                try
                {
                    // Spinning, not a kernel wait: a thread that sleeps until the last one arrives wakes tens of microseconds
                    // apart from the others, and the window two overlapping calls need against a store that keeps its rows in
                    // memory is a few instructions wide. Spinners see the release within a cache line transfer of each
                    // other, so their first calls start together.
                    Interlocked.Increment(ref arrived);

                    var wait = new SpinWait();

                    while (Volatile.Read(ref released) == 0)
                    {
                        wait.SpinOnce(sleep1Threshold: -1);
                    }

                    results[index] = call(index);
                }
                catch (Exception failure)
                {
                    // Held rather than thrown: an exception on a thread nobody
                    // joins yet would tear down the test host instead of failing
                    // the assertion.
                    failures[index] = failure;
                }
            })
            {
                IsBackground = true,
                Name = $"contract-simultaneous-{index}",
            };
        }

        foreach (var thread in threads)
        {
            thread.Start();
        }

        var waitForAll = new SpinWait();

        while (Volatile.Read(ref arrived) < count)
        {
            waitForAll.SpinOnce(sleep1Threshold: -1);
        }

        Volatile.Write(ref released, 1);

        foreach (var thread in threads)
        {
            thread.Join();
        }

        var thrown = failures.Where(static failure => failure is not null).ToArray();

        return thrown.Length > 0
            ? throw new AggregateException("A simultaneous call failed.", thrown!)
            : results;
    }
}
