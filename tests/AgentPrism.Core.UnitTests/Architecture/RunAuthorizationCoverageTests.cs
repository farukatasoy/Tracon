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
    /// Phase 148's own ratchet, kept in this class rather than a new one
    /// because it guards the SAME failure in the same way: an HTTP surface
    /// that reaches a session without asking whose it is. Session ownership is
    /// a second boundary drawn under the tenant, and it is enforced by explicit
    /// calls for the same reason the authorization gate is — these endpoints
    /// share no route shape.
    /// </summary>
    private static readonly Regex OwnershipRunMarker = new(
        @"SessionOwnershipGate\s*\.\s*CheckRunSessionAsync\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Matches either single-session ownership check on a session endpoint.</summary>
    private static readonly Regex OwnershipResourceMarker = new(
        @"SessionOwnershipGate\s*\.\s*(DeniesAsync|ResolveListOwnerFilterAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
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
    private static readonly (string Path, Regex Marker, int Calls)[] ExpectedRunStartingFiles =
    [
        ("src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs", StartMarker, 1),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", StartMarker, 1),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", ResourceStartMarker, 2),
        ("src/AgentPrism.AspNetCore/Endpoints/TriggerEndpoints.cs", StartMarker, 1),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs", StartMarker, 1),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs", StartMarker, 1),
        ("src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs", ResourceStartMarker, 1),
    ];

    /// <summary>The files that reach a run's or a session's RESOURCES behind the same handler (phase 147).</summary>
    /// <remarks>
    /// 🚨 <c>OpenAIConversationsEndpoints</c> joined in phase 149, one phase
    /// after it joined the ownership list below — the same file, the same
    /// miss, found twice. Phase 148 gave it AgentPrism's own ownership
    /// boundary and stopped there; the consumer's own
    /// <c>IRunAuthorizationHandler</c> was still never asked, so an
    /// installation whose handler refused <c>GET /api/sessions/{id}</c> had
    /// <c>GET /v1/conversations/{id}/items</c> hand over the same history.
    /// A surface can be half-gated, and finding one of its two gates is not
    /// evidence about the other.
    /// </remarks>
    private static readonly (string Path, Regex Marker, int Calls)[] ExpectedResourceFiles =
    [
        ("src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs", ResourceMarker, 12),
        ("src/AgentPrism.AspNetCore/Endpoints/ObservabilityEndpoints.cs", ResourceMarker, 2),
        ("src/AgentPrism.AspNetCore/Endpoints/AttachmentEndpoints.cs", ResourceMarker, 4),
        ("src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs", ResourceMarker, 3),
        ("src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs", ResourceMarker, 4),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", ResourceMarker, 2),
        ("src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs", ResourceMarker, 2),
        ("src/AgentPrism.AspNetCore/Voice/VoiceConversationEndpoint.cs", ResourceMarker, 1),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIConversationsEndpoints.cs", ResourceMarker, 3),
    ];

    /// <summary>
    /// The run-starting files that can name a SESSION, and therefore have to
    /// ask whose it is (phase 148).
    /// </summary>
    /// <remarks>
    /// 🚨 <c>OpenAIConversationsEndpoints</c> is on this list because the
    /// phase's own audit found it OUTSIDE it: <c>/v1/conversations/{id}</c>
    /// reaches the very same sessions under a different name, so while
    /// <c>GET /api/sessions/{id}</c> answered 404 for another user's session,
    /// <c>GET /v1/conversations/{id}/items</c> returned its whole history and
    /// <c>DELETE</c> removed it. The same class of miss phase 139 made and
    /// phase 147 had to come back for — a compatibility surface is a surface.
    ///
    /// 🚨 Three run-starting files, not six. <c>TriggerEndpoints</c> and
    /// <c>OpenAIChatCompletionsEndpoints</c> pass <c>sessionId: null</c> to the
    /// authorization gate — they start sessionless runs and have no session to
    /// own. Listing them here would demand a call that could only ever be a
    /// no-op, and a no-op call is worse than none: the next reader would take
    /// it as evidence that those surfaces carry sessions. If either ever gains
    /// a session parameter, it belongs in this list, and the phase that adds it
    /// must say so in its handover note.
    /// </remarks>
    private static readonly (string Path, Regex Marker, int Calls)[] ExpectedSessionOwnershipFiles =
    [
        ("src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs", OwnershipRunMarker, 1),
        ("src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs", OwnershipRunMarker, 1),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs", OwnershipRunMarker, 1),
        ("src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs", OwnershipResourceMarker, 4),
        ("src/AgentPrism.AspNetCore/Voice/VoiceConversationEndpoint.cs", OwnershipResourceMarker, 1),
        ("src/AgentPrism.AspNetCore/OpenAICompat/OpenAIConversationsEndpoints.cs", OwnershipResourceMarker, 3),
    ];

    [Fact]
    public void Every_known_session_reaching_surface_still_calls_the_ownership_gate()
        => AssertEveryFileMatches(
            ExpectedSessionOwnershipFiles,
            "'SessionOwnershipGate.CheckRunSessionAsync(' or '.DeniesAsync(' — another user's session " +
            "can be read, continued, or spoken into there WITHOUT the per-user ownership boundary " +
            "(phase 148, F-196)");

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

            // 🚨 The gate counts, it does not merely detect (F-204). A marker
            // that matched only the FIRST call site would make every expected
            // count 1 and re-open the exact gap this ratchet closes, so the
            // counting behavior is asserted here rather than inferred from the
            // real tree's numbers.
            var threeCalls = Path.Combine(directory.FullName, "ThreeCalls.cs");
            File.WriteAllText(
                threeCalls,
                "await RunAuthorizationGate\n    .CheckSessionAsync(a);\n" +
                "await RunAuthorizationGate\n    .CheckSessionAsync(b);\n" +
                "await RunAuthorizationGate\n    .CheckRunResourceAsync(c);\n");

            ResourceMarker.Count(File.ReadAllText(threeCalls)).ShouldBe(3);
            ResourceMarker.Count(File.ReadAllText(withoutCall)).ShouldBe(0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Asserts each file still calls its gate the EXACT expected number of
    /// times.
    /// </summary>
    /// <remarks>
    /// 🚨 This used to assert PRESENCE, and presence is not coverage: a file
    /// holds one entry per marker while carrying many call sites, so deleting
    /// all but one left the gate green. MEASURED (2026-09-06, F-204):
    /// <c>RunEndpoints.cs</c> carries TWELVE resource checks — eleven could
    /// have been removed silently, and <c>OpenAIConversationsEndpoints.cs</c>
    /// carries three, the file phases 147 and 149 each had to come back for.
    /// <para>
    /// The count is EXACT, not a floor. A floor would let an addition pay for
    /// a deletion and go green on a net zero, and the point of a ratchet on a
    /// security boundary is that changing it is deliberate: adding a gated
    /// endpoint here must be confirmed by a human updating the number, the
    /// same way <c>SourceLanguageTests</c>' baseline is. The failure message
    /// names the file and both numbers so the update takes one line.
    /// </para>
    /// </remarks>
    private static void AssertEveryFileMatches((string Path, Regex Marker, int Calls)[] expected, string message)
    {
        var root = FindRepositoryRoot();
        var wrong = new List<string>();

        foreach (var (relative, marker, calls) in expected)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(path).ShouldBeTrue($"'{relative}' no longer exists; this gate has gone stale and must be updated.");

            var actual = marker.Count(File.ReadAllText(path));

            if (actual != calls)
            {
                wrong.Add($"{relative} (expected {calls}, found {actual})");
            }
        }

        wrong.ShouldBeEmpty(
            customMessage: $"The following endpoint file(s) no longer call {message} the expected number of " +
                           "times. A LOWER count means a call site was deleted and that surface is now " +
                           "ungated — restore it. A HIGHER count means a gated surface was added: confirm it " +
                           $"is correctly gated, then update the number here. {string.Join(", ", wrong)}");
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
