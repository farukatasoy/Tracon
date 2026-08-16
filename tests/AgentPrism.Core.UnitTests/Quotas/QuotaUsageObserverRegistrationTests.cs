using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// HATA-S4-020 (MT-OBS-036): when <c>QuotaUsageObserver</c> is resolved from
/// DI, it must ACTUALLY see the <c>AgentPrism:Observability:EnableQuotaUsageGauge</c>
/// configuration.
/// </summary>
/// <remarks>
/// The root cause was in the DI registration: the standalone
/// <c>AgentPrismObservabilityOptions</c> (<c>IOptionsMonitor&lt;AgentPrismObservabilityOptions&gt;</c>)
/// was NEVER registered anywhere with
/// <c>services.Configure&lt;AgentPrismObservabilityOptions&gt;</c>; the container
/// always produced a default (disabled) instance for that type even when it was
/// genuinely configured. <see cref="QuotaUsageObserverTests"/> does NOT catch
/// this because it calls the constructor directly and never triggers DI
/// resolution. Fix: the observer now injects
/// <c>IOptionsMonitor&lt;AgentPrismOptions&gt;</c> (the type that is already
/// correctly wired via <c>Configure&lt;AgentPrismOptions&gt;</c>) and reads the
/// <c>Observability</c> sub-property.
/// </remarks>
public sealed class QuotaUsageObserverRegistrationTests
{
    [Fact]
    public void Observer_resolved_via_DI_sees_the_configured_flag()
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
            ?? throw new InvalidOperationException("QuotaUsageObserver._options not found — did the field name change?");

        var monitor = (IOptionsMonitor<AgentPrismOptions>)field.GetValue(observer)!;

        monitor.CurrentValue.Observability.EnableQuotaUsageGauge.ShouldBeTrue();
    }

    [Fact]
    public void Observer_resolved_via_DI_sees_the_disabled_default()
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
