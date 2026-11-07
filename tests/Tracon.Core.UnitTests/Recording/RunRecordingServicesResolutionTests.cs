using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The container builds every member of <see cref="RunRecordingServices"/>. A member that stays
/// <see langword="null"/> switches its feature off without an error: the quota counter stops, no
/// <c>run.*</c> event is published, a run records no user. The build and the unit tests of the recorder
/// stay green in every one of those cases, so this is the test that notices.
/// </summary>
public sealed class RunRecordingServicesResolutionTests
{
    // Off by configuration, not by a missing registration: no currency is set unless the host sets one.
    private static readonly string[] OffByConfiguration = [nameof(RunRecordingServices.PricingCurrency)];

    [Fact]
    public void The_container_fills_every_member_of_the_services()
    {
        using var provider = BuildProvider();

        var services = provider.GetRequiredService<RunRecordingServices>();

        // Walks the members instead of listing them: a member added to the services later and left out of Create
        // is null here, so it fails this test instead of switching its feature off in silence.
        var unset = typeof(RunRecordingServices)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => !property.PropertyType.IsValueType)
            .Where(static property => !OffByConfiguration.Contains(property.Name, StringComparer.Ordinal))
            .Where(property => property.GetValue(services) is null)
            .Select(static property => property.Name)
            .ToList();

        unset.ShouldBeEmpty("The container left these members of the run services null; their features are off.");
    }

    [Fact]
    public void A_registered_clock_reaches_the_services_and_the_system_clock_is_the_default()
    {
        var clock = new ManualClock();
        var registered = new ServiceCollection();
        registered.AddTracon();
        registered.AddSingleton<TimeProvider>(clock);

        using (var withClock = registered.BuildServiceProvider())
        {
            withClock.GetRequiredService<RunRecordingServices>().TimeProvider.ShouldBeSameAs(clock);
        }

        using var withoutClock = BuildProvider();

        withoutClock.GetRequiredService<RunRecordingServices>().TimeProvider.ShouldBeSameAs(TimeProvider.System);
    }

    [Fact]
    public void The_settings_come_from_the_Tracon_options()
    {
        using var provider = BuildProvider(options =>
        {
            options.Observability.IncludeAgentVersionTag = false;
            options.Pricing.Currency = "EUR";
        });

        var services = provider.GetRequiredService<RunRecordingServices>();
        var options = provider.GetRequiredService<IOptions<TraconOptions>>().Value;

        services.Recording.ShouldBeSameAs(options.RunRecording);
        services.AgentGraph.ShouldBeSameAs(options.AgentGraph);
        services.IncludeAgentVersionTag.ShouldBeFalse();
        services.PricingCurrency.ShouldBe("EUR");
    }

    [Fact]
    public void Every_registered_sink_reaches_the_services_in_registration_order()
    {
        var first = new NamedSink();
        var second = new NamedSink();
        using var provider = BuildProvider(configure: null, first, second);

        var services = provider.GetRequiredService<RunRecordingServices>();

        services.Sinks.ShouldBe([first, second]);
    }

    [Fact]
    public void The_services_are_one_singleton_and_the_recording_decorator_is_registered_once()
    {
        using var provider = BuildProvider();

        var services = provider.GetRequiredService<RunRecordingServices>();

        provider.GetRequiredService<RunRecordingServices>().ShouldBeSameAs(services);
        provider.GetServices<IAgentDecorator>().OfType<RunRecordingAgentDecorator>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_control_plane_run_keeps_its_own_record_and_drops_the_side_effects()
    {
        using var provider = BuildProvider();

        var shared = provider.GetRequiredService<RunRecordingServices>();
        var judge = shared.ForControlPlaneRun();

        // The row, the events, the cost and the metrics are the same ones.
        judge.RunStore.ShouldBeSameAs(shared.RunStore);
        judge.TenantContext.ShouldBeSameAs(shared.TenantContext);
        judge.Recording.ShouldBeSameAs(shared.Recording);
        judge.Metrics.ShouldBeSameAs(shared.Metrics);
        judge.TraceCollector.ShouldBeSameAs(shared.TraceCollector);
        judge.PricingResolver.ShouldBeSameAs(shared.PricingResolver);
        judge.ErrorClassifier.ShouldBeSameAs(shared.ErrorClassifier);
        judge.RunInputStore.ShouldBeSameAs(shared.RunInputStore);

        // Nothing that charges the user, tells the outside, samples the run or shows it to an observer.
        judge.QuotaEnforcer.ShouldBeNull();
        judge.WebhookPublisher.ShouldBeNull();
        judge.CancellationRegistry.ShouldBeNull();
        judge.RunSampler.ShouldBeNull();
        judge.ContentGuardPipeline.ShouldBeNull();
        judge.AttributionContext.ShouldBeNull();
        judge.ApprovalPresenterRunner.ShouldBeNull();
        judge.Sinks.ShouldBeEmpty();
        judge.PricingCurrency.ShouldBeNull();
        judge.IncludeAgentVersionTag.ShouldBeFalse();
    }

    private static ServiceProvider BuildProvider(Action<TraconOptions>? configure = null, params IRunEventSink[] sinks)
    {
        var services = new ServiceCollection();
        services.AddTracon();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        foreach (var sink in sinks)
        {
            services.AddSingleton(sink);
        }

        return services.BuildServiceProvider();
    }

    private sealed class NamedSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
