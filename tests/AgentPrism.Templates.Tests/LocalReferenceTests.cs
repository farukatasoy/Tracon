using System.Text.Json;
using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// The consumer-facing half of Phase 74: the <c>buildTransitive</c> target that
/// writes <c>AgentPrism.LocalReference.md</c> beside each project, and the
/// OpenAPI document that <c>AgentPrism.AspNetCore</c> now carries.
/// </summary>
/// <remarks>
/// <para>
/// Every behaviour here crosses the package boundary, so a unit test cannot
/// prove any of it: the target only exists inside the <c>.nupkg</c>, the paths
/// it writes only exist once NuGet has extracted the packages, and
/// <c>%(ReferencePath.NuGetPackageId)</c> is empty for the
/// <c>ProjectReference</c> build this repository does.
/// </para>
/// <para>
/// 🚨 The packages are consumed from the local feed that <see cref="TemplateFixture"/>
/// packs. MinVer keeps the version fixed between commits, so a repacked
/// <c>.nupkg</c> is never extracted again — the fixture clears the global
/// package folder for exactly that reason, and a hand-run experiment outside
/// these tests has to do the same.
/// </para>
/// </remarks>
public sealed class LocalReferenceTests(TemplateFixture fixture)
{
    private const string LocalReferenceFileName = "AgentPrism.LocalReference.md";

    private const string DoNotCommitMarker = "do not commit";

    private const string HttpSectionHeading = "## HTTP API document";

    private const string CapabilityMapHeading = "## Capability map - read this first";

