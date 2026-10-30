using System.Xml.Linq;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Tests that enforce Tracon's layer architecture.
///
/// The dependency graph is ONE-DIRECTIONAL and contains no cycles:
///
///     Abstractions -- Core -- PostgreSql
///                       |  -- OpenAI
///                       |  -- Mcp
///                       +----- AspNetCore -- UI
///                       |         |            |
///                       |         |     Tracon (meta)
///                       +----- Testing (test helper; NOT referenced by the meta package)
///     Abstractions -- Testing.Contracts.Xunit (store contract suite; NOT referenced by the meta package)
///
/// The meta package reaches NO storage package (PostgreSql, SqlServer, Sqlite): a store is always an
/// explicit reference, so a host on another store never takes the PostgreSQL driver.
///
/// Adding a ProjectReference that breaks this graph is forbidden.
///
/// The tests read project files, not the build output. This way the rule
/// holds even before product code is written, and does not depend on build order.
/// </summary>
public sealed class DependencyDirectionTests
{
    /// <summary>
    /// The Tracon packages each package is allowed to reference.
    /// Any edge not listed here is a violation.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["Tracon.Abstractions"] = [],
        // Tracon.Generators is NOT added here as a runtime
        // dependency: its ProjectReference carries ReferenceOutputAssembly=false +
        // OutputItemType=Analyzer, meaning Core.dll never LOADS it - it is only
        // passed to the compiler as an analyzer. Even so, the <ProjectReference>
        // tag still shows up in the .csproj XML this test reads, so it must be
        // in the allow-list. Tracon.Generators itself is NOT a KEY of this
        // dictionary (it is not published, it carries no README requirement - 52.4).
        ["Tracon.Core"] = ["Tracon.Abstractions", "Tracon.Generators"],
        ["Tracon.PostgreSql"] = ["Tracon.Core"],
        ["Tracon.OpenAI"] = ["Tracon.Core"],
        // Anthropic and Google also depend only on Core; they cannot see each
        // other or OpenAI. Each provider package keeps its own SDK isolated.
        ["Tracon.Anthropic"] = ["Tracon.Core"],
        ["Tracon.Google"] = ["Tracon.Core"],
        ["Tracon.Azure"] = ["Tracon.Core"],
        // Voice is not a MODEL provider but follows the same isolation rule:
        // it depends only on Core and takes NO NuGet package (raw HttpClient).
        ["Tracon.Voice"] = ["Tracon.Core"],
        // Mcp depends only on Core: the HTTP layer triggers MCP refresh
        // through the IMcpToolRefresher abstraction; there is NO reverse
        // reference. This keeps MCP an optional package.
        ["Tracon.Mcp"] = ["Tracon.Core"],
        // Workflows likewise depends only on Core: the HTTP layer runs
        // workflows through the IWorkflowRunner abstraction and does NOT
        // reference this package. The exact same pattern as MCP.
        // Tracon.Generators is here for the same reason it is in Core's
        // entry above: OutputItemType=Analyzer does not propagate across a
        // multi-hop ProjectReference chain (measured), so Workflows
        // needs its own analyzer reference to run TRC0501/TRC0502 over its
        // own async iterators. Never loaded as a runtime dependency.
        ["Tracon.Workflows"] = ["Tracon.Core", "Tracon.Generators"],
        ["Tracon.AspNetCore"] = ["Tracon.Core"],
        ["Tracon.UI"] = ["Tracon.AspNetCore"],
        // AG-UI is a second run surface beside the management API. It depends on AspNetCore for the shared
        // run turn and the access protection, and it is NOT referenced by the meta package: a consumer whose front end
        // does not speak AG-UI keeps a dependency graph without the AG-UI SDK.
        ["Tracon.AGUI"] = ["Tracon.AspNetCore"],
        // Testing is a test-helper package: it is NOT referenced by the meta
        // package. It references AspNetCore because the type
        // consumers ask for most is an in-memory host fixture, which requires
        // the package that wires up the endpoints.
        ["Tracon.Testing"] = ["Tracon.Core", "Tracon.AspNetCore"],
        // The behavior-contract suite for the store interfaces: it
        // depends on Abstractions ONLY, for the interfaces and record types
        // themselves. It is NOT referenced by the meta package (same reason
        // as Testing) and NOT referenced by Tracon.Testing
        // (the two test packages are independent; one binds no test
        // framework, the other binds xunit.v3 on purpose).
        ["Tracon.Testing.Contracts.Xunit"] = ["Tracon.Abstractions"],
        // The meta package carries no storage package: a store is an explicit reference.
        ["Tracon"] = ["Tracon.AspNetCore", "Tracon.Mcp", "Tracon.OpenAI", "Tracon.UI", "Tracon.Workflows"],
        // Client is the CALLING side of the control plane, not the hosting
        // side: it takes NO Tracon reference at all. Its DTOs are
        // generated straight from the OpenAPI document, not reused from
        // Abstractions - reusing Abstractions would fight the code generator
        // and would leak server-side store interfaces (IRunStore and
        // similar) into an HTTP consumer that never needs them.
        ["Tracon.Client"] = [],
        // Cli is a dotnet tool, not a library a consumer references: it
        // needs Client for `health` (HTTP) and all three SQL providers for
        // `migrate` (direct database access, chosen because the app has not
        // started yet at that point).
        ["Tracon.Cli"] = ["Tracon.Client", "Tracon.PostgreSql", "Tracon.SqlServer", "Tracon.Sqlite"],
    };

    [Fact]
    public void Each_package_references_only_allowed_packages()
    {
        foreach (var (package, allowed) in AllowedReferences)
        {
            var actual = ReadTraconProjectReferences(package).Order(StringComparer.Ordinal).ToList();
            var expected = allowed.Order(StringComparer.Ordinal).ToList();

            actual.ShouldBe(
                expected,
                customMessage: $"'{package}' package's Tracon references differ from expected. " +
                               "If the layer architecture changed, update ARCHITECTURE.md and this test first.");
        }
    }

    [Fact]
    public void Abstractions_references_no_Tracon_package()
    {
        // Abstractions is a pure contract layer. It knows nothing about its own family.
        ReadTraconProjectReferences("Tracon.Abstractions").ShouldBeEmpty();
    }

    [Fact]
    public void The_speech_dialogue_contract_lives_in_abstractions_not_in_the_voice_package()
    {
        // The HTTP layer and a host's workflow code name these types, and
        // neither may reference Tracon.Voice (the earlier decision placed the speech contracts in
        // Abstractions for the same reason). The resolver that hands the capability
        // out is in Core; the ElevenLabs implementation stays in Voice.
        Type[] contract =
        [
            typeof(ISpeechDialogueSynthesizer),
            typeof(SpeechDialogueRequest),
            typeof(SpeechDialogueTurn),
            typeof(SpeechDialogueAudio),
            typeof(SpeechDialogueSegment),
            typeof(SpeechDialogueCapabilities),
            typeof(ISpeechProviderResolver),
        ];

        contract.ShouldAllBe(type => type.Assembly.GetName().Name == "Tracon.Abstractions");
        typeof(ISpeechProviderResolver).GetMethod(nameof(ISpeechProviderResolver.GetDialogueSynthesizerAsync))
            .ShouldNotBeNull()
            .ReturnType.ShouldBe(typeof(ValueTask<ISpeechDialogueSynthesizer>));
    }

    [Fact]
    public void Dependency_graph_contains_no_cycle()
    {
        var graph = AllowedReferences.Keys.ToDictionary(
            package => package,
            ReadTraconProjectReferences,
            StringComparer.Ordinal);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var package in graph.Keys)
        {
            var cycle = FindCycle(package, graph, visiting, visited, []);
            cycle.ShouldBeNull($"Dependency cycle found: {string.Join(" -> ", cycle ?? [])}");
        }
    }

    [Fact]
    public void Mcp_client_package_does_not_depend_on_server_packages()
    {
        // Tracon.AspNetCore was exposed as an MCP/A2A SERVER and
        // took on the ModelContextProtocol.AspNetCore + Microsoft.Agents.AI.Hosting.A2A +
        // Microsoft.Agents.AI.Hosting.AspNetCore + A2A.AspNetCore packages.
        // the dependency direction must not break: these packages live
        // only inside Tracon.AspNetCore; Tracon.Mcp (the client) stays
        // on the `.Core` line and takes NONE of them. An earlier change added the MCP
        // Tasks extension to the same server-only list.
        var forbidden = new[]
        {
            "ModelContextProtocol.AspNetCore",
            "ModelContextProtocol.Extensions.Tasks",
            "Microsoft.Agents.AI.Hosting.A2A",
            "Microsoft.Agents.AI.Hosting.AspNetCore",
            "A2A.AspNetCore",
        };

        var projectPath = Path.Combine(RepositoryRoot, "src", "Tracon.Mcp", "Tracon.Mcp.csproj");
        var references = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToList();

        foreach (var name in forbidden)
        {
            references.Any(reference => string.Equals(reference, name, StringComparison.Ordinal)).ShouldBeFalse(
                $"Tracon.Mcp must not take the '{name}' package; server dependencies " +
                "must stay inside Tracon.AspNetCore only.");
        }
    }

    [Fact]
    public void The_AGUI_sdk_is_taken_only_by_the_AGUI_package_and_the_meta_package_does_not_reach_it()
    {
        // The AG-UI SDK is a third-party dependency of a protocol most applications do not speak. It lives
        // in Tracon.AGUI alone, so Tracon.AspNetCore and the Tracon meta package keep a dependency graph without it. A consumer
        // opts in by installing Tracon.AGUI.
        var sourceDirectory = Path.Combine(RepositoryRoot, "src");

        var takers = Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(include => include is not null && include.StartsWith("AGUI.", StringComparison.Ordinal)))
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .ToList();

        takers.ShouldBe(["Tracon.AGUI"]);

        ReadTraconProjectReferences("Tracon").ShouldNotContain("Tracon.AGUI", StringComparer.Ordinal);
        ReadTraconProjectReferences("Tracon.AspNetCore").ShouldNotContain("Tracon.AGUI", StringComparer.Ordinal);
    }

    [Fact]
    public void The_meta_package_reaches_no_storage_package()
    {
        // The meta package is a control plane without a storage engine. A store is always an explicit
        // reference, so a host on SQL Server, SQLite or the in-memory stores never takes the PostgreSQL driver. The
        // closure is walked over project files (the shape the nuspec is packed from), not only the direct references.
        string[] storage = ["Tracon.PostgreSql", "Tracon.SqlServer", "Tracon.Sqlite"];

        var closure = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(ReadTraconProjectReferences("Tracon"));

        while (pending.TryPop(out var package))
        {
            if (!closure.Add(package))
            {
                continue;
            }

            foreach (var dependency in ReadTraconProjectReferences(package))
            {
                pending.Push(dependency);
            }
        }

        foreach (var name in storage)
        {
            closure.Contains(name).ShouldBeFalse(
                $"'{name}' is in the closure of the 'Tracon' meta package. The meta package must not reach a " +
                "storage package; a store is an explicit reference. " +
                "Reference the package from the host project instead.");
        }
    }

    [Fact]
    public void Every_publishable_package_contains_a_README_for_NuGet()
    {
        // The TraconValidatePackageReadme target in Directory.Build.targets
        // also enforces this during the build. This test documents the rationale.
        foreach (var package in AllowedReferences.Keys)
        {
            var readme = Path.Combine(RepositoryRoot, "src", package, "README.md");

            File.Exists(readme).ShouldBeTrue(
                $"Package '{package}' has no README.md. This file shows up on the NuGet.org package page.");
        }
    }

    private static IReadOnlyList<string> ReadTraconProjectReferences(string package)
    {
        var projectPath = Path.Combine(RepositoryRoot, "src", package, $"{package}.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Project file not found: {projectPath}");

        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .Where(name => name.StartsWith("Tracon", StringComparison.Ordinal))
            .ToList();
    }

    private static List<string>? FindCycle(
        string package,
        Dictionary<string, IReadOnlyList<string>> graph,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> path)
    {
        if (visited.Contains(package))
        {
            return null;
        }

        if (!visiting.Add(package))
        {
            return [.. path, package];
        }

        path.Add(package);

        foreach (var dependency in graph.GetValueOrDefault(package, []))
        {
            var cycle = FindCycle(dependency, graph, visiting, visited, path);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(package);
        visited.Add(package);

        return null;
    }

    /// <summary>
    /// Finds the repository root. Since the test build output lives under
    /// artifacts/, a fixed relative path cannot be used; instead the tree is
    /// walked upward searching for the Tracon.slnx file.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found. Searched upward from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
