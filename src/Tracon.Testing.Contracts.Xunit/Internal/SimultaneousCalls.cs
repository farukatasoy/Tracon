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
/// The threads also make the probe sharper: the barrier now releases all of
/// them together, which is the simultaneity the contract claims to test. With
/// pooled callers the early ones had been parked for twenty seconds by the
/// time the last arrived.
/// </para>
/// </remarks>
internal static class SimultaneousCalls
{
    /// <summary>Invokes <paramref name="call"/> from <paramref name="count"/> threads released together.</summary>
    /// <typeparam name="T">What each call returns.</typeparam>
    /// <param name="count">How many callers run at once.</param>
    /// <param name="call">The work each caller performs, given its index.</param>
    /// <returns>Each caller's result, in caller order.</returns>
    public static T[] Run<T>(int count, Func<int, T> call)
    {
        var results = new T[count];
        var failures = new Exception?[count];
        using var start = new Barrier(count);
        var threads = new Thread[count];

        for (var i = 0; i < count; i++)
        {
            var index = i;
            threads[index] = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    results[index] = call(index);
                }
                catch (Exception failure)
                {
                    // Held rather than thrown: an exception on a thread nobody
                    // joins yet would tear down the test host instead of failing
                    // the assertion, and the barrier would never be satisfied.
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
