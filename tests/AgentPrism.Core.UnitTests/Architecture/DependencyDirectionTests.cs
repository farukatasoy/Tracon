using System.Xml.Linq;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Tests that enforce AgentPrism's layer architecture.
///
/// The dependency graph is ONE-DIRECTIONAL and contains no cycles:
///
///     Abstractions -- Core -- PostgreSql
///                       |  -- OpenAI
///                       |  -- Mcp
///                       +----- AspNetCore -- UI
///                       |         |            |
///                       |         |     AgentPrism (meta)
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
    /// The AgentPrism packages each package is allowed to reference.
    /// Any edge not listed here is a violation.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["AgentPrism.Abstractions"] = [],
        // AgentPrism.Generators (Phase 52) is NOT added here as a runtime
        // dependency: its ProjectReference carries ReferenceOutputAssembly=false +
        // OutputItemType=Analyzer, meaning Core.dll never LOADS it - it is only
        // passed to the compiler as an analyzer. Even so, the <ProjectReference>
        // tag still shows up in the .csproj XML this test reads, so it must be
        // in the allow-list. AgentPrism.Generators itself is NOT a KEY of this
        // dictionary (it is not published, it carries no README requirement - 52.4).
        ["AgentPrism.Core"] = ["AgentPrism.Abstractions", "AgentPrism.Generators"],
        ["AgentPrism.PostgreSql"] = ["AgentPrism.Core"],
        ["AgentPrism.OpenAI"] = ["AgentPrism.Core"],
        // Anthropic and Google also depend only on Core; they cannot see each
        // other or OpenAI. Each provider package keeps its own SDK isolated.
        ["AgentPrism.Anthropic"] = ["AgentPrism.Core"],
        ["AgentPrism.Google"] = ["AgentPrism.Core"],
        ["AgentPrism.Azure"] = ["AgentPrism.Core"],
        // Voice is not a MODEL provider but follows the same isolation rule:
        // it depends only on Core and takes NO NuGet package (raw HttpClient).
        ["AgentPrism.Voice"] = ["AgentPrism.Core"],
        // Mcp depends only on Core: the HTTP layer triggers MCP refresh
        // through the IMcpToolRefresher abstraction; there is NO reverse
        // reference. This keeps MCP an optional package.
        ["AgentPrism.Mcp"] = ["AgentPrism.Core"],
        // Workflows likewise depends only on Core: the HTTP layer runs
        // workflows through the IWorkflowRunner abstraction and does NOT
        // reference this package. The exact same pattern as MCP.
        // AgentPrism.Generators is here for the same reason it is in Core's
        // entry above: OutputItemType=Analyzer does not propagate across a
        // multi-hop ProjectReference chain (measured, phase 93), so Workflows
        // needs its own analyzer reference to run APG0501/APG0502 over its
        // own async iterators. Never loaded as a runtime dependency.
        ["AgentPrism.Workflows"] = ["AgentPrism.Core", "AgentPrism.Generators"],
        ["AgentPrism.AspNetCore"] = ["AgentPrism.Core"],
        ["AgentPrism.UI"] = ["AgentPrism.AspNetCore"],
        // Testing is a test-helper package: it is NOT referenced by the meta
        // package (section 39.1). It references AspNetCore because the type
        // consumers ask for most is an in-memory host fixture, which requires
        // the package that wires up the endpoints.
        ["AgentPrism.Testing"] = ["AgentPrism.Core", "AgentPrism.AspNetCore"],
        // The behavior-contract suite for the store interfaces (Phase 98): it
        // depends on Abstractions ONLY, for the interfaces and record types
        // themselves. It is NOT referenced by the meta package (same reason
        // as Testing, section 39.1) and NOT referenced by AgentPrism.Testing
        // (the two test packages are independent; one binds no test
        // framework, the other binds xunit.v3 on purpose).
        ["AgentPrism.Testing.Contracts.Xunit"] = ["AgentPrism.Abstractions"],
        ["AgentPrism"] = ["AgentPrism.AspNetCore", "AgentPrism.Mcp", "AgentPrism.OpenAI", "AgentPrism.PostgreSql", "AgentPrism.UI", "AgentPrism.Workflows"],
        // Client is the CALLING side of the control plane, not the hosting
        // side: it takes NO AgentPrism reference at all. Its DTOs are
        // generated straight from the OpenAPI document, not reused from
        // Abstractions - reusing Abstractions would fight the code generator
        // and would leak server-side store interfaces (IRunStore and
        // similar) into an HTTP consumer that never needs them (Phase 83).
        ["AgentPrism.Client"] = [],
        // Cli is a dotnet tool, not a library a consumer references: it
        // needs Client for `health` (HTTP) and all three SQL providers for
        // `migrate` (direct database access, chosen because the app has not
        // started yet at that point - Phase 83, section 83.5).
        ["AgentPrism.Cli"] = ["AgentPrism.Client", "AgentPrism.PostgreSql", "AgentPrism.SqlServer", "AgentPrism.Sqlite"],
    };

    [Fact]
    public void Each_package_references_only_allowed_packages()
    {
        foreach (var (package, allowed) in AllowedReferences)
        {
            var actual = ReadAgentPrismProjectReferences(package).Order(StringComparer.Ordinal).ToList();
            var expected = allowed.Order(StringComparer.Ordinal).ToList();

            actual.ShouldBe(
                expected,
                customMessage: $"'{package}' package's AgentPrism references differ from expected. " +
                               "If the layer architecture changed, update docs/MIMARI.md and this test first.");
        }
    }

    [Fact]
    public void Abstractions_references_no_AgentPrism_package()
    {
        // Abstractions is a pure contract layer. It knows nothing about its own family.
        ReadAgentPrismProjectReferences("AgentPrism.Abstractions").ShouldBeEmpty();
    }

    [Fact]
    public void Dependency_graph_contains_no_cycle()
    {
        var graph = AllowedReferences.Keys.ToDictionary(
            package => package,
            ReadAgentPrismProjectReferences,
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
        // Phase 50: AgentPrism.AspNetCore was exposed as an MCP/A2A SERVER and
        // took on the ModelContextProtocol.AspNetCore + Microsoft.Agents.AI.Hosting.A2A +
        // Microsoft.Agents.AI.Hosting.AspNetCore + A2A.AspNetCore packages.
        // K-057's dependency direction must not break: these packages live
        // only inside AgentPrism.AspNetCore; AgentPrism.Mcp (the client) stays
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

        var projectPath = Path.Combine(RepositoryRoot, "src", "AgentPrism.Mcp", "AgentPrism.Mcp.csproj");
        var references = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToList();

        foreach (var name in forbidden)
        {
            references.Any(reference => string.Equals(reference, name, StringComparison.Ordinal)).ShouldBeFalse(
                $"AgentPrism.Mcp must not take the '{name}' package; server dependencies " +
                "must stay inside AgentPrism.AspNetCore only (K-057).");
        }
    }

    [Fact]
    public void Every_publishable_package_contains_a_README_for_NuGet()
    {
        // The AgentPrismValidatePackageReadme target in Directory.Build.targets
        // also enforces this during the build. This test documents the rationale.
        foreach (var package in AllowedReferences.Keys)
        {
            var readme = Path.Combine(RepositoryRoot, "src", package, "README.md");

            File.Exists(readme).ShouldBeTrue(
                $"Package '{package}' has no README.md. This file shows up on the NuGet.org package page.");
        }
    }

    private static IReadOnlyList<string> ReadAgentPrismProjectReferences(string package)
    {
        var projectPath = Path.Combine(RepositoryRoot, "src", package, $"{package}.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Project file not found: {projectPath}");

        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .Where(name => name.StartsWith("AgentPrism", StringComparison.Ordinal))
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
    /// walked upward searching for the AgentPrism.slnx file.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

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
