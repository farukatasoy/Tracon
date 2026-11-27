using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The per-tenant ceiling a host asks for with <c>Tracon:Scheduling:TenantCapHandlerKeys</c> and
/// <c>MaxLeasedPerTenant</c>: off by default, read from configuration, checked at startup, and handed to the worker
/// through <see cref="JobTenantBindings"/> without a service of its own.
/// </summary>
/// <remarks>
/// The ceiling itself is the store's contract and the worker's half is <see cref="JobWorkerTenantCapTests"/>. These cases
/// pin what can go wrong before a lease is asked for: a misspelled handler key that caps nothing, a ceiling that holds
/// every tenant back, and two ceilings that one lease request cannot carry.
/// </remarks>
public sealed class JobTenantCapOptionsTests
{
    private const string Key = "contoso.podcast";

    [Fact]
    public void The_ceiling_is_off_by_default_and_the_worker_asks_for_none()
    {
        var options = new TraconSchedulingOptions();

        options.MaxLeasedPerTenant.ShouldBeNull();
        options.TenantCapHandlerKeys.ShouldBeEmpty();
        Validator().Validate(null, options).Succeeded.ShouldBeTrue();
        JobTenantBindings.Create(capSource: null, hostScope: null, options).CapSource.ShouldBeNull();
    }

    [Fact]
    public void A_ceiling_over_registered_keys_becomes_the_one_pair_the_worker_leases_with()
    {
        var options = new TraconSchedulingOptions { MaxLeasedPerTenant = 2 };
        options.TenantCapHandlerKeys.Add(Key);

        Validator().Validate(null, options).Succeeded.ShouldBeTrue();

        var cap = JobTenantBindings.Create(capSource: null, hostScope: null, options).CapSource.ShouldNotBeNull().Current.ShouldNotBeNull();

        cap.HandlerKeys.ShouldBe([Key]);
        cap.MaxLeasedPerTenant.ShouldBe(2);
    }

    [Fact]
    public void A_key_no_handler_is_registered_for_is_refused_by_name()
    {
        // A typo caps nothing and says nothing: the worker would lease in plain age order and the operator would
        // believe the ceiling holds.
        var options = new TraconSchedulingOptions { MaxLeasedPerTenant = 2 };
        options.TenantCapHandlerKeys.Add("contoso.podcats");

        var result = Validator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("contoso.podcats");
        result.FailureMessage.ShouldContain(nameof(TraconSchedulingOptions.TenantCapHandlerKeys));
    }

