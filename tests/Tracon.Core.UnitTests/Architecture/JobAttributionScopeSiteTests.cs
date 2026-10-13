using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every built-in <see cref="IJobHandler"/> states whether the runs it starts
/// carry an attribution, and a handler that says it does opens
/// <see cref="AmbientRunAttributionScope"/> in its own source.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards repeated four times: a background path started a
/// root run whose row looked right while its sub-agents, tools and quota record
/// saw no user and no labels (K-692, F-337, F-364, F-369). A worker has no
/// request to read, so the handler must replay the attribution the work was
/// queued with, and it must open the scope in its own method body (an ambient
/// write made in an async helper does not flow back). Each case was found by
/// reading code. The behaviour is proven by each path's own tests
/// (<c>QueuedRunAttributionTests</c>, <c>QueuedWorkflowAttributionTests</c>,
/// <c>RunContinuationTests</c>).
/// </para>
/// <para>
/// The gate does not prove the scope is right; it makes a NEW handler fail until
/// someone states its attribution, and it makes a handler that drops its scope
/// fail. Where the scope belongs in the method is checked by
/// <c>AmbientWriteSiteTests</c>.
/// </para>
/// </remarks>
public sealed partial class JobAttributionScopeSiteTests
{
    private const string ScopeCall = "AmbientRunAttributionScope.Begin(";

    // true: the handler starts root runs and must open the scope.
    // false: the reason it starts no root run that a user owns.
    private static readonly Dictionary<string, (bool OpensScope, string Reason)> KnownHandlers = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Core/Approvals/ApprovalResumeJobHandler.cs"] =
            (true, "replays the user and labels of the run that asked for approval"),
        ["src/Tracon.Core/Evaluation/EvalJobHandler.cs"] =
            (false, "starts eval case runs with no user and no labels; whether an eval carries the attribution of whoever queued it is an open decision"),
        ["src/Tracon.Core/Evaluation/OnlineEvalJobHandler.cs"] =
            (false, "runs a judge over a finished run; the judge rows belong to the platform, not to a user"),
        ["src/Tracon.Core/Retention/RetentionJobHandler.cs"] =
            (false, "starts no run"),
        ["src/Tracon.Core/Scheduling/AgentBatchJobHandler.cs"] =
            (true, "replays the attribution envelope of the job payload"),
        ["src/Tracon.Core/Scheduling/AgentRunJobHandler.cs"] =
            (true, "replays the userId and labels of the queued run's payload"),
        ["src/Tracon.Core/Scheduling/RunContinuationJobHandler.cs"] =
            (true, "replays the user and labels of the interrupted run"),
        ["src/Tracon.Core/Scheduling/WorkflowJobHandler.cs"] =
            (true, "replays the attribution envelope of the job payload; a continuation's row is opened with it too"),
        ["src/Tracon.Core/Scheduling/WorkflowRunJobHandler.cs"] =
            (true, "replays the userId and labels of the queued operation's payload; proven on a function node by WorkflowAsyncAttributionTests"),
        ["src/Tracon.Core/Webhooks/WebhookDeliveryJobHandler.cs"] =
            (false, "starts no run"),
    };

    [Fact]
    public void Every_job_handler_states_the_attribution_of_the_runs_it_starts()
    {
        var root = FindRepositoryRoot();

        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => HandlerDeclaration().IsMatch(File.ReadAllText(path)))
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);

        var unknown = actual.Keys.Where(path => !KnownHandlers.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var gone = KnownHandlers.Keys.Where(path => !actual.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A new job handler was added. If it starts a root run, replay the attribution the work was queued " +
            "with: carry the user and labels in the job, and open AmbientRunAttributionScope in the handler's own " +
            "ExecuteAsync body. Prove it on a sub-run or a tool, not on the run row, then add the file here.");
        gone.ShouldBeEmpty("A known job handler no longer exists; remove it from the list.");

        var lostScope = KnownHandlers
            .Where(entry => entry.Value.OpensScope && actual.TryGetValue(entry.Key, out var text) && !text.Contains(ScopeCall, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        lostScope.ShouldBeEmpty(
            "These handlers start root runs that carry an attribution, but their source no longer opens " +
            "AmbientRunAttributionScope. Their runs' sub-agents and tools would see no user and no labels.");

        var undeclaredScope = KnownHandlers
            .Where(entry => !entry.Value.OpensScope && actual.TryGetValue(entry.Key, out var text) && text.Contains(ScopeCall, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        undeclaredScope.ShouldBeEmpty(
            "These handlers now open AmbientRunAttributionScope; mark them as opening it and state what they replay.");
    }

    [Fact]
    public void The_scan_sees_a_primary_constructor_declaration_and_skips_a_comment()
    {
        HandlerDeclaration().IsMatch("internal sealed class Widget(ILogger? logger = null) : IJobHandler").ShouldBeTrue();
        HandlerDeclaration().IsMatch("    ILogger<Widget>? logger = null) : IJobHandler").ShouldBeTrue();
        HandlerDeclaration().IsMatch("        // K-641: IJobHandler is at-least-once").ShouldBeFalse();
    }

    // The same declaration scan JobHandlerOutcomeWriteSiteTests uses: a class
    // declaration, or the closing parenthesis of a primary constructor, followed
    // by a base list that names IJobHandler. A comment line is skipped.
    [GeneratedRegex(
        @"^(?!\s*//)[^\r\n]*(?:\)|\bclass\s+\w+(?:<[^>\r\n]*>)?)\s*:\s*(?:[\w.]+(?:<[^>\r\n]*>)?\s*,\s*)*IJobHandler\b",
        RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex HandlerDeclaration();

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
