using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Keeps the shipped API documentation answerable: every registration entry
/// point must show a worked call, and no example may teach an API that does not
/// exist.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CapabilityCoverageTests"/> proves the map <em>names</em> every
/// entry point. That is where a coding agent's first question ends and its
/// second one begins — "how is this called?". The answer already sits on the
/// consumer's disk, in the XML documentation each package carries into the
/// NuGet cache, so the only thing that has to be guarded is that the answer is
/// actually there.
/// </para>
/// <para>
/// A ratchet, not a snapshot, in the shape of
/// <see cref="CapabilityCoverageTests"/>. <c>capability-example-baseline.txt</c>
/// lists the entry points allowed to have no example; it was born empty and may
/// only shrink. A stale entry — one that has an example now — fails too.
/// </para>
/// <para>
/// Refresh after deliberately changing what is covered:
/// <c>TRACON_CAPABILITY_EXAMPLE_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class CapabilityExampleTests
{
    private const string RefreshEnvVar = "TRACON_CAPABILITY_EXAMPLE_REFRESH";

    /// <summary>
    /// The single framework the gate reads. The three target frameworks produce
    /// three different XML files (measured), but the difference comes from
    /// conditional compilation; every entry point exists under every one of
    /// them, so reading all three would triple the work and catch nothing.
    /// </summary>
    private const string ReadTargetFramework = "net10.0";

    /// <summary>
    /// Extension methods from Microsoft's own packages that an example is
    /// allowed to show. A Tracon member NEVER belongs here — the point of
    /// the list is to say "this name is not ours", so adding one of ours would
    /// be visible as exactly the wrong claim.
    /// </summary>
    private static readonly HashSet<string> ForeignRegistrationMembers = new(StringComparer.Ordinal)
    {
        "AddSingleton",
        "AddScoped",
        "AddTransient",
        "AddKeyedSingleton",
        "AddKeyedScoped",
        "AddKeyedTransient",
        "AddHostedService",
        "AddOpenApi",
        "AddHealthChecks",
        "MapHealthChecks",
    };

    /// <summary>An <c>Add</c>, <c>Use</c>, or <c>Map</c> call written in an example.</summary>
    private static readonly Regex RegistrationCallPattern = new(
        @"(?<name>(?:Add|Use|Map)[A-Za-z0-9_]+)\s*(?:<|\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_registration_entry_point_shows_a_worked_example()
    {
        var documented = ReadDocumentedMembers();
        var entryPoints = CapabilityEntryPoints.Names;

        entryPoints.ShouldNotBeEmpty("No entry point was read; the PublicAPI files or the parser changed shape.");

        // 🚨 Without this the gate goes quietly green on an unbuilt solution:
        // no XML means no member, and no member means nothing is uncovered.
        var unreadable = entryPoints
            .Where(name => !documented.ContainsKey(name))
            .ToList();

        unreadable.ShouldBeEmpty(
            customMessage: "No XML documentation was found for these entry points: " +
                           $"{string.Join(", ", unreadable)}.\n" +
                           "Build the solution first: dotnet build Tracon.slnx -c Release");

        var uncovered = entryPoints
            .Where(name => !documented[name].Any(member => member.HasExample))
            .ToList();

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
            failures.Add($"+ {name}: a registration entry point whose documentation carries no <example>");
        }

        foreach (var name in baseline.Where(name => !uncovered.Contains(name)))
        {
            failures.Add($"- {name}: documented with an <example> now, but still listed in the baseline");
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: "The public API and its worked examples disagree.\n" +
                           $"{string.Join('\n', failures)}\n" +
                           "Add an <example><code>...</code></example> to the member's documentation, or " +
                           $"refresh the baseline: {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    /// <summary>
    /// Proves the reader sees every entry point, including the generic ones. A
    /// generic member's documentation id carries an arity suffix
    /// (<c>AddContentGuard``1</c>); a name comparison that does not expect it
    /// skips three of the members without saying so.
    /// </summary>
    [Fact]
    public void The_reader_sees_every_entry_point_including_the_generic_ones()
    {
        var documented = ReadDocumentedMembers();

        foreach (var name in CapabilityEntryPoints.Names)
        {
            documented.ShouldContainKey(
                name,
                customMessage: $"'{name}' was never matched to a documented member. " +
                               "A generic member carries an arity suffix in its id; the reader must strip it.");
        }

        var generics = new[] { "AddContentGuard", "AddJobHandler", "AddToolsFrom", "AddWorkflowFunction" };

        foreach (var name in generics)
        {
            documented[name]
                .ShouldContain(
                    member => member.Id.Contains($"{name}``", StringComparison.Ordinal),
                    customMessage: $"No generic overload of '{name}' was read; the arity suffix handling regressed.");
        }
    }

    /// <summary>
    /// An example that names an API which no longer exists is worse than no
    /// example: the agent copies it and the consumer's build breaks. Same
    /// rationale as <c>DiagnosticIntegrityTests</c>.
    /// </summary>
    [Fact]
    public void No_example_teaches_a_registration_that_does_not_exist()
    {
        var failures = new List<string>();

        foreach (var members in ReadDocumentedMembers().Values)
        {
            foreach (var member in members.Where(member => member.HasExample))
            {
                foreach (Match call in RegistrationCallPattern.Matches(member.ExampleText))
                {
                    var called = call.Groups["name"].Value;

                    if (!CapabilityEntryPoints.AllMemberNames.Contains(called)
                        && !ForeignRegistrationMembers.Contains(called))
                    {
                        failures.Add($"{member.Id}: the example calls '{called}', which the public API does not declare");
                    }
                }
            }
        }

        failures.Sort(StringComparer.Ordinal);

        var distinct = failures.Distinct(StringComparer.Ordinal).ToList();

        distinct.ShouldBeEmpty(
            customMessage: $"An example teaches an API that does not exist.\n{string.Join('\n', distinct)}");
    }

    /// <summary>
    /// Guards the copy-and-paste failure that 27 hand-written examples invite:
    /// an example that never calls the member it documents teaches the wrong
    /// call while the coverage assertion stays green.
    /// </summary>
    [Fact]
    public void Every_example_calls_the_member_it_documents()
    {
        var documented = ReadDocumentedMembers();
        var failures = new List<string>();

        foreach (var (name, members) in documented)
        {
            foreach (var member in members.Where(member => member.HasExample))
            {
                if (!member.ExampleText.Contains(name, StringComparison.Ordinal))
                {
                    failures.Add($"{member.Id}: the example never calls '{name}'");
                }
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"An example documents one member and shows another.\n{string.Join('\n', failures)}");
    }

    /// <summary>One documented member: its id, and its example text if it has one.</summary>
    private sealed record DocumentedMember(string Id, bool HasExample, string ExampleText);

    /// <summary>
    /// Reads the built XML documentation and groups every member by the entry
    /// point name it belongs to.
    /// </summary>
    /// <remarks>
    /// The package directory and the file name must match, otherwise the copy
    /// of a dependency's documentation that lands in another package's output
    /// would be read a second time.
    /// </remarks>
    private static Dictionary<string, IReadOnlyList<DocumentedMember>> ReadDocumentedMembers()
    {
        var byName = new SortedDictionary<string, List<DocumentedMember>>(StringComparer.Ordinal);
        var patterns = CapabilityEntryPoints.Names.ToDictionary(
            name => name,
            name => new Regex(
                $@"[.:]{Regex.Escape(name)}(?:``?\d+)?(?:\(|$)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5)),
            StringComparer.Ordinal);

        foreach (var file in DocumentationFiles())
        {
            foreach (var member in XDocument.Load(file).Descendants("member"))
            {
                var id = member.Attribute("name")?.Value;

                if (id is null)
                {
                    continue;
                }

                var example = member.Descendants("example").FirstOrDefault();

                foreach (var (name, pattern) in patterns.Where(entry => entry.Value.IsMatch(id)))
                {
                    if (!byName.TryGetValue(name, out var members))
                    {
                        members = [];
                        byName[name] = members;
                    }

                    members.Add(new DocumentedMember(id, example is not null, example?.Value ?? string.Empty));
                }
            }
        }

        return byName.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<DocumentedMember>)entry.Value,
            StringComparer.Ordinal);
    }

    private static IEnumerable<string> DocumentationFiles()
    {
        var binaries = Path.Combine(CapabilityEntryPoints.RepositoryRoot, "artifacts", "bin");

        if (!Directory.Exists(binaries))
        {
            yield break;
        }

        foreach (var package in CapabilityEntryPoints.Packages.Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(
                binaries,
                package,
                $"{Configuration}_{ReadTargetFramework}",
                $"{package}.xml");

            if (File.Exists(file))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The build configuration this test run was built in. With
    /// <c>UseArtifactsOutput</c> the test assembly lands in
    /// <c>artifacts/bin/&lt;project&gt;/&lt;configuration&gt;</c>, or in
    /// <c>&lt;configuration&gt;_&lt;tfm&gt;</c> once the project targets more than
    /// one framework (phase 183), so the last segment up to the first
    /// underscore names it; reading the wrong configuration's documentation
    /// would silently report a stale answer.
    /// </summary>
    private static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory).Name.Split('_')[0];

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

        builder.Append("# Generated by CapabilityExampleTests. Refresh:\n");
        builder.Append($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release\n");
        builder.Append("# One registration entry point per line: allowed to carry no <example>.\n");
        builder.Append("# The list may only shrink. It was born empty and that is the intended state.\n");

        foreach (var name in uncovered.OrderBy(name => name, StringComparer.Ordinal))
        {
            builder.Append(name).Append('\n');
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string BaselinePath { get; } = Path.Combine(
        CapabilityEntryPoints.RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "capability-example-baseline.txt");
}
