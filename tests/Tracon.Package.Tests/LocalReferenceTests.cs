using System.Text.Json;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The consumer-facing side of the local reference: the <c>buildTransitive</c> target that
/// writes <c>Tracon.LocalReference.md</c> beside each project, and the
/// OpenAPI document that <c>Tracon.AspNetCore</c> now carries.
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
    private const string LocalReferenceFileName = "Tracon.LocalReference.md";

    private const string DoNotCommitMarker = "do not commit";

    private const string HttpSectionHeading = "## HTTP API document";

    private const string CapabilityMapHeading = "## Capability map - read this first";

    /// <summary>Mirrors the marker in <c>docs-site/scripts/build-agent-map.mjs</c>.</summary>
    private const string MarkerOpening = "<!-- Tracon agent map \u00b7 revision: ";

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
            properties: "<TraconWriteAgentsFile>true</TraconWriteAgentsFile>" +
                        "<TraconWriteLocalReference>false</TraconWriteLocalReference>");

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

        // Only TraconWriteAgentsFile is set: the two files share one switch,
        // so opting in does not get harder than it was.
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

        documentationFiles.ShouldContain(path => path.EndsWith("Tracon.Core.xml", StringComparison.Ordinal), written);
    }

    /// <summary>
    /// The reason this change exists: the map is on disk in the NuGet cache, and
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
                           && line.EndsWith("Tracon.AgentMap.md", StringComparison.Ordinal))[2..];

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
            packageId: "Tracon.Core",
            sdk: "Microsoft.NET.Sdk",
            program: "// The HTTP layer is not referenced here.\n");

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var written = await File.ReadAllTextAsync(
            Path.Combine(projectDirectory, LocalReferenceFileName),
            TestContext.Current.CancellationToken);

        written.ShouldNotContain(HttpSectionHeading);
        written.ShouldContain("Tracon.Core");
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
                           && line.EndsWith("tracon.json", StringComparison.Ordinal))[2..];

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
    /// In a git worktree <c>.git</c> is a file that points elsewhere, not a directory with a <c>config</c> in it. The
    /// instructions file still belongs at the root of the worktree: that is where a coding agent started there looks.
    /// </summary>
    [Fact]
    public async Task The_agents_file_goes_to_the_root_of_a_git_worktree()
    {
        using var directory = new TempDirectory();
        var main = Path.Combine(directory.Path, "main");
        Directory.CreateDirectory(main);
        await InitialiseRepositoryAsync(main);

        // A worktree branches from a commit, and a fresh repository has none.
        var commit = await ProcessRunner.RunAsync(
            "git",
            "-c user.name=test -c user.email=test@example.invalid commit --allow-empty -m initial",
            main,
            TimeSpan.FromMinutes(1));
        commit.ExitCode.ShouldBe(0, commit.Combined);

        var worktree = Path.Combine(directory.Path, "worktree");
        var add = await ProcessRunner.RunAsync("git", $"worktree add --detach \"{worktree}\"", main, TimeSpan.FromMinutes(1));
        add.ExitCode.ShouldBe(0, add.Combined);
        File.Exists(Path.Combine(worktree, ".git")).ShouldBeTrue("A worktree's .git is expected to be a file.");

        var projectDirectory = await CreateConsumerAsync(worktree, writeAgentsFile: true);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        File.Exists(Path.Combine(worktree, "AGENTS.md")).ShouldBeTrue(build.Combined);
        File.Exists(Path.Combine(projectDirectory, "AGENTS.md")).ShouldBeFalse("The file was written beside the project, not at the worktree root.\n" + build.Combined);
    }

    /// <summary>
    /// The build hands the consumer's own instructions files to the analyzer whenever they exist, and a compiler that is
    /// given something it cannot open as an additional file stops with an error. A directory that carries the name of
    /// one of them is not a file, so it is left out.
    /// </summary>
    /// <param name="relativePath">The path of the file the analyzer reads, relative to the repository root.</param>
    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData(".claude/skills/tracon/SKILL.md")]
    public async Task A_directory_named_like_a_file_the_analyzer_reads_does_not_break_the_build(string relativePath)
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path);

        Directory.CreateDirectory(Path.Combine(directory.Path, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
    }

    /// <summary>
    /// A link that points nowhere cannot be told from a file by the build (File.Exists is true for it), and the compiler
    /// stops with an error when the analyzer's input is missing. The switch that turns the usage diagnostics off is the
    /// way out for a consumer with one, and it works because the compiler never opens the files when every rule of the
    /// analyzer is suppressed. Measured, not designed: this pins the way out so that a change in the build or the
    /// compiler that closes it is noticed.
    /// </summary>
    [Fact]
    public async Task The_usage_switch_is_a_way_out_for_a_link_that_points_nowhere()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a symbolic link needs a privilege on Windows.");

        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            properties: "<TraconUsageDiagnostics>false</TraconUsageDiagnostics>");

        File.CreateSymbolicLink(Path.Combine(directory.Path, "AGENTS.md"), Path.Combine(directory.Path, "missing-target.md"));

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
    }

    /// <summary>
    /// A copy that cannot succeed fails once and moves on: it does not retry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MSBuild's <c>Copy</c> task retries a failed copy ten times, a second apart, and logs a warning for each. In a
    /// read-only checkout (an ordinary CI mount, and the very case the target says it survives) that is ten seconds
    /// added to every build of every project, and ten warnings that a consumer who treats warnings as errors sees as a
    /// failed build. The file is a convenience; nothing is gained by waiting for it.
    /// </para>
    /// <para>
    /// The repository root is a regular file here, so <c>AGENTS.md</c> can neither exist nor be created under it. That
    /// fails the same copy on every platform and for every user, which a permission bit does not: root ignores it, and
    /// the test cannot tell the two apart.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_copy_that_cannot_succeed_is_not_retried()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        var notADirectory = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(notADirectory, string.Empty, TestContext.Current.CancellationToken);

        var build = await ProcessRunner.RunAsync(
            "dotnet",
            $"build -c Release -p:_TraconRepositoryRoot=\"{notADirectory}\"",
            projectDirectory,
            BuildTimeout);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("MSB3021", customMessage: "The copy was expected to fail and warn once.\n" + build.Combined);
        build.Combined.ShouldNotContain("MSB3026", Case.Sensitive, "The copy was retried.\n" + build.Combined);
        build.Combined.ShouldNotContain("MSB3027", Case.Sensitive, "The copy exhausted its retries.\n" + build.Combined);
    }

    /// <summary>
    /// A consumer who turns MSBuild warnings into errors stops on a copy that cannot succeed, and says no to the file
    /// to get past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target warns and goes on (<see cref="A_copy_that_cannot_succeed_is_not_retried"/>), and a warning is exactly
    /// what a strict build refuses. Measured: no change that keeps the file and stays silent here is both cheap and
    /// complete, so the answer for a strict consumer on a tree it cannot write to is the property that switches the
    /// files off. This pins that answer: it fails now, and a change that makes the write silent has to change this test
    /// and the page that tells the consumer what to do.
    /// </para>
    /// <para>
    /// Both strictness switches are set, as a CI command line sets them: the CLI switch converts the warnings of the
    /// build, and the property converts the ones the MSBuild tasks log.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_strict_build_stops_on_a_copy_that_cannot_succeed_and_the_property_is_the_way_out()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        var notADirectory = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(notADirectory, string.Empty, TestContext.Current.CancellationToken);

        var root = $"-p:_TraconRepositoryRoot=\"{notADirectory}\"";

        var strict = await StrictBuildAsync(projectDirectory, root);

        strict.ExitCode.ShouldNotBe(0, strict.Combined);
        strict.Combined.ShouldContain("error MSB3021", customMessage: strict.Combined);

        var exit = await StrictBuildAsync(projectDirectory, root, "-p:TraconWriteAgentsFile=false");

        exit.ExitCode.ShouldBe(0, exit.Combined);
        exit.Combined.ShouldNotContain("MSB3021", Case.Sensitive, exit.Combined);
    }

    /// <summary>
    /// The pointer file is the other write a strict build can stop on, and it has its own way out: turning only that
    /// file off keeps the map.
    /// </summary>
    [Fact]
    public async Task A_strict_build_stops_on_a_write_that_cannot_succeed_and_the_second_property_is_the_way_out()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true);

        Directory.CreateDirectory(Path.Combine(projectDirectory, LocalReferenceFileName));

        var strict = await StrictBuildAsync(projectDirectory);

        strict.ExitCode.ShouldNotBe(0, strict.Combined);
        strict.Combined.ShouldContain("error MSB3491", customMessage: strict.Combined);

        var exit = await StrictBuildAsync(projectDirectory, "-p:TraconWriteLocalReference=false");

        exit.ExitCode.ShouldBe(0, exit.Combined);
        File.Exists(Path.Combine(directory.Path, "AGENTS.md")).ShouldBeTrue("The map still arrives; only the pointer file is off.\n" + exit.Combined);
    }

    /// <summary>
    /// A file the build user cannot open stops the build, and the usage switch is the way out, exactly as for a link
    /// that points nowhere.
    /// </summary>
    /// <remarks>
    /// Each build gets its own project: the compiler leaves its output behind after a failed build, so a second build
    /// of the same project passes without reading the file again and would report a way out that is not one.
    /// </remarks>
    [Fact]
    public async Task An_instructions_file_nobody_can_read_stops_the_build_and_the_usage_switch_is_the_way_out()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File permission bits do not describe access on Windows.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "A privileged user reads a file whatever its mode says.");

        using var blocked = new TempDirectory();
        await InitialiseRepositoryAsync(blocked.Path);
        var blockedProject = await CreateConsumerAsync(blocked.Path);
        await WriteUnreadableInstructionsAsync(blocked.Path);

        var build = await BuildAsync(blockedProject);

        build.ExitCode.ShouldNotBe(0, build.Combined);
        build.Combined.ShouldContain("CS1504", customMessage: build.Combined);

        using var switched = new TempDirectory();
        await InitialiseRepositoryAsync(switched.Path);
        var switchedProject = await CreateConsumerAsync(
            switched.Path,
            properties: "<TraconUsageDiagnostics>false</TraconUsageDiagnostics>");
        await WriteUnreadableInstructionsAsync(switched.Path);

        var exit = await BuildAsync(switchedProject);

        exit.ExitCode.ShouldBe(0, exit.Combined);
    }

    /// <summary>
    /// Silencing the three diagnostics that read the instructions files is not a way out: the analyzer still runs for
    /// its other rules, so the compiler still opens the files. Only the switch that silences every rule of the family
    /// keeps the compiler from touching them.
    /// </summary>
    [Fact]
    public async Task Silencing_only_the_three_diagnostics_that_read_the_instructions_is_not_a_way_out()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a symbolic link needs a privilege on Windows.");

        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            properties: "<NoWarn>$(NoWarn);TRC0401;TRC0402;TRC0403</NoWarn>");

        File.CreateSymbolicLink(Path.Combine(directory.Path, "AGENTS.md"), Path.Combine(directory.Path, "missing-target.md"));

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldNotBe(0, build.Combined);
        build.Combined.ShouldContain("CS2001", customMessage: build.Combined);
    }

    /// <summary>
    /// Two projects that reference DIFFERENT Tracon packages - the ordinary
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
            packageId: "Tracon.Core",
            sdk: "Microsoft.NET.Sdk",
            program: "// The HTTP layer is not referenced here.\n");

        var solution = await ProcessRunner.RunAsync("dotnet", "new sln -n Both --format slnx", directory.Path, BuildTimeout);
        solution.ExitCode.ShouldBe(0, solution.Combined);

        var added = await ProcessRunner.RunAsync(
            "dotnet",
            "sln Both.slnx add src/Web/Web.csproj src/Worker/Worker.csproj",
            directory.Path,
            BuildTimeout);
        added.ExitCode.ShouldBe(0, added.Combined);

        var build = await ProcessRunner.RunAsync("dotnet", "build Both.slnx -c Release", directory.Path, BuildTimeout);
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
        webText.ShouldContain("Tracon.AspNetCore");

        workerText.ShouldNotContain(HttpSectionHeading);
        workerText.ShouldNotContain("Tracon.AspNetCore");

        // Neither answer moves once written: no project writes the other's file.
        var stamps = new[] { File.GetLastWriteTimeUtc(webFile), File.GetLastWriteTimeUtc(workerFile) };

        var second = await ProcessRunner.RunAsync("dotnet", "build Both.slnx -c Release -t:Rebuild", directory.Path, BuildTimeout);
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
            .Where(line => line.StartsWith("- Tracon", StringComparison.Ordinal)
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
        string packageId = "Tracon",
        string sdk = "Microsoft.NET.Sdk.Web",
        string? program = null)
    {
        var projectDirectory = Path.Combine(root, subdirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(projectDirectory);
        await TemplateFixture.WriteLocalNuGetConfigAsync(root);

        properties ??= writeAgentsFile
            ? "<TraconWriteAgentsFile>true</TraconWriteAgentsFile>"
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
        using Tracon;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddTracon();

        var app = builder.Build();
        app.MapTracon();
        app.Run();
        """;

    private static async Task InitialiseRepositoryAsync(string root)
    {
        var result = await ProcessRunner.RunAsync("git", "init", root, TimeSpan.FromMinutes(1));

        result.ExitCode.ShouldBe(0, result.Combined);
    }

    private static Task<ProcessResult> BuildAsync(string projectDirectory)
        => ProcessRunner.RunAsync("dotnet", "build -c Release", projectDirectory, BuildTimeout);

    /// <summary>A build the way a strict CI command line runs it: the CLI switch and the MSBuild property together.</summary>
    private static Task<ProcessResult> StrictBuildAsync(string projectDirectory, params string[] extraArguments)
        => ProcessRunner.RunAsync(
            "dotnet",
            string.Join(' ', ["build -c Release -warnaserror -p:MSBuildTreatWarningsAsErrors=true", .. extraArguments]),
            projectDirectory,
            BuildTimeout);

    private static async Task WriteUnreadableInstructionsAsync(string root)
    {
        var path = Path.Combine(root, "AGENTS.md");

        await File.WriteAllTextAsync(path, "# House rules\n", TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }
    }
}
