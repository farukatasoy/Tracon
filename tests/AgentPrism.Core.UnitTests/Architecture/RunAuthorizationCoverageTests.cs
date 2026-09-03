using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces that every run-starting HTTP surface still calls
/// <c>RunAuthorizationGate.CheckRunAsync</c> (Phase 139, F-185).
/// </summary>
/// <remarks>
/// <para>
/// The four run-starting endpoints do not share one endpoint filter — each
/// calls the gate explicitly, in its own body, the same reason
/// <c>QuotaGate</c> is not a filter either (an OpenAI-compatible request
/// carries its agent name in the body's <c>model</c> field, not the route).
/// A functional test that exercises a DENYING handler against all four
/// surfaces (<c>RunAuthorizationEndpointTests</c>) already proves today's
/// behavior; this is the second, cheaper layer the phase's own risk table
/// asked for — a plain source scan that fails loudly if a refactor ever
/// deletes the call from one of the four files, without needing to boot a
/// host or resolve a handler.
/// </para>
/// <para>
/// This test cannot discover a brand-new FIFTH run-starting endpoint added
/// in a later phase — that judgment call ("is this a run-starting surface")
/// is not mechanical. What it catches is a REGRESSION: one of the four KNOWN
/// call sites silently losing its call during a later edit.
/// </para>
/// </remarks>
public sealed class RunAuthorizationCoverageTests
{
    // 🚨 The real call is formatted across two lines (`RunAuthorizationGate`
    // then `.CheckRunAsync(` on the next, indented line) at every call site -
    // a plain substring search would never match, the exact K-642 "split
    // phrase" trap SeamContractDocumentationTests warns about. The regex
    // tolerates any whitespace (including a newline) between the two halves.
    private static readonly Regex CallMarker = new(
        @"RunAuthorizationGate\s*\.\s*CheckRunAsync\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly string[] ExpectedRunStartingFiles =
    [
        "src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/TriggerEndpoints.cs",
        "src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs",
    ];

    [Fact]
    public void Every_known_run_starting_surface_still_calls_the_gate()
    {
        var root = FindRepositoryRoot();
        var missing = new List<string>();

        foreach (var relative in ExpectedRunStartingFiles)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(path).ShouldBeTrue($"'{relative}' no longer exists; this gate has gone stale and must be updated.");

            if (!CallMarker.IsMatch(File.ReadAllText(path)))
            {
                missing.Add(relative);
            }
        }

        missing.ShouldBeEmpty(
            customMessage: "The following run-starting endpoint(s) no longer call " +
                            "'RunAuthorizationGate.CheckRunAsync(' — a run can start there WITHOUT going through the " +
                            $"installation's own IRunAuthorizationHandler (phase 139, F-185): " +
                            $"{string.Join(", ", missing)}");
    }

    /// <summary>Regression coverage for the scan itself, isolated from the real repository tree.</summary>
    [Fact]
    public void A_file_missing_the_call_is_reported_by_name()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-run-auth-coverage-test");

        try
        {
            var withCall = Path.Combine(directory.FullName, "WithCall.cs");
            var withoutCall = Path.Combine(directory.FullName, "WithoutCall.cs");

            // Matches the real, two-line-wrapped call shape at every actual call site.
            File.WriteAllText(withCall, "if (await RunAuthorizationGate\n        .CheckRunAsync(handler, tenants)");
            File.WriteAllText(withoutCall, "// no call here");

            CallMarker.IsMatch(File.ReadAllText(withCall)).ShouldBeTrue();
            CallMarker.IsMatch(File.ReadAllText(withoutCall)).ShouldBeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
