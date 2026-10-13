using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every file in <c>src/</c> that arms a timeout with <c>CancelAfter(</c> or
/// <c>new CancellationTokenSource(&lt;value&gt;</c> must be a known site whose value
/// is either a constant or checked against the timer range before it is armed
/// (F-280).
/// </summary>
/// <remarks>
/// <para>
/// Both calls refuse a negative value or more than about 49.7 days, and they run
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
        ["src/Tracon.Core/Graph/ChildAgentInvoker.cs"] = "SubAgentSettings / TraconAgentGraphOptions ChildDeadline — compiler, TraconOptionsValidator, constructor guard",
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
        ["src/Tracon.Workflows/Internal/WorkflowRunner.cs"] = "TraconWorkflowOptions.RunTimeout — TraconWorkflowOptionsValidator",
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

    // `CancelAfter(` anywhere, or a CancellationTokenSource constructed WITH an argument
    // (the parameterless one arms nothing).
    [GeneratedRegex(
        @"\.CancelAfter\(|new\s+CancellationTokenSource\(\s*[^)\s]",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex TimeoutCall();

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
