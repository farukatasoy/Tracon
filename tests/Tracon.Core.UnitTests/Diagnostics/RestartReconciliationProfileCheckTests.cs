using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The decision about a run that a restart cuts off: asked of a host whose state outlives the process and whose run
/// reconciliation is off, and of no other.
/// </summary>
/// <remarks>
/// The judgement of the check on its own. What a real container resolves, and what the host does with the answer, is
/// proven in <c>ProductionProfileStartupTests</c>.
/// </remarks>
public sealed class RestartReconciliationProfileCheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void State_that_a_restart_takes_with_it_leaves_nothing_to_decide(bool reconciliationOn)
    {
        using var provider = Provider(instances: 1, StateSharing.InProcessMemory, reconciliationOn);

        var result = new RestartReconciliationProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.NotApplicable);
        result.Observed.ShouldContain("memory of one process", Case.Sensitive);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void State_that_outlives_the_process_with_reconciliation_off_is_permissive(string kind)
    {
        var sharing = Persistent(kind);
        using var provider = Provider(instances: 1, sharing, reconciliationOn: false);

        var result = new RestartReconciliationProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.Permissive);
        result.Setting.ShouldBe("Tracon:RunReconciliation:Enabled");
        result.Observed.ShouldContain(sharing.Description, Case.Sensitive);
        result.Remedy.ShouldContain("Tracon:RunReconciliation:Enabled", Case.Sensitive);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    public void State_that_outlives_the_process_with_reconciliation_on_is_satisfied(string kind)
    {
        using var provider = Provider(instances: 1, Persistent(kind), reconciliationOn: true);

        new RestartReconciliationProfileCheck().Evaluate(provider).State.ShouldBe(ProductionProfileState.Satisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_deployment_of_several_instances_is_asked_the_instance_decision_instead(bool reconciliationOn)
    {
        using var provider = Provider(instances: 2, StateSharing.DatabaseServer("PostgreSQL"), reconciliationOn);

        var result = new RestartReconciliationProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.NotApplicable);
        result.Observed.ShouldContain(nameof(TraconProductionRisk.UnreconciledRuns), Case.Sensitive);
        new InstanceReconciliationProfileCheck().Evaluate(provider).State.ShouldBe(
            reconciliationOn ? ProductionProfileState.Satisfied : ProductionProfileState.Permissive);
    }

    [Fact]
    public void The_two_decisions_about_one_setting_are_never_open_together()
    {
        foreach (var instances in new[] { 1, 2 })
        {
            foreach (var sharing in new[] { StateSharing.InProcessMemory, StateSharing.SqliteFile, StateSharing.DatabaseServer("SQL Server") })
            {
                using var provider = Provider(instances, sharing, reconciliationOn: false);

                var open = new IProductionProfileCheck[] { new RestartReconciliationProfileCheck(), new InstanceReconciliationProfileCheck() }
                    .Count(check => check.Evaluate(provider).State == ProductionProfileState.Permissive);

                open.ShouldBeLessThanOrEqualTo(1, $"{instances} instance(s), state {sharing.Description}");
            }
        }
    }

    private static StateSharing Persistent(string kind)
        => kind switch
        {
            "sqlite" => StateSharing.SqliteFile,
            "postgresql" => StateSharing.DatabaseServer("PostgreSQL"),
            "sqlserver" => StateSharing.DatabaseServer("SQL Server"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown state."),
        };

    private static ServiceProvider Provider(int instances, StateSharing sharing, bool reconciliationOn)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(new TraconOptions { Deployment = { Instances = instances } }));
        services.AddSingleton(sharing);
        services.AddSingleton(Options.Create(new RunReconciliationOptions { Enabled = reconciliationOn }));

        return services.BuildServiceProvider();
    }
}
