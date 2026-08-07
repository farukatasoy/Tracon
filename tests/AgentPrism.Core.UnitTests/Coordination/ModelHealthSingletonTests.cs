using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Coordination;

/// <summary>
/// Iki gercek <see cref="ModelProviderHealthBackgroundService"/> orneginin AYNI
/// kira deposunu paylastigi uctan uca senaryo (Faz 42, DoD: "Iki ornek ...
/// model saglik yoklamasi yalniz birinde kosar"). Gercek zaman kullanir:
/// servisler kendi <c>PeriodicTimer</c>'lariyla calisir, sahte zamanlayici
/// enjekte edilmez (bu proje BackgroundService'leri boyle test etmez, bkz.
/// <c>MEMORY.md</c> — "Birim testi yetmez").
/// </summary>
public sealed class ModelHealthSingletonTests
{
    [Fact]
    public async Task Saglik_yoklamasi_yalniz_bir_ornekte_kosar()
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

        // 🚨 Yalniz biri calismis olmalidir: toplam sayac > 0, ama ikisi de
        // ayni anda > 0 OLAMAZ.
        (providerA.CheckCount > 0 ^ providerB.CheckCount > 0).ShouldBeTrue(
            $"providerA={providerA.CheckCount}, providerB={providerB.CheckCount}");
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) => new(value);

    /// <summary>Cagrildigi sayiyi tutan sahte model saglayicisi.</summary>
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
