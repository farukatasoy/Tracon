using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Keeps the capability map in step with the code: every public registration
/// entry point must be named on the capability map page.
/// </summary>
/// <remarks>
/// <para>
/// The map is what a coding agent reads — <c>AGENTS.md</c>, <c>llms.txt</c>, and
/// the published page are all generated from
/// <c>docs-site/src/content/docs/capabilities.md</c>. Nothing else notices when
/// the code grows a new capability and the map does not follow, and a map that
/// is missing an entry point is worse than no map: the agent concludes the
/// capability does not exist and writes it by hand.
/// </para>
/// <para>
/// A ratchet, not a snapshot, in the shape of <see cref="SourceLanguageTests"/>.
/// <c>capability-coverage-baseline.txt</c> lists the entry points that are
/// allowed to be absent from the map. The list may only shrink: a new absence
/// fails, and so does a stale entry that is now covered.
/// </para>
/// <para>
/// Refresh after deliberately changing what is covered:
/// <c>AGENTPRISM_CAPABILITY_COVERAGE_REFRESH=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class CapabilityCoverageTests
{
    private const string RefreshEnvVar = "AGENTPRISM_CAPABILITY_COVERAGE_REFRESH";

    /// <summary>
    /// The receivers that make an extension method a capability entry point. A
    /// consumer turns a capability on through one of these; an extension on any
    /// other type is a helper, not a registration.
    /// </summary>
    private static readonly HashSet<string> RegistrationReceivers = new(StringComparer.Ordinal)
    {
        "AgentPrism.IAgentPrismBuilder",
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder",
        "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
        "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
    };

    /// <summary>A member declared on the builder interface itself.</summary>
    private static readonly Regex BuilderMemberPattern = new(
        @"^AgentPrism\.IAgentPrismBuilder\.(?<name>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>An <c>Add</c>, <c>Use</c>, or <c>Map</c> extension method, with its receiver.</summary>
    private static readonly Regex ExtensionMethodPattern = new(
        @"^static [A-Za-z0-9_.]+\.(?<name>(?:Add|Use|Map)[A-Za-z0-9_]*)(?:<[^(]*>)?\(this (?<receiver>[A-Za-z0-9_.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_registration_entry_point_is_named_on_the_capability_map()
    {
        var entryPoints = CollectEntryPoints();

        entryPoints.ShouldNotBeEmpty("No entry point was read; the PublicAPI files or the parser changed shape.");

        var map = File.ReadAllText(CapabilityMapPath);
        var uncovered = entryPoints.Where(name => !IsNamed(map, name)).ToList();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(uncovered);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var name in uncovered.Where(name => !baseline.Contains(name)))
        {
            failures.Add($"+ {name}: a registration entry point that the capability map never names");
        }

        foreach (var name in baseline.Where(name => !uncovered.Contains(name)))
        {
            failures.Add($"- {name}: covered by the capability map now, but still listed in the baseline");
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The capability map and the public API disagree.\n" +
                           $"{string.Join('\n', failures)}\n" +
                           $"Add the entry point to {RelativeCapabilityMapPath}, or refresh the baseline: " +
                           $"{RefreshEnvVar}=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release");
    }

    /// <summary>
    /// Reads the tracked public API of every package. Both files are read:
    /// members move from <c>Unshipped</c> to <c>Shipped</c> at release, and the
    /// gate must not go blind on that day.
    /// </summary>
    private static SortedSet<string> CollectEntryPoints()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "src"),
                     "PublicAPI.*.txt",
                     SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                var member = BuilderMemberPattern.Match(line);

                if (member.Success)
                {
                    names.Add(member.Groups["name"].Value);
                    continue;
                }

                var extension = ExtensionMethodPattern.Match(line);

                if (extension.Success && RegistrationReceivers.Contains(extension.Groups["receiver"].Value))
                {
                    names.Add(extension.Groups["name"].Value);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Whether the map names the member. The trailing boundary matters:
    /// without it, <c>AddAgentPrism()</c> would silently vouch for a member
    /// called <c>AddAgent</c>.
    /// </summary>
    private static bool IsNamed(string map, string name)
        => Regex.IsMatch(
            map,
            $@"{Regex.Escape(name)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

    private static SortedSet<string> ReadBaseline()
    {
        var baseline = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                baseline.Add(line.Trim());
            }
        }

        return baseline;
    }

    private static void WriteBaseline(IEnumerable<string> uncovered)
    {
        var builder = new StringBuilder();

        builder.Append("# Generated by CapabilityCoverageTests. Refresh:\n");
        builder.Append($"#   {RefreshEnvVar}=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release\n");
        builder.Append("# One registration entry point per line: allowed to be absent from the capability map.\n");
        builder.Append("# The list may only shrink. An empty list is the intended state.\n");

        foreach (var name in uncovered.OrderBy(name => name, StringComparer.Ordinal))
        {
            builder.Append(name).Append('\n');
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private const string RelativeCapabilityMapPath = "docs-site/src/content/docs/capabilities.md";

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string CapabilityMapPath { get; } =
        Path.Combine(RepositoryRoot, "docs-site", "src", "content", "docs", "capabilities.md");

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "AgentPrism.Core.UnitTests",
        "Architecture",
        "capability-coverage-baseline.txt");

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
