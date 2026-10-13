using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// F-321: <see cref="TraconWorkflowOptions.ResumeInterruptedRuns"/> is off by default,
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
        WorkflowRecoveryPolicy.JobAttempts(policy, asyncRunMaxAttempts: 1).ShouldBe(3);
        WorkflowRecoveryPolicy.JobAttempts(policy: null, asyncRunMaxAttempts: 1).ShouldBe(1);
    }

    [Fact]
    public void Zero_continuations_is_allowed_and_keeps_the_detection()
    {
        using var provider = Build(
            ("Tracon:Workflows:ResumeInterruptedRuns", "true"),
            ("Tracon:Workflows:MaxResumeAttempts", "0"));

        provider.GetRequiredService<IStartupValidator>().Validate();
        WorkflowRecoveryPolicy.JobAttempts(provider.GetRequiredService<IWorkflowRecoveryPolicy>(), asyncRunMaxAttempts: 5).ShouldBe(1);
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
