using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// A run that is bound to values when its agent is resolved says where the values came from.
/// </summary>
/// <remarks>
/// <para>
/// A tool reads the values of its run with a source (<see cref="ParameterValueSource"/>), and decides by the source whether the
/// values may carry authority. An agent the catalog compiled for values carries them in a marker, and a run with a marker and
/// nothing else to say reads them as the embedding code's own (<see cref="ParameterValueSource.Caller"/>), because that is what
/// <c>AgentResolveOptions.Parameters</c> is for. A surface that resolves an agent with values it took from a request would
/// hand those values to its tools as the host's.
/// </para>
/// <para>
/// So the one place in <c>src/</c> that binds request values at resolve time, the turn preparation of a request, says the source
/// explicitly (<c>TraconRunOptions.ParameterSource</c>), and no other place does that. Every other surface that repeats values
/// (a queued run, an approval resumed from the inbox, a replay) resolves its agent without them and passes them as run
/// options, which read as <see cref="ParameterValueSource.Client"/>. A new site that resolves with <c>Parameters</c> fails
/// here until it is added to the list WITH the source it says.
/// </para>
/// </remarks>
public sealed class ParameterSourceSiteTests
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    // `new AgentResolveOptions { ... Parameters = ... }`: the initializer may span lines and holds no nested braces.
    private static readonly Regex ResolveWithValues = new(
        @"new\s+AgentResolveOptions\s*\{[^{}]*\bParameters\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    // The sites that bind values at resolve time, and the text each must carry to have said where the values came from.
    private static readonly (string Path, string MustSay)[] Declared =
    [
        ("src/Tracon.AspNetCore/Internal/AgentTurnService.cs", "ParameterSource = parameterSource"),
        ("src/Tracon.Core/Voice/VoiceConversationDriver.cs", "ParameterSource = _hostDecided"),
    ];

    [Fact]
    public void Only_the_declared_sites_resolve_an_agent_with_values_and_each_says_where_they_came_from()
    {
        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            if (ResolveWithValues.IsMatch(File.ReadAllText(file)))
            {
                found.Add(relative);
            }
        }

        found.Order(StringComparer.Ordinal).ShouldBe(
            Declared.Select(static site => site.Path).Order(StringComparer.Ordinal),
            "A site that resolves an agent with AgentResolveOptions.Parameters hands the values to its tools as the embedding " +
            "code's own unless it says otherwise. Say the source in the run options (TraconRunOptions.ParameterSource) and add " +
            "the site to Declared with the text that says it.");

        foreach (var (path, mustSay) in Declared)
        {
            File.ReadAllText(Path.Combine(RepoRoot.Path, path)).ShouldContain(
                mustSay,
                Case.Sensitive,
                $"{path} binds values at resolve time and must say where they came from.");
        }
    }
}
