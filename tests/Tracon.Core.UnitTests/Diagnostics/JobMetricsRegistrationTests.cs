using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 133: the DI graph must really hand the worker its
/// <see cref="TraconMetrics"/>.
/// </summary>
/// <remarks>
/// 🚨 This is the phase's silent-failure risk. Adding an OPTIONAL
/// <c>TraconMetrics?</c> parameter to <c>JobWorkerBackgroundService</c>
/// produces no compiler error anywhere: if the container never supplies it, the
/// parameter stays <see langword="null"/> and every job metric is silently
/// never written, while every unit test that passes the dependency by hand
/// stays green. Only resolution from the real container proves the wiring.
/// </remarks>
public sealed class JobMetricsRegistrationTests
{
    [Fact]
    public void The_worker_resolved_via_DI_actually_receives_the_metrics_instance()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        var worker = provider.GetServices<IHostedService>().OfType<JobWorkerBackgroundService>().Single();

        // The primary-constructor parameter is captured in a compiler-generated
        // field; find it by TYPE rather than by name, since that name is not
        // part of any contract.
        var metrics = typeof(JobWorkerBackgroundService)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(TraconMetrics))
            .Select(field => field.GetValue(worker))
            .SingleOrDefault();

        metrics.ShouldNotBeNull("the worker was resolved with a null TraconMetrics; job metrics would be silently dropped");
        metrics.ShouldBeSameAs(provider.GetRequiredService<TraconMetrics>());
    }

    [Fact]
    public void The_queue_depth_observer_is_registered_as_a_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        // Registered as IHostedService for one reason only: so the container
        // builds it EARLY and its ObservableGauge exists to be scraped.
        provider.GetServices<IHostedService>().OfType<JobQueueDepthObserver>().ShouldHaveSingleItem();
    }

    [Fact]
    public void The_metrics_instance_resolved_via_DI_sees_the_configured_lane_limit()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Observability:MaxJobLaneCardinality"] = "7",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        using var provider = services.BuildServiceProvider();

        var metrics = provider.GetRequiredService<TraconMetrics>();

        var field = typeof(TraconMetrics).GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TraconMetrics._options not found — did the field name change?");

        var monitor = (IOptionsMonitor<TraconOptions>?)field.GetValue(metrics);

        monitor.ShouldNotBeNull("the guard would silently fall back to the default limit");
        monitor.CurrentValue.Observability.MaxJobLaneCardinality.ShouldBe(7);
    }

    [Theory]
    [InlineData("Tracon:Observability:EnableJobQueueDepthGauge", "true")]
    [InlineData("Tracon:Observability:JobQueueDepthRefreshInterval", "00:01:15")]
    [InlineData("Tracon:Observability:MaxJobLaneCardinality", "9")]
    public void Every_new_observability_setting_is_actually_bound(string key, string value)
    {
        // An option added to the type but never added to Bind() has been missed
        // three times in this repo; each new key is pinned here.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value })
            .Build();

        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        using var provider = services.BuildServiceProvider();

        var observability = provider.GetRequiredService<IOptionsMonitor<TraconOptions>>().CurrentValue.Observability;

        var actual = key.Split(':')[^1] switch
        {
            nameof(TraconObservabilityOptions.EnableJobQueueDepthGauge) =>
                observability.EnableJobQueueDepthGauge.ToString().ToLowerInvariant(),
            nameof(TraconObservabilityOptions.JobQueueDepthRefreshInterval) =>
                observability.JobQueueDepthRefreshInterval.ToString(),
            _ => observability.MaxJobLaneCardinality.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        actual.ShouldBe(value);
    }
}
