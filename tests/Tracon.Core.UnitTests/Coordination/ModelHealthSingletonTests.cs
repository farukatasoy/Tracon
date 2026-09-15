using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Coordination;

/// <summary>
/// End-to-end scenario where two real <see cref="ModelProviderHealthBackgroundService"/>
/// instances share the SAME lease store (Phase 42, DoD: "With two instances ...
/// the model health check runs on only one"). Uses real time: the services run
/// on their own <c>PeriodicTimer</c>s, no fake timer is injected (this project
/// does not test BackgroundServices that way, see <c>MEMORY.md</c> — "A unit
/// test is not enough").
/// </summary>
public sealed class ModelHealthSingletonTests
{
    [Fact]
    public async Task Health_check_runs_on_only_one_instance()
    {
        var leaseStore = new InMemorySingletonLeaseStore();
        // 🚨 The lease MUST NOT be able to expire inside the test window. With
        // a 1-second lease this test used to pass only because the window
        // stayed under a second: renewal runs at one third of the lease but
        // never faster than SingletonGuard.MinimumRenewInterval, so the lease
        // expired under its live owner and instance B took the work over.
        // Under CI load the 500 ms wait overran, the takeover happened, and
        // this test went red. That configuration is rejected outright now
        // (SingletonExecutionOptionsValidator); here a lease that cannot expire
        // keeps the assertion about ONE instance free of the wall clock.
        var singletonOptions = Options(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromMinutes(5),
        });

        var healthOptions = Options(new TraconOptions
        {
            Health = new TraconHealthOptions { BackgroundInterval = TimeSpan.FromMilliseconds(60) },
        });

        var providerA = new CountingHealthProvider("providerA");
        var providerB = new CountingHealthProvider("providerB");

        var cacheA = new ModelProviderHealthCache([providerA], healthOptions);
        var cacheB = new ModelProviderHealthCache([providerB], healthOptions);

        var serviceA = new ModelProviderHealthBackgroundService(cacheA, healthOptions, leaseStore, singletonOptions);
        var serviceB = new ModelProviderHealthBackgroundService(cacheB, healthOptions, leaseStore, singletonOptions);

        await serviceA.StartAsync(CancellationToken.None);
        await serviceB.StartAsync(CancellationToken.None);

        // 🚨 Wait for the SIGNAL, not for the clock. A fixed delay asserts that a
        // background tick fits inside a wall-clock window, which is a claim about
        // the machine rather than about the code: under a loaded full-suite run
        // neither service got its 60 ms tick within 500 ms and the test failed with
        // providerA=0, providerB=0 — nothing had happened yet, so the XOR below was
        // false for a reason the test was not asking about.
        //
        // The lease comment above records the first half of this same lesson. This
        // is the second half: the timing dependency moved from the lease to the
        // wait, and only removing the wall clock from BOTH closes it.
        await WaitUntilAsync(() => providerA.CheckCount > 0 || providerB.CheckCount > 0);

        await serviceA.StopAsync(CancellationToken.None);
        await serviceB.StopAsync(CancellationToken.None);

        // 🚨 Only one must have run: the total counter > 0, but both CANNOT be
        // > 0 at the same time. The wait above guarantees the ">0" half, so a
        // failure here is the real claim failing — both instances ran.
        (providerA.CheckCount > 0 ^ providerB.CheckCount > 0).ShouldBeTrue(
            $"providerA={providerA.CheckCount}, providerB={providerB.CheckCount}");
    }

    /// <summary>Polls until <paramref name="condition"/> holds, or fails the test.</summary>
    /// <param name="condition">The signal to wait for.</param>
    /// <remarks>
    /// The timeout is generous on purpose. It is not a performance budget — it only
    /// bounds the failure so a broken singleton reports instead of hanging. Waiting
    /// longer than necessary costs nothing when the condition is already true.
    /// </remarks>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException(
            "No health check ran on either instance within 30 seconds; the background service never ticked.");
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) => new(value);

    /// <summary>Fake model provider that tracks how many times it was called.</summary>
    private sealed class CountingHealthProvider(string name) : IModelProvider, IModelProviderHealthCheck
    {
        public int CheckCount { get; private set; }

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public IChatClient CreateChatClient(ModelBinding binding) => throw new NotSupportedException();

        public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;

            return new ValueTask<ModelProviderHealth>(new ModelProviderHealth
            {
                ProviderName = Name,
                Status = ModelProviderHealthStatus.Healthy,
                CheckedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}
