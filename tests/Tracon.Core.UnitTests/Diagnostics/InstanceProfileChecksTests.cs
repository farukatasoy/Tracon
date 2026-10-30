using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Tracon.Core.UnitTests.Configuration;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The production decisions that exist only when the deployment declares more than one
/// instance (<c>Tracon:Deployment:Instances</c>).
/// </summary>
/// <remarks>
/// The unit level covers the judgement of each check on its own. What a real container resolves, and what the
/// host does with the answers, crosses a dependency-injection boundary and is proven in
/// <c>ProductionProfileStartupTests</c> instead.
/// </remarks>
public sealed class InstanceProfileChecksTests
{
    [Fact]
    public void The_declaration_defaults_to_one_instance()
    {
        new TraconOptions().Deployment.Instances.ShouldBe(1);
    }

    [Fact]
    public void The_instance_count_is_read_from_configuration()
    {
        var options = SectionBinding.Bind<TraconOptions>(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Deployment:Instances"] = "2" });

        options.Deployment.Instances.ShouldBe(2);
    }

    [Fact]
    public void An_absent_section_leaves_one_instance()
    {
        SectionBinding.Bind<TraconOptions>(SectionBinding.NoKeys).Deployment.Instances.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void An_instance_count_below_one_is_rejected_at_start(int instances)
    {
        var options = new TraconOptions();
        options.Deployment.Instances = instances;

        var result = new TraconOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(TraconDeploymentOptions.Instances), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void An_instance_count_of_one_or_more_is_accepted(int instances)
    {
        var options = new TraconOptions();
        options.Deployment.Instances = instances;

        new TraconOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    public static TheoryData<IProductionProfileCheck> AllCoreChecks() =>
    [
        new InstanceStateProfileCheck(),
        new InstanceReconciliationProfileCheck(),
        new InstanceBackgroundWorkProfileCheck(),
        new InstanceRateLimitProfileCheck(),
    ];

    [Theory]
    [MemberData(nameof(AllCoreChecks))]
    public void One_declared_instance_is_never_asked_anything(IProductionProfileCheck check)
    {
        // Every setting is at its permissive default and the check still does not object: a host that never
        // declared a second instance is asked exactly what it was asked before the declaration existed.
        using var provider = Provider(instances: 1);

        var result = check.Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.NotApplicable);
        result.Setting.ShouldBe("Tracon:Deployment:Instances");
    }

    [Fact]
    public void A_store_that_serves_one_process_is_refused_for_two_instances()
    {
        using var provider = Provider(instances: 2, StateSharing.SqliteFile);

        var result = new InstanceStateProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.Permissive);
        result.Observed.ShouldContain("2 instances", Case.Sensitive);
        result.Observed.ShouldContain("SQLite", Case.Sensitive);
        result.Remedy.ShouldContain("UsePostgreSql", Case.Sensitive);
    }

    [Fact]
    public void The_in_memory_stores_are_refused_for_two_instances()
    {
        using var provider = Provider(instances: 2);

        new InstanceStateProfileCheck().Evaluate(provider).State.ShouldBe(ProductionProfileState.Permissive);
    }

    [Fact]
    public void A_database_server_satisfies_the_state_decision()
    {
        using var provider = Provider(instances: 2, StateSharing.DatabaseServer("PostgreSQL"));

        new InstanceStateProfileCheck().Evaluate(provider).State.ShouldBe(ProductionProfileState.Satisfied);
    }

    [Theory]
    [InlineData(false, ProductionProfileState.Permissive)]
    [InlineData(true, ProductionProfileState.Satisfied)]
    public void Reconciliation_must_be_on_for_two_instances(bool enabled, ProductionProfileState expected)
    {
        using var provider = Provider(
            instances: 2,
            configure: services => services.Configure<RunReconciliationOptions>(options => options.Enabled = enabled));

        var result = new InstanceReconciliationProfileCheck().Evaluate(provider);

        result.State.ShouldBe(expected);
        result.Setting.ShouldBe("Tracon:RunReconciliation:Enabled");
    }

    [Theory]
    [InlineData(false, ProductionProfileState.Permissive)]
    [InlineData(true, ProductionProfileState.Satisfied)]
    public void Single_executor_election_must_be_on_for_two_instances(bool enabled, ProductionProfileState expected)
    {
        using var provider = Provider(
            instances: 2,
            configure: services => services.Configure<SingletonExecutionOptions>(options => options.Enabled = enabled));

        var result = new InstanceBackgroundWorkProfileCheck().Evaluate(provider);

        result.State.ShouldBe(expected);
        result.Setting.ShouldBe("Tracon:SingletonExecution:Enabled");
    }

    [Theory]
    [InlineData(true, RateLimitScope.Instance, ProductionProfileState.Permissive)]
    [InlineData(true, RateLimitScope.Deployment, ProductionProfileState.Satisfied)]
    [InlineData(false, RateLimitScope.Instance, ProductionProfileState.Permissive)]
    [InlineData(false, RateLimitScope.Deployment, ProductionProfileState.Satisfied)]
    public void The_rate_limit_scope_decides_whether_or_not_the_endpoint_limit_is_on(
        bool limited,
        RateLimitScope scope,
        ProductionProfileState expected)
    {
        using var provider = Provider(
            instances: 2,
            configure: services => services.Configure<TraconRateLimitOptions>(options =>
            {
                options.Enabled = limited;
                options.Scope = scope;
            }));

        var result = new InstanceRateLimitProfileCheck().Evaluate(provider);

        result.State.ShouldBe(expected);
        result.Setting.ShouldBe("Tracon:RateLimit:Scope");
    }

    [Fact]
    public void With_the_endpoint_limit_off_the_message_names_the_inbound_trigger_limit_that_is_always_on()
    {
        // The inbound trigger limiter is always on and follows the scope, so a deployment that turned the endpoint limit
        // off still admits the trigger limit once per instance.
        using var provider = Provider(
            instances: 2,
            configure: services => services.Configure<TraconRateLimitOptions>(options => options.Enabled = false));

        var result = new InstanceRateLimitProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.Permissive);
        result.Observed.ShouldContain("inbound trigger limit, which is always on", Case.Sensitive);
        result.Remedy.ShouldContain("accept the risk by name", Case.Sensitive);
    }

    [Fact]
    public void The_rate_limit_message_counts_the_declared_instances()
    {
        using var provider = Provider(
            instances: 3,
            configure: services => services.Configure<TraconRateLimitOptions>(options => options.Enabled = true));

        new InstanceRateLimitProfileCheck().Evaluate(provider).Observed.ShouldContain("3 times over", Case.Sensitive);
    }

    private static ServiceProvider Provider(
        int instances,
        StateSharing? sharing = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(new TraconOptions { Deployment = { Instances = instances } }));
        services.AddSingleton(sharing ?? StateSharing.InProcessMemory);
        services.AddOptions<RunReconciliationOptions>();
        services.AddOptions<SingletonExecutionOptions>();
        services.AddOptions<TraconRateLimitOptions>();
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