    [Fact]
    public void A_key_that_is_not_a_valid_handler_key_is_refused()
    {
        var options = new TraconSchedulingOptions { MaxLeasedPerTenant = 2 };
        options.TenantCapHandlerKeys.Add("Contoso Podcast");

        var result = Validator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("'Contoso Podcast'");
        result.FailureMessage.ShouldContain("not a valid job handler key");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_ceiling_below_one_is_refused(int ceiling)
    {
        var options = new TraconSchedulingOptions { MaxLeasedPerTenant = ceiling };
        options.TenantCapHandlerKeys.Add(Key);

        var result = Validator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconSchedulingOptions.MaxLeasedPerTenant));
        result.FailureMessage.ShouldContain("at least 1");
    }

    [Fact]
    public void A_ceiling_without_keys_is_refused_because_it_would_cap_nothing()
    {
        var result = Validator().Validate(null, new TraconSchedulingOptions { MaxLeasedPerTenant = 2 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconSchedulingOptions.TenantCapHandlerKeys));
    }

    [Fact]
    public void Keys_without_a_ceiling_are_refused_because_the_setting_would_do_nothing()
    {
        var options = new TraconSchedulingOptions();
        options.TenantCapHandlerKeys.Add(Key);

        var result = Validator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconSchedulingOptions.MaxLeasedPerTenant));
    }

    [Fact]
    public void The_settings_are_read_from_configuration_and_reach_the_worker_without_a_registered_cap_source()
    {
        using var provider = Build(
            ("Tracon:Scheduling:TenantCapHandlerKeys:0", Key),
            ("Tracon:Scheduling:MaxLeasedPerTenant", "3"));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var cap = provider.GetRequiredService<JobTenantBindings>().CapSource.ShouldNotBeNull().Current.ShouldNotBeNull();

        cap.HandlerKeys.ShouldBe([Key]);
        cap.MaxLeasedPerTenant.ShouldBe(3);

        // 🚨 Not a service: the first registration of IJobTenantCapSource would win, and the workflow engine's own
        // ceiling would be dropped without a word when this one came first.
        provider.GetService<IJobTenantCapSource>().ShouldBeNull();
    }

    [Fact]
    public void A_misspelled_key_in_configuration_stops_the_host_at_startup()
    {
        using var provider = Build(
            ("Tracon:Scheduling:TenantCapHandlerKeys:0", "contoso.podcats"),
            ("Tracon:Scheduling:MaxLeasedPerTenant", "3"));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("contoso.podcats");
    }

    [Fact]
    public void With_nothing_configured_the_worker_asks_for_no_ceiling()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();

        provider.GetRequiredService<JobTenantBindings>().CapSource.ShouldBeNull();
    }

    [Fact]
    public void A_ceiling_beside_the_workflow_engines_ceiling_stops_the_host_and_names_both_settings()
    {
        // One lease request carries one ceiling. The engine's source is registered by its own package.
        using var provider = Build(
            [new FixedCapSource(new JobTenantCap { HandlerKeys = [JobHandlerKeys.Workflow], MaxLeasedPerTenant = 2 })],
            ("Tracon:Scheduling:TenantCapHandlerKeys:0", Key),
            ("Tracon:Scheduling:MaxLeasedPerTenant", "3"));

        var message = Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<JobTenantBindings>()).Message;

        message.ShouldContain("Tracon:Scheduling:MaxLeasedPerTenant");
        message.ShouldContain("Tracon:Workflows:MaxConcurrentRunsPerTenant");
    }

    [Fact]
    public void A_cap_source_with_no_ceiling_beside_the_setting_is_not_a_conflict()
    {
        // The workflow engine registers its source whenever it is on; the ceiling it holds is off by default.
        using var provider = Build(
            [new FixedCapSource(cap: null)],
            ("Tracon:Scheduling:TenantCapHandlerKeys:0", Key),
            ("Tracon:Scheduling:MaxLeasedPerTenant", "3"));

        provider.GetRequiredService<JobTenantBindings>().CapSource.ShouldNotBeNull().Current.ShouldNotBeNull().MaxLeasedPerTenant.ShouldBe(3);
    }

    [Fact]
    public void The_workflow_engines_ceiling_alone_is_left_as_it_is()
    {
        var cap = new JobTenantCap { HandlerKeys = [JobHandlerKeys.Workflow], MaxLeasedPerTenant = 2 };
        var source = new FixedCapSource(cap);

        JobTenantBindings.Create(source, hostScope: null, new TraconSchedulingOptions()).CapSource.ShouldBeSameAs(source);
    }

    private static TraconSchedulingOptionsValidator Validator()
        => new(JobHandlerRegistry.Create([new JobHandlerRegistration(Key, typeof(NoopHandler), BuiltIn: false)]));

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
        => Build([], settings);

    private static ServiceProvider Build(IJobTenantCapSource[] capSources, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        foreach (var source in capSources)
        {
            services.AddSingleton(source);
        }

        services.AddTracon(configuration.GetSection("Tracon")).AddJobHandler<NoopHandler>(Key);

        return services.BuildServiceProvider();
    }

    private sealed class FixedCapSource(JobTenantCap? cap) : IJobTenantCapSource
    {
        public JobTenantCap? Current => cap;
    }

    private sealed class NoopHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
