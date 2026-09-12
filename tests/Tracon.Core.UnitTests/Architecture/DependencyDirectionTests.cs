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
/// Adding a ProjectReference that breaks this graph is forbidden.
/// Rationale: docs/MIMARI.md, section 2.
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
        // Tracon.Generators (Phase 52) is NOT added here as a runtime
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
        // multi-hop ProjectReference chain (measured, phase 93), so Workflows
        // needs its own analyzer reference to run TRC0501/TRC0502 over its
        // own async iterators. Never loaded as a runtime dependency.
        ["Tracon.Workflows"] = ["Tracon.Core", "Tracon.Generators"],
        ["Tracon.AspNetCore"] = ["Tracon.Core"],
        ["Tracon.UI"] = ["Tracon.AspNetCore"],
        // Testing is a test-helper package: it is NOT referenced by the meta
        // package (section 39.1). It references AspNetCore because the type
        // consumers ask for most is an in-memory host fixture, which requires
        // the package that wires up the endpoints.
        ["Tracon.Testing"] = ["Tracon.Core", "Tracon.AspNetCore"],
        // The behavior-contract suite for the store interfaces (Phase 98): it
        // depends on Abstractions ONLY, for the interfaces and record types
        // themselves. It is NOT referenced by the meta package (same reason
        // as Testing, section 39.1) and NOT referenced by Tracon.Testing
        // (the two test packages are independent; one binds no test
        // framework, the other binds xunit.v3 on purpose).
        ["Tracon.Testing.Contracts.Xunit"] = ["Tracon.Abstractions"],
        ["Tracon"] = ["Tracon.AspNetCore", "Tracon.Mcp", "Tracon.OpenAI", "Tracon.PostgreSql", "Tracon.UI", "Tracon.Workflows"],
        // Client is the CALLING side of the control plane, not the hosting
        // side: it takes NO Tracon reference at all. Its DTOs are
        // generated straight from the OpenAPI document, not reused from
        // Abstractions - reusing Abstractions would fight the code generator
        // and would leak server-side store interfaces (IRunStore and
        // similar) into an HTTP consumer that never needs them (Phase 83).
        ["Tracon.Client"] = [],
        // Cli is a dotnet tool, not a library a consumer references: it
        // needs Client for `health` (HTTP) and all three SQL providers for
        // `migrate` (direct database access, chosen because the app has not
        // started yet at that point - Phase 83, section 83.5).
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
                               "If the layer architecture changed, update docs/MIMARI.md and this test first.");
        }
    }

    [Fact]
    public void Abstractions_references_no_Tracon_package()
    {
        // Abstractions is a pure contract layer. It knows nothing about its own family.
        ReadTraconProjectReferences("Tracon.Abstractions").ShouldBeEmpty();
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
        // Phase 50: Tracon.AspNetCore was exposed as an MCP/A2A SERVER and
        // took on the ModelContextProtocol.AspNetCore + Microsoft.Agents.AI.Hosting.A2A +
        // Microsoft.Agents.AI.Hosting.AspNetCore + A2A.AspNetCore packages.
        // K-057's dependency direction must not break: these packages live
        // only inside Tracon.AspNetCore; Tracon.Mcp (the client) stays
        // on the `.Core` line and takes NONE of them. Phase 117 added the MCP
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
                "must stay inside Tracon.AspNetCore only (K-057).");
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
