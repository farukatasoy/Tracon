using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// What the retention check tells an operator: switching the configuration on, or writing a policy, decides what is
/// eligible for deletion, and a job is what deletes it.
/// </summary>
public sealed class RetentionProfileCheckTests
{
    [Fact]
    public void Disabled_retention_is_permissive_and_the_remedy_names_the_job_that_deletes()
    {
        var result = new RetentionProfileCheck().Evaluate(Services(enabled: false));

        result.State.ShouldBe(ProductionProfileState.Permissive);
        result.Remedy.ShouldContain("tracon.retention", Case.Sensitive);
        result.Remedy.ShouldContain("nothing schedules it", Case.Sensitive);
        result.Remedy.ShouldContain("tracon.retention.fanout", Case.Sensitive);
        result.Remedy.ShouldContain("only the tenant it was queued for", Case.Sensitive);
        result.Remedy.ShouldContain("job worker", Case.Sensitive);
    }

    [Fact]
    public void Enabled_retention_is_satisfied()
        => new RetentionProfileCheck().Evaluate(Services(enabled: true)).State.ShouldBe(ProductionProfileState.Satisfied);

    private static ServiceProvider Services(bool enabled)
        => new ServiceCollection()
            .AddOptions()
            .Configure<TraconRetentionOptions>(options => options.Enabled = enabled)
            .BuildServiceProvider();
}
