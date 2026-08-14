using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// HATA-S4-020 (MT-OBS-036): <c>QuotaUsageObserver</c> DI'dan cozuldugunde
/// <c>AgentPrism:Observability:EnableQuotaUsageGauge</c> yapilandirmasini
/// GERCEKTEN gormeli.
/// </summary>
/// <remarks>
/// Kok neden DI kaydindaydi: <c>AgentPrismObservabilityOptions</c> standalone
/// (<c>IOptionsMonitor&lt;AgentPrismObservabilityOptions&gt;</c>) hicbir yerde
/// <c>services.Configure&lt;AgentPrismObservabilityOptions&gt;</c> ile kayitli
/// DEGILDI; konteyner GERCEKTEN yapilandirilmis olsa dahi bu tur icin her zaman
/// varsayilan (kapali) bir ornek uretiyordu. <see cref="QuotaUsageObserverTests"/>
/// bunu YAKALAMAZ cunku direkt kurucu cagrisi kullanir, DI cozumlemesini hic
/// tetiklemez. Duzeltme: gozlemci artik <c>IOptionsMonitor&lt;AgentPrismOptions&gt;</c>
/// enjekte eder (zaten <c>Configure&lt;AgentPrismOptions&gt;</c> ile dogru
/// baglanan tur) ve <c>Observability</c> alt ozelligini okur.
/// </remarks>
public sealed class QuotaUsageObserverRegistrationTests
{
    [Fact]
    public void DI_uzerinden_cozulen_observer_yapilandirilmis_bayragi_gorur()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AgentPrism:Observability:EnableQuotaUsageGauge"] = "true",
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddAgentPrism(configuration.GetSection(AgentPrismOptions.SectionName));

        using var provider = services.BuildServiceProvider();

        var observer = provider.GetServices<IHostedService>().OfType<QuotaUsageObserver>().Single();

        var field = typeof(QuotaUsageObserver).GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("QuotaUsageObserver._options bulunamadi — alan adi degisti mi?");

        var monitor = (IOptionsMonitor<AgentPrismOptions>)field.GetValue(observer)!;

        monitor.CurrentValue.Observability.EnableQuotaUsageGauge.ShouldBeTrue();
    }

    [Fact]
    public void DI_uzerinden_cozulen_observer_varsayilanda_kapali_gorur()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();

        using var provider = services.BuildServiceProvider();

        var observer = provider.GetServices<IHostedService>().OfType<QuotaUsageObserver>().Single();

        var field = typeof(QuotaUsageObserver).GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var monitor = (IOptionsMonitor<AgentPrismOptions>)field.GetValue(observer)!;

        monitor.CurrentValue.Observability.EnableQuotaUsageGauge.ShouldBeFalse();
    }
}
