using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every file in <c>src/</c> that arms a timeout with <c>CancelAfter(</c>,
/// <c>new CancellationTokenSource(&lt;value&gt;</c> or the <c>Task.Delay</c> overload
/// that takes a <see cref="TimeProvider"/> must be a known site whose value is either a
/// constant or checked against the timer range before it is armed.
/// </summary>
/// <remarks>
/// <para>
/// All three refuse a negative value or more than about 49.7 days, and they run
/// where the timeout is armed: in a run, a request, a delivery attempt, a shutdown.
/// The class scan found eighteen inputs that reached them with only a lower bound -
/// the same defect <c>PeriodicTimerSiteTests</c> closed for background intervals
/// One call further along.
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
        ["src/Tracon.Cli/Commands/ExportCommand.cs"] = "constant 5 min (DefinitionTransferHttp.Budget) that no operator sets",
        ["src/Tracon.Cli/Commands/HealthCommand.cs"] = "constant 10 s",
        ["src/Tracon.Cli/Commands/ImportCommand.cs"] = "constant 5 min (DefinitionTransferHttp.Budget) that no operator sets",
        ["src/Tracon.Core/Approvals/ToolApprovalPresenterRunner.cs"] = "TraconToolOptions.ApprovalPresentationTimeout — TraconOptionsValidator",
        ["src/Tracon.Core/Compilation/AgentDefinitionValidator.cs"] = "TraconValidationOptions.McpTimeout — TraconOptionsValidator",
        ["src/Tracon.Core/Diagnostics/TaskRace.cs"] = "the wait its callers pass — ChildAgentInvoker's wait limit and OnlineEvaluationOptions.JudgeTimeout, each bounded at its own site (Task.Delay rejects a value outside the timer range)",
        ["src/Tracon.Core/Evaluation/OnlineEvalJobHandler.cs"] = "OnlineEvaluationOptions.JudgeTimeout — OnlineEvaluationOptionsValidator (at most 5 min)",
        ["src/Tracon.Core/Graph/ChildAgentInvoker.cs"] = "SubAgentSettings / TraconAgentGraphOptions ChildDeadline / WorkflowNodeReference.Timeout — compiler, TraconOptionsValidator, WorkflowDefinitionValidator, constructor guard",
        ["src/Tracon.Core/Scheduling/JobWorkerBackgroundService.cs"] = "JobRecord.MaxDuration / TraconSchedulingOptions.MaxDurationByHandlerKey — JobDispatcher guard, TraconSchedulingOptionsValidator, worker check before arming (JobWorkerDeadlineTests); the finalization retry delay is a constant ceiling cut to the lease window left; the renewal call is waited for with LeaseRenewal.CallBudget, which is positive by construction (the proven lease left, never less than half a renewal interval) — the finalization store calls are waited for with FinalizationWindow.CallBudget, one lease duration cut to the longest timer wait; the gate does not scan Task.WaitAsync(TimeSpan, TimeProvider), so those two sites are covered by JobWorkerLeaseContractTests only",
        ["src/Tracon.Core/Hosting/TraconDrainService.cs"] = "TraconDrainOptions.Timeout — TraconDrainOptionsValidator",
        ["src/Tracon.Core/Skills/Scripts/SandboxedSkillScriptRunner.cs"] = "TraconSkillScriptOptions.Timeout + constant 5 s executor grace — TraconOptionsValidator (sum in range)",
        ["src/Tracon.Core/Skills/Scripts/SkillScriptProcessRunner.cs"] = "TraconSkillScriptOptions.Timeout — TraconOptionsValidator",
        ["src/Tracon.Core/Voice/LiveVoiceSessionHost.cs"] = "VoiceLiveOptions.DelegationTimeout — VoiceLiveOptionsValidator",
        ["src/Tracon.Core/Voice/VoiceConversationDriver.cs"] = "VoiceConversationOptions.IdleTimeout / MaxConnectionDuration — VoiceConversationOptionsValidator",
        ["src/Tracon.Core/Webhooks/WebhookDeliveryJobHandler.cs"] = "TraconWebhookOptions.Timeout — TraconWebhookOptionsValidator",
        ["src/Tracon.Sqlite/Internal/SqliteDialect.cs"] = "constant 100 ms poll interval between two tries of the migration lock; the wait itself (the command timeout) is compared with the elapsed monotonic time, not armed as a timer",
        ["src/Tracon.Mcp/Internal/McpConnection.cs"] = "TraconMcpOptions.ConnectionTimeout — TraconMcpOptionsValidator",
        ["src/Tracon.Mcp/Internal/McpDiscoveryService.cs"] = "TraconMcpOptions.RefreshInterval — TraconMcpOptionsValidator (TimerPeriod.IsValid); the value is read again after every pass and checked with TimerPeriod.IsValid before it is armed (McpDiscoveryLoopTests)",
        ["src/Tracon.Mcp/Internal/McpOAuthPoll.cs"] = "constant intervals and limits (McpOAuthDistributedTiming) that no operator sets",
        ["src/Tracon.Mcp/Internal/McpShortLivedConnection.cs"] = "TraconMcpOptions.ConnectionTimeout — TraconMcpOptionsValidator",
        ["src/Tracon.Mcp/McpOAuthAuthorizationCoordinator.cs"] = "TraconMcpOptions.ConnectionTimeout — TraconMcpOptionsValidator",
        ["src/Tracon.OpenAI/Live/OpenAILiveSideband.cs"] = "constant 10 s",
        ["src/Tracon.OpenAI.Shared/OpenAIBoundedRetryPolicy.cs"] = "the delay between two tries of the client library, already decided by GetNextDelay (a Retry-After above ProviderRetryAfter.MaximumHonoredDelay is replaced by the library backoff, OpenAIBoundedRetryPolicyTests); this site only waits it out on the host clock",
        ["src/Tracon.Providers.Shared/ProviderHealthCheckCore.cs"] = "provider Timeout — the four provider validators (HTTP range, ProviderTimeoutValidationTests)",
        ["src/Tracon.Workflows/Internal/WorkflowRunner.cs"] = "TraconWorkflowOptions.RunTimeout / WorkflowDefinition.RunTimeout — TraconWorkflowOptionsValidator, WorkflowDefinitionValidator (save and compile), ceiling cut",
        ["src/Tracon.Workflows/Internal/WorkflowRunTypes.cs"] = "WorkflowDefinition.RunTimeout / WorkflowRegistrationOptions.RunTimeout — the run deadline re-arms the source the runner armed with the validated TraconWorkflowOptions.RunTimeout, cut to the ceiling",
        ["src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs"] = "TraconEndpointOptions.RunEventPollInterval — the property setter (TimerPeriod.IsValid); the follow stream waits it between two looks for the run's next continuation, on the clock the host registered",
        ["src/Tracon.AspNetCore/Internal/RunEventTail.cs"] = "TraconEndpointOptions.RunEventPollInterval — the property setter (TimerPeriod.IsValid); the wait between two reads of a run in progress runs on the clock the host registered",
        ["src/Tracon.AspNetCore/Security/ExternalSurfaceGuard.cs"] = "constant 200 ms doubling up to the constant 5 s ceiling (MaxCatalogListDelay) that no operator sets, waited on the clock the host registered",
        ["src/Tracon.Core/Retention/RetentionExecutor.cs"] = "TraconRetentionOptions.BatchDelay — TraconRetentionOptionsValidator (zero up to TimerPeriod.MaxMilliseconds); the pause between two batches runs on the executor's clock",
        ["src/Tracon.AspNetCore/Streaming/SseWriter.cs"] = "TraconEndpointOptions.StreamKeepAliveInterval — the property setter (TimerPeriod.IsValid); the delay is the part of it still to wait",
        ["src/Tracon.Core/Recording/RunEventWriter.cs"] = "TraconRunRecordingOptions.SinkTimeout — TraconOptionsValidator (zero and Timeout.InfiniteTimeSpan wait for ever, anything else TimerPeriod.IsValid); the closing write's pause and budget are the constants 250 ms CompletionRetryPause and 5 s CompletionBudget",
        ["src/Tracon.Core/Scheduling/JobOutcomeWriteRetry.cs"] = "constant 5 s ceiling (MaxDelay), cut to the lease window left",
        ["src/Tracon.Core/Scheduling/QueuedTurn.cs"] = "constant 5 s ceiling (RetryMaxDelay), cut to the lease window left",
        ["src/Tracon.Core/Tools/TimeoutAIFunction.cs"] = "per-tool timeout — constructor guard (TimerPeriod.IsValid)",
        ["src/Tracon.Core/Voice/LiveVoiceSessionRegistry.cs"] = "constant 5 s sweep",
        ["src/Tracon.Workflows/Internal/StalledStreamGuard.cs"] = "constant 2 s grace (StalledStreamGuard.Grace) that nothing configures — StalledStreamGuardTests",
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
    // matched.
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
    [InlineData("await " + DelayCall + "options.PollInterval, cancellationToken);")]
    public void The_pattern_leaves_a_delay_with_no_time_source_to_the_other_gates(string source)
        => TimeoutCall().IsMatch(source).ShouldBeFalse();

    /// <summary>
    /// The contract package is test support that ships as a package; the test-time ratchet governs its waits, not this gate.
    /// </summary>
    private const string ContractsPackage = "Tracon.Testing.Contracts.Xunit";

    // A delay that names no time source waits on the system clock, whatever the host registered. These are the ones that do
    // so on purpose. Every other delay in product code takes its clock from the host, so that a test advances it instead of
    // waiting (TimeoutSiteTests above lists the ones that arm a timeout on it).
    private static readonly Dictionary<string, (int Count, string Reason)> KnownRealClockDelays = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Sql.Shared/Internal/SqlStoreContext.cs"] = (1, "SQL conflict jitter: the random 10-40 ms pause before a deadlock victim retries, which spreads the victims out in real time against a database whose locks clear in real time"),
        ["src/Tracon.Sql.Shared/Migrations/MigrationRunner.cs"] = (1, "SQL conflict jitter: the random pause before a migration statement is retried after a transient conflict; it runs before the host's clock exists"),
        ["src/Tracon.Sql.Shared/Stores/SqlAuditLog.cs"] = (1, "SQL conflict jitter: the random pause before an audit write that lost the hash chain to a concurrent writer is retried, against a database whose locks clear in real time"),
        ["src/Tracon.Sql.Shared/Stores/SqlEvalStore.cs"] = (1, "SQL conflict jitter: the random pause before a promotion that read the same sequence number as a concurrent one is retried, against a database whose locks clear in real time"),
    };

    [Fact]
    public void Every_product_delay_that_names_no_time_source_is_a_known_real_clock_wait()
    {
        var root = FindRepositoryRoot();
        var actual = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains(ContractsPackage, StringComparison.Ordinal))
            {
                continue;
            }

            var count = CountRealClockDelays(File.ReadAllText(path));

            if (count > 0)
            {
                actual[relative] = count;
            }
        }

        var unknown = actual.Where(site => !KnownRealClockDelays.ContainsKey(site.Key) || KnownRealClockDelays[site.Key].Count != site.Value);
        var gone = KnownRealClockDelays.Where(site => actual.GetValueOrDefault(site.Key) != site.Value.Count);

        unknown.Select(static site => $"{site.Key}: {site.Value}").Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A delay in product code names no time source, so it waits on the system clock whatever the host registered and a " +
            "test cannot advance it. Take a TimeProvider and pass it to Task.Delay. Only a wait that must run in real time " +
            "(a database's lock contention clears in real time) is added here, with its count and the reason.");
        gone.Select(static site => site.Key).Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A known real-clock delay is gone or changed its count; fix the list.");
    }

    [Theory]
    [InlineData("await " + DelayCall + "delay, _clock, cancellationToken);", 0)]
    [InlineData("await " + DelayCall + "TimeSpan.FromMilliseconds(Random.Shared.Next(10, 40)), cancellationToken);", 1)]
    [InlineData("await " + DelayCall + "Timeout.InfiniteTimeSpan, token);", 0)]
    [InlineData("return " + DelayCall + "\n    TimeSpan.FromMilliseconds(5),\n    CancellationToken.None);", 1)]
    [InlineData("// await " + DelayCall + "5, token);", 0)]
    [InlineData("/// <see cref=\"" + DelayCall + "TimeSpan)\"/>", 0)]
    public void The_scan_counts_the_delays_that_name_no_time_source(string source, int expected)
        => CountRealClockDelays(source).ShouldBe(expected);

    private static int CountRealClockDelays(string source)
    {
        var code = string.Join('\n', source.Split('\n').Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        return DelayStatement()
            .Matches(code)
            .Count(static match => !TimeSource().IsMatch(match.Value) && !NeverWakes().IsMatch(match.Groups["arguments"].Value));
    }

    // The arguments of a delay run to the end of its statement; a call may be written over several lines.
    [GeneratedRegex(@"Task\.Delay\((?<arguments>[^;]*);", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex DelayStatement();

    [GeneratedRegex(@"(?:[Tt]imeProvider|[Cc]lock)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex TimeSource();

    // A delay of no end never wakes by time, so no clock decides when it ends.
    [GeneratedRegex(@"^\s*(?:Timeout\.Infinite(?:TimeSpan)?\b|-1\b)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex NeverWakes();

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