    /// <summary>Mirrors the marker in <c>docs-site/scripts/build-agent-map.mjs</c>.</summary>
    private const string MarkerOpening = "<!-- AgentPrism agent map \u00b7 revision: ";

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Property_unset_writes_no_file()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        File.Exists(Path.Combine(projectDirectory, LocalReferenceFileName)).ShouldBeFalse(build.Combined);
    }

    [Fact]
    public async Task The_second_property_turns_the_file_off_on_its_own()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            properties: "<AgentPrismWriteAgentsFile>true</AgentPrismWriteAgentsFile>" +
                        "<AgentPrismWriteLocalReference>false</AgentPrismWriteLocalReference>");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);

        // The map still arrives; only the pointer file is off.
        File.Exists(Path.Combine(directory.Path, "AGENTS.md")).ShouldBeTrue(build.Combined);
        File.Exists(Path.Combine(projectDirectory, LocalReferenceFileName)).ShouldBeFalse(build.Combined);
    }

    [Fact]
    public async Task One_switch_writes_a_file_whose_every_path_exists()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        // Only AgentPrismWriteAgentsFile is set: the two files share one switch,
        // so opting in does not get harder than it was in Phase 73.
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var file = Path.Combine(projectDirectory, LocalReferenceFileName);
        File.Exists(file).ShouldBeTrue(build.Combined);

        var written = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

        written.ShouldContain(DoNotCommitMarker);

        // The version is not a header line: it lives in every path, which is the
        // only honest place for it when two packages resolve at two versions.
        written.ShouldContain(fixture.Version);

        var documentationFiles = DocumentationPaths(written);

        documentationFiles.ShouldNotBeEmpty(written);

        foreach (var path in documentationFiles)
        {
            File.Exists(path).ShouldBeTrue($"'{path}' is named in the local reference but does not exist.\n{written}");

            // The pointer is worthless unless the file it names really answers
            // the question the map cannot: how a member is called.
            var documentation = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            documentation.ShouldStartWith("<?xml");
        }

        documentationFiles.ShouldContain(path => path.EndsWith("AgentPrism.Core.xml", StringComparison.Ordinal), written);
    }

    /// <summary>
    /// The reason this phase exists: the map is on disk in the NuGet cache, and
    /// before this section nothing generated ever named its path, so an agent in
    /// a repository with its own AGENTS.md could not reach it at all.
    /// </summary>
    /// <remarks>
    /// The section is asserted to be FIRST because the order encodes the order
    /// of the questions: what exists, then how it is called.
    /// </remarks>
    [Fact]
    public async Task The_first_section_is_the_capability_map_and_the_path_it_names_is_real()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var written = await File.ReadAllTextAsync(
            Path.Combine(projectDirectory, LocalReferenceFileName),
            TestContext.Current.CancellationToken);

        var sections = written
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();

        sections.ShouldNotBeEmpty(written);
        sections[0].ShouldBe(CapabilityMapHeading, written);

        var map = written
            .Split('\n')
            .Select(line => line.Trim())
            .First(line => line.StartsWith("- ", StringComparison.Ordinal)
                           && line.EndsWith("AgentPrism.AgentMap.md", StringComparison.Ordinal))[2..];

        // A dead path is worse than no path: the agent spends a turn on it and
        // learns nothing.
        File.Exists(map).ShouldBeTrue($"'{map}' is named in the local reference but does not exist.\n{written}");

        var contents = await File.ReadAllTextAsync(map, TestContext.Current.CancellationToken);

        contents.ShouldStartWith(MarkerOpening, Case.Sensitive, written);
        contents.ShouldContain("## Capabilities", customMessage: written);
    }

    [Fact]
    public async Task A_second_build_leaves_the_file_untouched()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        (await BuildAsync(projectDirectory)).ExitCode.ShouldBe(0);

        var file = Path.Combine(projectDirectory, LocalReferenceFileName);
        var written = File.GetLastWriteTimeUtc(file);

        var second = await ProcessRunner.RunAsync("dotnet", "build -c Release -t:Rebuild", projectDirectory, BuildTimeout);

        second.ExitCode.ShouldBe(0, second.Combined);
        File.GetLastWriteTimeUtc(file).ShouldBe(written, "WriteOnlyWhenDifferent must leave unchanged content alone.");
    }

    [Fact]
    public async Task A_core_only_consumer_gets_no_HTTP_section()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            packageId: "AgentPrism.Core",
            sdk: "Microsoft.NET.Sdk",
            program: "// The HTTP layer is not referenced here.\n");

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var written = await File.ReadAllTextAsync(
            Path.Combine(projectDirectory, LocalReferenceFileName),
            TestContext.Current.CancellationToken);

        written.ShouldNotContain(HttpSectionHeading);
        written.ShouldContain("AgentPrism.Core");
    }

    /// <summary>
    /// Proves three things at once: the document is inside the package, the
    /// <c>buildTransitive</c> file carries the contract name NuGet imports by
    /// itself, and the path written into the local reference is real.
    /// </summary>
    [Fact]
    public async Task The_HTTP_document_travels_with_the_package_and_is_readable()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var written = await File.ReadAllTextAsync(
            Path.Combine(projectDirectory, LocalReferenceFileName),
            TestContext.Current.CancellationToken);

        written.ShouldContain(HttpSectionHeading);

        var document = written
            .Split('\n')
            .Select(line => line.Trim())
            .First(line => line.StartsWith("- ", StringComparison.Ordinal)
                           && line.EndsWith("agentprism.json", StringComparison.Ordinal))[2..];

        File.Exists(document).ShouldBeTrue($"'{document}' is named in the local reference but does not exist.\n{written}");

        using var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(document, TestContext.Current.CancellationToken));

        json.RootElement.GetProperty("paths").EnumerateObject().Count().ShouldBeGreaterThan(100);
    }

    /// <summary>
    /// A write that cannot succeed warns; it never fails the build.
    /// </summary>
    /// <remarks>
    /// The guarantee is what matters, not one way of breaking the write: a
    /// convenience file must not stop a consumer's build. A directory standing
    /// where the file belongs states that precisely and on every platform,
    /// unlike a read-only source tree, whose effect depends on where the build
    /// puts <c>bin</c> and <c>obj</c>.
    /// </remarks>
    [Fact]
    public async Task A_write_that_cannot_succeed_only_warns()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        Directory.CreateDirectory(Path.Combine(projectDirectory, LocalReferenceFileName));

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("MSB", customMessage: build.Combined);
    }

    /// <summary>
    /// Two projects that reference DIFFERENT AgentPrism packages - the ordinary
    /// web-host-plus-worker split - each get their own answer.
    /// </summary>
    /// <remarks>
    /// 🚨 This is why the file sits beside the project and not at the repository
    /// root. Measured with one shared file at the root: whichever project built
    /// last won, the web host lost its HTTP document, and the content flipped
    /// between builds. A single file cannot hold two different answers, and
    /// merging cannot help either - the two projects build in parallel and race
    /// each other on the read.
    /// </remarks>
    [Fact]
    public async Task Two_projects_with_different_references_each_get_their_own_answer()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        var web = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            subdirectory: "src/Web",
            projectName: "Web");

        var worker = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            subdirectory: "src/Worker",
            projectName: "Worker",
            packageId: "AgentPrism.Core",
            sdk: "Microsoft.NET.Sdk",
            program: "// The HTTP layer is not referenced here.\n");

        var solution = await ProcessRunner.RunAsync("dotnet", "new sln -n Both", directory.Path, BuildTimeout);
        solution.ExitCode.ShouldBe(0, solution.Combined);

        var added = await ProcessRunner.RunAsync(
            "dotnet",
            "sln Both.sln add src/Web/Web.csproj src/Worker/Worker.csproj",
            directory.Path,
            BuildTimeout);
        added.ExitCode.ShouldBe(0, added.Combined);

        var build = await ProcessRunner.RunAsync("dotnet", "build Both.sln -c Release", directory.Path, BuildTimeout);
        build.ExitCode.ShouldBe(0, build.Combined);

        var webFile = Path.Combine(web, LocalReferenceFileName);
        var workerFile = Path.Combine(worker, LocalReferenceFileName);

        File.Exists(webFile).ShouldBeTrue(build.Combined);
        File.Exists(workerFile).ShouldBeTrue(build.Combined);

        // Nothing lands at the repository root: AGENTS.md owns that name space.
        File.Exists(Path.Combine(directory.Path, LocalReferenceFileName)).ShouldBeFalse(build.Combined);

        var webText = await File.ReadAllTextAsync(webFile, TestContext.Current.CancellationToken);
        var workerText = await File.ReadAllTextAsync(workerFile, TestContext.Current.CancellationToken);

        webText.ShouldContain(HttpSectionHeading);
        webText.ShouldContain("AgentPrism.AspNetCore");

        workerText.ShouldNotContain(HttpSectionHeading);
        workerText.ShouldNotContain("AgentPrism.AspNetCore");

        // Neither answer moves once written: no project writes the other's file.
        var stamps = new[] { File.GetLastWriteTimeUtc(webFile), File.GetLastWriteTimeUtc(workerFile) };

        var second = await ProcessRunner.RunAsync("dotnet", "build Both.sln -c Release -t:Rebuild", directory.Path, BuildTimeout);
        second.ExitCode.ShouldBe(0, second.Combined);

        File.GetLastWriteTimeUtc(webFile).ShouldBe(stamps[0]);
        File.GetLastWriteTimeUtc(workerFile).ShouldBe(stamps[1]);
    }

    [Fact]
    public async Task A_path_with_a_space_and_a_non_ascii_character_survives()
    {
        using var directory = new TempDirectory();

        // The repository root itself carries the awkward characters, so both the
        // destination path and the project path go through them.
        var root = Path.Combine(directory.Path, "Café Solutions");
        Directory.CreateDirectory(root);
        await InitialiseRepositoryAsync(root);

        var projectDirectory = await CreateConsumerAsync(root, writeAgentsFile: true, subdirectory: "src/Órder Service");

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var file = Path.Combine(projectDirectory, LocalReferenceFileName);
        File.Exists(file).ShouldBeTrue(build.Combined);

        var written = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

        foreach (var path in DocumentationPaths(written))
        {
            File.Exists(path).ShouldBeTrue($"'{path}' is named in the local reference but does not exist.\n{written}");
        }
    }

    /// <summary>
    /// The documentation paths the file names, one per referenced package.
    /// </summary>
    private static List<string> DocumentationPaths(string written)
        => written
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- AgentPrism", StringComparison.Ordinal)
                           && line.EndsWith(".xml", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim())
            .ToList();

    /// <summary>
    /// Writes a consumer project that references a packed package, and returns
    /// its directory. The meta package is the default: a build/ asset would not
    /// survive that transitive hop, which is why both targets ship under
    /// buildTransitive/.
    /// </summary>
    private async Task<string> CreateConsumerAsync(
        string root,
        bool writeAgentsFile = false,
        string? properties = null,
        string subdirectory = "src/Consumer",
        string projectName = "Consumer",
        string packageId = "AgentPrism",
        string sdk = "Microsoft.NET.Sdk.Web",
        string? program = null)
    {
        var projectDirectory = Path.Combine(root, subdirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(projectDirectory);
        await TemplateFixture.WriteLocalNuGetConfigAsync(root);

        properties ??= writeAgentsFile
            ? "<AgentPrismWriteAgentsFile>true</AgentPrismWriteAgentsFile>"
            : string.Empty;

        var project = $"""
            <Project Sdk="{sdk}">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RootNamespace>{projectName}</RootNamespace>
                {properties}
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{packageId}" Version="{fixture.Version}" />
              </ItemGroup>

            </Project>
            """;

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, $"{projectName}.csproj"),
            project,
            TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            program ?? WebProgram,
            TestContext.Current.CancellationToken);

        return projectDirectory;
    }

    private const string WebProgram = """
        using AgentPrism;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddAgentPrism();

        var app = builder.Build();
        app.MapAgentPrism();
        app.Run();
        """;

    private static async Task InitialiseRepositoryAsync(string root)
    {
        var result = await ProcessRunner.RunAsync("git", "init", root, TimeSpan.FromMinutes(1));

        result.ExitCode.ShouldBe(0, result.Combined);
    }

    private static Task<ProcessResult> BuildAsync(string projectDirectory)
        => ProcessRunner.RunAsync("dotnet", "build -c Release", projectDirectory, BuildTimeout);
}
