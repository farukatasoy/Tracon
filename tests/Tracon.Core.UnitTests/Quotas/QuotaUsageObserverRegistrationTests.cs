using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// HATA-S4-020 (MT-OBS-036): when <c>QuotaUsageObserver</c> is resolved from
/// DI, it must ACTUALLY see the <c>Tracon:Observability:EnableQuotaUsageGauge</c>
/// configuration.
/// </summary>
/// <remarks>
/// The root cause was in the DI registration: the standalone
/// <c>TraconObservabilityOptions</c> (<c>IOptionsMonitor&lt;TraconObservabilityOptions&gt;</c>)
/// was NEVER registered anywhere with
/// <c>services.Configure&lt;TraconObservabilityOptions&gt;</c>; the container
/// always produced a default (disabled) instance for that type even when it was
/// genuinely configured. <see cref="QuotaUsageObserverTests"/> does NOT catch
/// this because it calls the constructor directly and never triggers DI
/// resolution. Fix: the observer injects <c>IOptionsMonitor&lt;TraconOptions&gt;</c>
/// (the type that is already correctly wired via <c>Configure&lt;TraconOptions&gt;</c>)
/// and reads the <c>Observability</c> sub-property.
/// <para>
/// The observer is a background refresher now, so the flag is proven by what
/// the resolved service DOES: enabled, it completes a refresh; disabled, it
/// returns at once and never refreshes. The earlier version read a private
/// field by reflection, which a refactor could move without the test noticing.
/// </para>
/// </remarks>
public sealed class QuotaUsageObserverRegistrationTests
{
    [Fact]
    public async Task Observer_resolved_via_DI_sees_the_configured_flag()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Observability:EnableQuotaUsageGauge"] = "true",
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        await using var provider = services.BuildServiceProvider();

        var observer = provider.GetServices<IHostedService>().OfType<QuotaUsageObserver>().Single();

        await observer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => observer.CompletedRefreshes >= 1);
        await observer.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Observer_resolved_via_DI_sees_the_disabled_default()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();

        var observer = provider.GetServices<IHostedService>().OfType<QuotaUsageObserver>().Single();

        await observer.StartAsync(TestContext.Current.CancellationToken);
        await observer.ExecuteTask!.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        observer.CompletedRefreshes.ShouldBe(0);
    }
}
