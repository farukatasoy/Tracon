using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces that every HTTP surface that starts a run, or reaches a run's
/// resources, still calls <c>RunAuthorizationGate</c> (phases 139 and 147,
/// F-185 and F-195).
/// </summary>
/// <remarks>
/// <para>
/// These endpoints do not share one endpoint filter — each calls the gate
/// explicitly, in its own body, the same reason <c>QuotaGate</c> is not a
/// filter either (an OpenAI-compatible request carries its agent name in the
/// body's <c>model</c> field, not the route). Functional tests that exercise a
/// DENYING handler against each surface (<c>RunAuthorizationEndpointTests</c>,
/// <c>RunResourceAuthorizationTests</c>, <c>VoiceAuthorizationTests</c>)
/// already prove today's behavior; this is the second, cheaper layer both
/// phases' risk tables asked for — a plain source scan that fails loudly if a
/// refactor ever deletes the call from one of these files, without needing to
/// boot a host or resolve a handler.
/// </para>
/// <para>
/// 🚨 The scan cannot discover a brand-new run-starting or resource endpoint
/// added in a later phase — that judgment call ("is this a run-starting
/// surface") is not mechanical, and phase 139 got it wrong: it declared FOUR
/// run-starting files while <c>POST /api/runs/{id}/replay</c> and
/// <c>/v1/chat/completions</c> were already starting real runs without the
/// gate. Phase 147 found them and the list below is now SIX. What this test
/// catches is a REGRESSION: a known call site silently losing its call during
/// a later edit. When a new surface is added, this list must be added to BY
/// HAND, and the handover note of the phase that adds it must say so.
/// </para>
/// </remarks>
public sealed class RunAuthorizationCoverageTests
{
    // 🚨 The real call is formatted across two lines (`RunAuthorizationGate`
    // then `.CheckRunAsync(` on the next, indented line) at every call site -
    // a plain substring search would never match, the exact K-642 "split
    // phrase" trap SeamContractDocumentationTests warns about. The regex
    // tolerates any whitespace (including a newline) between the two halves.
    private static readonly Regex StartMarker = new(
        @"RunAuthorizationGate\s*\.\s*CheckRunAsync\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ResourceMarker = new(
        @"RunAuthorizationGate\s*\.\s*(CheckRunResourceAsync|CheckSessionAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// 🚨 A run started FROM another run — a replay, a workflow resume, a
    /// workflow respond — asks the gate through <c>CheckRunResourceAsync</c>
    /// with <c>RunAccess.Start</c>, not <c>CheckRunAsync</c>: the request has
    /// to carry the SOURCE run's id, which the run-start overload cannot
    /// express. Without it a handler cannot tell "start a run of this agent"
    /// apart from "continue somebody else's".
    /// </summary>
    /// <remarks>
    /// <c>AuthorizeWorkflowRunAsync</c> is named explicitly rather than matched
    /// by a loose pattern: it is <c>WorkflowEndpoints</c>' own two-line wrapper
    /// around the gate (it settles the run's tenant first, so another tenant's
    /// identity never reaches a consumer's handler), and a pattern loose enough
    /// to catch "some method with Run in its name" could be satisfied by a call
    /// that is not the gate at all.
    /// </remarks>
    private static readonly Regex ResourceStartMarker = new(
        @"(?:CheckRunResourceAsync|AuthorizeWorkflowRunAsync)\s*\([^;]*?RunAccess\.Start",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The files that START a run. Four came from phase 139; replay, Chat
    /// Completions, and the two workflow continuation endpoints from 147.
    /// </summary>
    /// <remarks>
    /// 🚨 <c>WorkflowEndpoints.cs</c> appears TWICE, under two different
    /// markers. It holds both <c>POST /api/workflows/{name}/run</c> (which
    /// calls <c>CheckRunAsync</c>) and <c>resume</c>/<c>respond</c> (which open
    /// a NEW run row from an existing one and therefore call
    /// <c>CheckRunResourceAsync</c> with <c>RunAccess.Start</c>). A single
    /// file-level entry would have gone green on the first call alone and
    /// hidden the other two — which is exactly how phase 147's audit found them
    /// still ungated.
    /// </remarks>
    private static readonly (string Path, Regex Marker)[] ExpectedRunStartingFiles =
    [
        ("src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs", StartMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", StartMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", ResourceStartMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/TriggerEndpoints.cs", StartMarker),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs", StartMarker),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs", StartMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs", ResourceStartMarker),
    ];

    /// <summary>The files that reach a run's or a session's RESOURCES behind the same handler (phase 147).</summary>
    private static readonly (string Path, Regex Marker)[] ExpectedResourceFiles =
    [
        ("src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/ObservabilityEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/AttachmentEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs", ResourceMarker),
        ("src/AgentPrism.AspNetCore/Voice/VoiceConversationEndpoint.cs", ResourceMarker),
    ];

    [Fact]
    public void Every_known_run_starting_surface_still_calls_the_gate()
        => AssertEveryFileMatches(
            ExpectedRunStartingFiles,
            "the run-start gate — a run can start there WITHOUT going through the " +
            "installation's own IRunAuthorizationHandler (phases 139 and 147)");

    [Fact]
    public void Every_known_run_resource_surface_still_calls_the_gate()
        => AssertEveryFileMatches(
            ExpectedResourceFiles,
            "'RunAuthorizationGate.CheckRunResourceAsync(' or '.CheckSessionAsync(' — another user's run, " +
            "attachments, approvals, or session can be reached there WITHOUT going through the " +
            "installation's own IRunAuthorizationHandler (phase 147, F-195)");

    /// <summary>Regression coverage for the scan itself, isolated from the real repository tree.</summary>
    /// <remarks>
    /// 🚨 The point is K-642: a plain <c>Contains</c> would find NEITHER real
    /// call, because every call site wraps the line after the class name. A
    /// scan that silently matches nothing is a test that reports green while
    /// proving nothing, so both directions are asserted here.
    /// </remarks>
    [Fact]
    public void A_file_missing_the_call_is_reported_by_name()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-run-auth-coverage-test");

        try
        {
            var withCall = Path.Combine(directory.FullName, "WithCall.cs");
            var withResourceCall = Path.Combine(directory.FullName, "WithResourceCall.cs");
            var withoutCall = Path.Combine(directory.FullName, "WithoutCall.cs");

            // Matches the real, two-line-wrapped call shape at every actual call site.
            File.WriteAllText(withCall, "if (await RunAuthorizationGate\n        .CheckRunAsync(handler, tenants)");
            File.WriteAllText(withResourceCall, "if (await RunAuthorizationGate\n        .CheckRunResourceAsync(handler, tenants)");
            File.WriteAllText(withoutCall, "// no call here");

            StartMarker.IsMatch(File.ReadAllText(withCall)).ShouldBeTrue();
            StartMarker.IsMatch(File.ReadAllText(withResourceCall)).ShouldBeFalse();
            StartMarker.IsMatch(File.ReadAllText(withoutCall)).ShouldBeFalse();

            ResourceMarker.IsMatch(File.ReadAllText(withResourceCall)).ShouldBeTrue();
            ResourceMarker.IsMatch(File.ReadAllText(withCall)).ShouldBeFalse();
            ResourceMarker.IsMatch(File.ReadAllText(withoutCall)).ShouldBeFalse();

            // The run-from-run marker must see the ACCESS argument, not just the
            // call: a resource call for any other access does not start a run.
            var replayCall = Path.Combine(directory.FullName, "ReplayCall.cs");
            File.WriteAllText(replayCall, "await RunAuthorizationGate\n    .CheckRunResourceAsync(\n        h,\n        t,\n        RunAccess.Start,");

            ResourceStartMarker.IsMatch(File.ReadAllText(replayCall)).ShouldBeTrue();
            ResourceStartMarker.IsMatch(File.ReadAllText(withResourceCall)).ShouldBeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void AssertEveryFileMatches((string Path, Regex Marker)[] expected, string message)
    {
        var root = FindRepositoryRoot();
        var missing = new List<string>();

        foreach (var (relative, marker) in expected)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(path).ShouldBeTrue($"'{relative}' no longer exists; this gate has gone stale and must be updated.");

            if (!marker.IsMatch(File.ReadAllText(path)))
            {
                missing.Add(relative);
            }
        }

        missing.ShouldBeEmpty(
            customMessage: $"The following endpoint file(s) no longer call {message}: {string.Join(", ", missing)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
