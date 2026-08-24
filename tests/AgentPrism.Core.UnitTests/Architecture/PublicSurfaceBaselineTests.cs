using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Pins the public TYPE count of every publicly-tracked package to a
/// checked-in baseline, so the surface Phase 96 narrowed cannot regrow
/// silently.
/// </summary>
/// <remarks>
/// <para>
/// A ratchet, not a snapshot - the same pattern as
/// <see cref="SourceLanguageTests"/>: the number may only shrink.
/// <c>public-surface-baseline.txt</c> fails when a package grows past its
/// allowed count, when an untracked package appears, and when a package
/// shrinks without the baseline being refreshed.
/// </para>
/// <para>
/// Counts TYPES, not entries: <c>PublicAPI.Unshipped.txt</c> lists one line
/// per member too (a property getter, a constructor), and most of Phase 96's
/// 8,063 entries were <c>Options</c> accessors that a type-count gate must
/// not react to (96.5). A line is a type declaration when it names neither a
/// member signature (<c>(</c>) nor a member's type (<c> -&gt; </c>) - the
/// same filter as the phase's own measurement command.
/// </para>
/// <para>
/// Refresh after a deliberate, reviewed surface change:
/// <c>AGENTPRISM_PUBLIC_SURFACE_REFRESH=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class PublicSurfaceBaselineTests
{
    private const string RefreshEnvVar = "AGENTPRISM_PUBLIC_SURFACE_REFRESH";

    private static readonly Regex MemberSignaturePattern = new(
        @"\(| -> ",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Public_type_count_matches_the_checked_in_baseline_per_package()
    {
        var actual = MeasureAllPackages();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(actual);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var (package, count) in actual)
        {
            if (!baseline.TryGetValue(package, out var allowed))
            {
                failures.Add($"+ {package}: new publicly-tracked package with {count} types, no baseline entry - add one");
                continue;
            }

            if (count > allowed)
            {
                failures.Add($"+ {package}: {count} public types, baseline allows {allowed}");
            }
        }

        foreach (var (package, allowed) in baseline)
        {
            var count = actual.GetValueOrDefault(package, -1);

            if (count < 0)
            {
                failures.Add($"- {package}: baseline entry for a package that no longer tracks public API - remove it");
            }
            else if (count < allowed)
            {
                failures.Add($"- {package}: {count} public types, baseline still allows {allowed} - refresh it");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The public surface baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"A count that only shrank is fixed by refreshing: {RefreshEnvVar}=1 " +
                           "dotnet test tests/AgentPrism.Core.UnitTests -c Release");
    }

    private static SortedDictionary<string, int> MeasureAllPackages()
    {
        var results = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var unshippedFile in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"), "PublicAPI.Unshipped.txt", SearchOption.AllDirectories))
        {
            var package = Path.GetFileName(Path.GetDirectoryName(unshippedFile))!;
            var count = File.ReadLines(unshippedFile)
                .Count(line => line.Length > 0 && line[0] != '#' && !MemberSignaturePattern.IsMatch(line));

            results[package] = count;
        }

        return results;
    }

    private static Dictionary<string, int> ReadBaseline()
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');

            separator.ShouldBeGreaterThan(0, $"Malformed baseline line: '{line}'");

            baseline[line[..separator]] = int.Parse(
                line[(separator + 1)..],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return baseline;
    }

    private static void WriteBaseline(SortedDictionary<string, int> actual)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Generated by PublicSurfaceBaselineTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release");
        builder.AppendLine("# One line per public-API-tracked package: <package>=<public type count>.");
        builder.AppendLine("# Counts TYPES, not entries - an Options accessor does not move this number.");
        builder.AppendLine("# The number may only shrink; a growth is a deliberate surface addition.");

        foreach (var (package, count) in actual)
        {
            builder.Append(package).Append('=').Append(count).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "AgentPrism.Core.UnitTests",
        "Architecture",
        "public-surface-baseline.txt");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found. Searched upward from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
