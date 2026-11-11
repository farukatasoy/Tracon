namespace Tracon.Core.UnitTests.TestInfrastructure;

/// <summary>
/// A test that fails on purpose while the thread pool is starved, to prove the heartbeat reaches the artifact.
/// </summary>
/// <remarks>
/// Skipped unless <see cref="EvidenceSelfTest.Variable"/> is set. CI sets it in one step, expects the test to be red and
/// looks for the thread pool delay in <c>pulse.txt</c> and in the timeout message the test fails with.
/// </remarks>
public sealed class PulseSelfTests
{
    /// <summary>How long the starved pool stays blocked. Real time on purpose: the pool is the thing under test.</summary>
    private static readonly TimeSpan Blocked = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task A_wait_that_times_out_on_a_starved_thread_pool_names_the_starvation()
    {
        EvidenceSelfTest.SkipUnlessEnabled();

        using var release = new ManualResetEventSlim();
        var blockers = new List<Task>();

        // Every pool thread blocks, so the heartbeat's own pool work item cannot start. The wait below then ends in a
        // timeout whose message and whose heartbeat file must both say so.
        for (var index = 0; index < (Environment.ProcessorCount * 2) + 2; index++)
        {
            blockers.Add(Task.Run(() => release.Wait(Blocked)));
        }

        try
        {
            await WaitUntil.TrueAsync(static () => false, "a condition that never holds while the pool is starved", TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(blockers);
        }
    }
}
