using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every file in <c>src/</c> that arms a timeout with <c>CancelAfter(</c>,
/// <c>new CancellationTokenSource(&lt;value&gt;</c> or the <c>Task.Delay</c> overload
/// that takes a <see cref="TimeProvider"/> must be a known site whose value is either a
/// constant or checked against the timer range before it is armed (F-280, F-449).
/// </summary>
/// <remarks>
/// <para>
/// All three refuse a negative value or more than about 49.7 days, and they run
/// where the timeout is armed: in a run, a request, a delivery attempt, a shutdown.
/// The class scan found eighteen inputs that reached them with only a lower bound -
/// the same defect <c>PeriodicTimerSiteTests</c> closed for background intervals
/// (F-278), one call further along.
/// </para>
/// <para>
/// The gate does not read the code: it makes a NEW site fail until someone names
/// the value that feeds it and the check that bounds it with <c>TimerPeriod.IsValid</c>
/// (the checks themselves are proven by <c>TimeoutOptionValidationTests</c> and the
/// package tests named below). A site that disappears fails too, so the list cannot
/// go stale.
/// </para>
/// </remarks>
public sealed partial class TimeoutSiteTests
{
    private static readonly Dictionary<string, string> KnownSites = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Cli/Commands/EvalCommand.cs"] = "--timeout — ParseOptionalSeconds (EvalCommandTests)",
        ["src/Tracon.Cli/Commands/HealthCommand.cs"] = "constant 10 s",
        ["src/Tracon.Core/Approvals/ToolApprovalPresenterRunner.cs"] = "TraconToolOptions.ApprovalPresentationTimeout — TraconOptionsValidator",
        ["src/Tracon.Core/Compilation/AgentDefinitionValidator.cs"] = "TraconValidationOptions.McpTimeout — TraconOptionsValidator",
        ["src/Tracon.Core/Evaluation/OnlineEvalJobHandler.cs"] = "OnlineEvaluationOptions.JudgeTimeout — OnlineEvaluationOptionsValidator (at most 5 min)",
        ["src/Tracon.Core/Graph/ChildAgentInvoker.cs"] = "SubAgentSettings / TraconAgentGraphOptions ChildDeadline / WorkflowNodeReference.Timeout — compiler, TraconOptionsValidator, WorkflowDefinitionValidator, constructor guard",
        ["src/Tracon.Core/Scheduling/JobWorkerBackgroundService.cs"] = "JobRecord.MaxDuration / TraconSchedulingOptions.MaxDurationByHandlerKey — JobDispatcher guard, TraconSchedulingOptionsValidator, worker check before arming (JobWorkerDeadlineTests); the finalization retry delay is a constant ceiling cut to the lease window left",
        ["src/Tracon.Core/Hosting/TraconDrainService.cs"] = "TraconDrainOptions.Timeout — TraconDrainOptionsValidator",
        ["src/Tracon.Core/Skills/Scripts/SandboxedSkillScriptRunner.cs"] = "TraconSkillScriptOptions.Timeout + constant 5 s executor grace — TraconOptionsValidator (sum in range)",
        ["src/Tracon.Core/Skills/Scripts/SkillScriptProcessRunner.cs"] = "TraconSkillScriptOptions.Timeout — TraconOptionsValidator",
        ["src/Tracon.Core/Voice/LiveVoiceSessionHost.cs"] = "VoiceLiveOptions.DelegationTimeout — VoiceLiveOptionsValidator",
        ["src/Tracon.Core/Voice/VoiceConversationDriver.cs"] = "VoiceConversationOptions.IdleTimeout / MaxConnectionDuration — VoiceConversationOptionsValidator",
        ["src/Tracon.Core/Webhooks/WebhookDeliveryJobHandler.cs"] = "TraconWebhookOptions.Timeout — TraconWebhookOptionsValidator",
        ["src/Tracon.Mcp/Internal/McpConnection.cs"] = "TraconMcpOptions.ConnectionTimeout — TraconMcpOptionsValidator",
        ["src/Tracon.Mcp/Internal/McpShortLivedConnection.cs"] = "TraconMcpOptions.ConnectionTimeout — TraconMcpOptionsValidator",
        ["src/Tracon.Mcp/McpOAuthAuthorizationCoordinator.cs"] = "TraconMcpOptions.ConnectionTimeout — TraconMcpOptionsValidator",
        ["src/Tracon.OpenAI/Live/OpenAILiveSideband.cs"] = "constant 10 s",
        ["src/Tracon.Providers.Shared/ProviderHealthCheckCore.cs"] = "provider Timeout — the four provider validators (HTTP range, ProviderTimeoutValidationTests)",
        ["src/Tracon.Workflows/Internal/WorkflowRunner.cs"] = "TraconWorkflowOptions.RunTimeout / WorkflowDefinition.RunTimeout — TraconWorkflowOptionsValidator, WorkflowDefinitionValidator (save and compile), ceiling cut",
        ["src/Tracon.AspNetCore/Streaming/SseWriter.cs"] = "TraconEndpointOptions.StreamKeepAliveInterval — the property setter (TimerPeriod.IsValid); the delay is the part of it still to wait",
        ["src/Tracon.Core/Scheduling/JobOutcomeWriteRetry.cs"] = "constant 5 s ceiling (MaxDelay), cut to the lease window left",
        ["src/Tracon.Core/Scheduling/QueuedTurn.cs"] = "constant 5 s ceiling (RetryMaxDelay), cut to the lease window left",
        ["src/Tracon.Core/Tools/TimeoutAIFunction.cs"] = "per-tool timeout — constructor guard (TimerPeriod.IsValid)",
        ["src/Tracon.Core/Voice/LiveVoiceSessionRegistry.cs"] = "constant 5 s sweep",
        ["src/Tracon.Workflows/Internal/WorkflowNodeRetry.cs"] = "WorkflowNodeRetryPolicy.InitialDelay — Wrap guard; the doubling is capped at TimerPeriod.MaxMilliseconds",
        ["src/Tracon.Workflows/Internal/WorkflowNodeTimeout.cs"] = "WorkflowFunctionOptions.Timeout — Wrap guard (TimerPeriod.IsValid)",
    };

    [Fact]
    public void Every_timeout_site_is_known_and_bounded_before_it_is_armed()
    {
        var root = FindRepositoryRoot();

        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => TimeoutCall().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = actual.Where(path => !KnownSites.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var gone = KnownSites.Keys.Where(path => !actual.Contains(path)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A new timeout site was added. Bound the value that feeds it with TimerPeriod.IsValid where it is " +
            "configured (a validator registered with ValidateOnStart, or the guard where code passes it), prove " +
            "it with a test, then add the file here with that value's name.");
        gone.ShouldBeEmpty("A known timeout site no longer arms a timeout; remove it from the list.");
    }

    // `CancelAfter(` anywhere, a CancellationTokenSource constructed WITH an argument (the
    // parameterless one arms nothing), or `Task.Delay(` whose arguments name a time source.
    // The last one is a heuristic on the argument's name: Tracon passes its TimeProvider as
    // timeProvider, _timeProvider or _clock. A delay with no time source argument is not
    // matched (F-449).
    [GeneratedRegex(
        @"\.CancelAfter\(|new\s+CancellationTokenSource\(\s*[^)\s]|Task\.Delay\([^;]*(?:[Tt]imeProvider|[Cc]lock)\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex TimeoutCall();

    // The call is spelled in two pieces: TestDelayClassificationTests flags a literal
    // `Task.Delay(` in any test file, and these strings are sample source, not a delay.
    private const string DelayCall = "Task" + ".Delay(";

    [Theory]
    [InlineData("await " + DelayCall + "delay, _clock, cancellationToken)")]
    [InlineData("var d = " + DelayCall + "timeout, timeProvider ?? TimeProvider.System, CancellationToken.None);")]
    [InlineData("await " + DelayCall + "\n    interval - silence,\n    _timeProvider,\n    token)")]
    public void The_pattern_sees_the_Task_Delay_overload_that_takes_a_time_source(string source)
        => TimeoutCall().IsMatch(source).ShouldBeTrue();

    [Theory]
    [InlineData("var wait = " + DelayCall + "Timeout.InfiniteTimeSpan, cancellationToken);")]
    [InlineData("await " + DelayCall + "options.BatchDelay, cancellationToken);")]
    public void The_pattern_leaves_a_delay_with_no_time_source_to_the_other_gates(string source)
        => TimeoutCall().IsMatch(source).ShouldBeFalse();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
