using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Coordination;

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
        var singletonOptions = Options(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromSeconds(1),
        });

        var healthOptions = Options(new AgentPrismOptions
        {
            Health = new AgentPrismHealthOptions { BackgroundInterval = TimeSpan.FromMilliseconds(60) },
        });

        var providerA = new CountingHealthProvider("providerA");
        var providerB = new CountingHealthProvider("providerB");

        var cacheA = new ModelProviderHealthCache([providerA], healthOptions);
        var cacheB = new ModelProviderHealthCache([providerB], healthOptions);

        var serviceA = new ModelProviderHealthBackgroundService(cacheA, healthOptions, leaseStore, singletonOptions);
        var serviceB = new ModelProviderHealthBackgroundService(cacheB, healthOptions, leaseStore, singletonOptions);

        await serviceA.StartAsync(CancellationToken.None);
        await serviceB.StartAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await serviceA.StopAsync(CancellationToken.None);
        await serviceB.StopAsync(CancellationToken.None);

        // 🚨 Only one must have run: the total counter > 0, but both CANNOT be
        // > 0 at the same time.
        (providerA.CheckCount > 0 ^ providerB.CheckCount > 0).ShouldBeTrue(
            $"providerA={providerA.CheckCount}, providerB={providerB.CheckCount}");
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) => new(value);

    /// <summary>Fake model provider that tracks how many times it was called.</summary>
    private sealed class CountingHealthProvider(string name) : IModelProvider, IModelProviderHealthCheck
    {
        public int CheckCount { get; private set; }

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null) => throw new NotSupportedException();

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
