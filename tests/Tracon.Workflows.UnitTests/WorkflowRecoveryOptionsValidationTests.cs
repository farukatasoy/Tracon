using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="TraconWorkflowOptions.ResumeInterruptedRuns"/> is off by default,
/// needs checkpoints, and reaches the job handlers through the seam <c>UseWorkflows</c>
/// registers.
/// </summary>
/// <remarks>
/// Recovery resumes from checkpoints: turned on while checkpointing is off, every
/// interrupted run would find nothing to resume from, long after the host started.
/// The cases go through <c>UseWorkflows</c> to <see cref="IStartupValidator"/>.
/// </remarks>
public sealed class WorkflowRecoveryOptionsValidationTests
{
    [Fact]
    public void Recovery_is_off_by_default_and_allows_one_continuation()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value;
        options.ResumeInterruptedRuns.ShouldBeFalse();
        options.MaxResumeAttempts.ShouldBe(1);

        var policy = provider.GetRequiredService<IWorkflowRecoveryPolicy>();
        policy.ResumeInterruptedRuns.ShouldBeFalse();
        WorkflowRecoveryPolicy.IsOn(policy).ShouldBeFalse();
    }

    [Fact]
    public void Recovery_without_checkpoints_fails_at_startup_naming_both_settings()
    {
        using var provider = Build(
            ("Tracon:Workflows:ResumeInterruptedRuns", "true"),
            ("Tracon:Workflows:EnableCheckpointing", "false"));

        var message = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate()).Message;

        message.ShouldContain(nameof(TraconWorkflowOptions.ResumeInterruptedRuns));
        message.ShouldContain(nameof(TraconWorkflowOptions.EnableCheckpointing));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public void A_continuation_limit_out_of_range_fails_at_startup(string value)
    {
        using var provider = Build(("Tracon:Workflows:MaxResumeAttempts", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.MaxResumeAttempts));
    }

    [Fact]
    public void The_seam_reads_the_configured_values_and_gives_a_queued_run_one_lease_per_continuation()
    {
        using var provider = Build(
            ("Tracon:Workflows:ResumeInterruptedRuns", "true"),
            ("Tracon:Workflows:MaxResumeAttempts", "2"));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var policy = provider.GetRequiredService<IWorkflowRecoveryPolicy>();

        policy.ResumeInterruptedRuns.ShouldBeTrue();
        policy.MaxResumeAttempts.ShouldBe(2);
        WorkflowRecoveryPolicy.JobAttempts(policy, asyncRunMaxAttempts: 1).ShouldBe(4);
        WorkflowRecoveryPolicy.JobAttempts(policy: null, asyncRunMaxAttempts: 1).ShouldBe(1);
    }

    /// <summary>
    /// Zero continuations still needs a second attempt: the worker fails a lease past
    /// the limit before the handler runs, and only the handler on the lease after the
    /// interruption can find the run and close it. The end-to-end proof, through the
    /// worker's own attempt check, is
    /// <c>WorkflowRecoveryAttemptLimitTests.With_no_continuation_allowed_the_lease_after_the_interruption_closes_the_run_Failed</c>.
    /// </summary>
    [Fact]
    public void Zero_continuations_is_allowed_and_leaves_the_job_the_attempt_that_closes_the_interrupted_run()
    {
        using var provider = Build(
            ("Tracon:Workflows:ResumeInterruptedRuns", "true"),
            ("Tracon:Workflows:MaxResumeAttempts", "0"));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var policy = provider.GetRequiredService<IWorkflowRecoveryPolicy>();

        policy.MaxResumeAttempts.ShouldBe(0);
        WorkflowRecoveryPolicy.JobAttempts(policy, asyncRunMaxAttempts: 5).ShouldBe(2, "one attempt for the run, one that finds it interrupted and closes it");
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows();

        return services.BuildServiceProvider();
    }
}
