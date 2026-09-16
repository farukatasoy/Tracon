using System.Globalization;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The consumer-facing half of Phase 73: the <c>buildTransitive</c> target that
/// writes <c>AGENTS.md</c>, and the usage analyzer that reaches a consumer
/// through the package.
/// </summary>
/// <remarks>
/// These behaviours cross the package boundary, so a unit test cannot prove
/// them: the target only exists inside the <c>.nupkg</c>, and the analyzer only
/// loads when NuGet propagates <c>analyzers/dotnet/cs</c>. Every test here
/// consumes the packed <c>Tracon</c> meta package from the local feed, which
/// also proves the assets flow across the transitive reference.
/// </remarks>
public sealed class TemplateAgentsFileTests(TemplateFixture fixture)
{
    /// <summary>
    /// The budget the agent map is generated under, READ from
    /// <c>docs-site/scripts/build-agent-map.mjs</c> rather than repeated.
    /// </summary>
    /// <remarks>
    /// 🚨 It used to be a mirrored <c>const</c>, with a comment saying so.
    /// Phase 153 raised the generator's ceiling and this copy stayed behind:
    /// the generator, its own gate and the site all agreed, and only this test
    /// failed — the repeated-expression class the repository has paid for
    /// before. The value now has one home; a second edit cannot desynchronise
    /// what is no longer duplicated.
    /// </remarks>
    private static int AgentMapBudgetBytes
    {
        get
        {
            var generator = Path.Combine(RepoPaths.Root, "docs-site", "scripts", "build-agent-map.mjs");
            var match = Regex.Match(
                File.ReadAllText(generator),
                @"agentMapBudgetBytes\s*=\s*(?<bytes>\d+)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));

            match.Success.ShouldBeTrue($"'{generator}' no longer declares agentMapBudgetBytes.");

            return int.Parse(match.Groups["bytes"].Value, CultureInfo.InvariantCulture);
        }
    }

    private const string MarkerOpening = "<!-- Tracon agent map · revision: ";

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Property_unset_writes_no_file()
    {
        using var directory = new TempDirectory();
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: false, CleanProgram);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        File.Exists(Path.Combine(directory.Path, "AGENTS.md")).ShouldBeFalse(build.Combined);
        File.Exists(Path.Combine(projectDirectory, "AGENTS.md")).ShouldBeFalse(build.Combined);
    }

    [Fact]
    public async Task Property_set_writes_the_map_at_the_repository_root_and_never_overwrites_it()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "src/Consumer");

        var build = await BuildAsync(projectDirectory);
        build.ExitCode.ShouldBe(0, build.Combined);

        var agentsFile = Path.Combine(directory.Path, "AGENTS.md");
        File.Exists(agentsFile).ShouldBeTrue(build.Combined);

        var written = await File.ReadAllTextAsync(agentsFile, TestContext.Current.CancellationToken);
        written.ShouldStartWith(MarkerOpening);
        new FileInfo(agentsFile).Length.ShouldBeLessThanOrEqualTo(AgentMapBudgetBytes);

        // The consumer owns the file from here on: an edit must survive a build.
        var edited = $"{written}\n## House rules\n\nRun the tests before you commit.\n";
        await File.WriteAllTextAsync(agentsFile, edited, TestContext.Current.CancellationToken);

        var second = await BuildAsync(projectDirectory);
        second.ExitCode.ShouldBe(0, second.Combined);
        (await File.ReadAllTextAsync(agentsFile, TestContext.Current.CancellationToken)).ShouldBe(edited);
    }

    [Fact]
    public async Task A_file_from_an_older_map_is_reported_as_stale()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "src/Consumer");

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            StaleMap,
            TestContext.Current.CancellationToken);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("TRC0401");
    }

    [Fact]
    public async Task Without_a_repository_the_file_lands_next_to_the_project()
    {
        using var directory = new TempDirectory();
        var projectDirectory = await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "Consumer");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        File.Exists(Path.Combine(projectDirectory, "AGENTS.md")).ShouldBeTrue(build.Combined);
        build.Combined.ShouldContain("no repository root was found");
    }

    [Fact]
    public async Task Two_projects_in_one_build_leave_a_single_file()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "src/First", projectName: "First");
        await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "src/Second", projectName: "Second");

        // One solution, one build: two projects that both opt in race for the
        // same destination, and the target must stay correct under that race.
        var solution = await ProcessRunner.RunAsync("dotnet", "new sln -n Both --format slnx", directory.Path, BuildTimeout);
        solution.ExitCode.ShouldBe(0, solution.Combined);

        var added = await ProcessRunner.RunAsync(
            "dotnet",
            "sln Both.slnx add src/First/First.csproj src/Second/Second.csproj",
            directory.Path,
            BuildTimeout);
        added.ExitCode.ShouldBe(0, added.Combined);

        var build = await ProcessRunner.RunAsync("dotnet", "build Both.slnx -c Release", directory.Path, BuildTimeout);

        build.ExitCode.ShouldBe(0, build.Combined);

        var files = Directory.EnumerateFiles(directory.Path, "AGENTS.md", SearchOption.AllDirectories).ToList();
        files.Count.ShouldBe(1, string.Join(", ", files));
        files[0].ShouldBe(Path.Combine(directory.Path, "AGENTS.md"));
    }

    /// <summary>
    /// Every diagnostic a GENERATED map can produce, in one real build. TRC0402
    /// is the one that cannot appear here - it reports a file this package did
    /// not write - and <see cref="Instructions_that_never_name_the_local_reference_are_reported"/>
    /// proves it reaches a consumer on its own.
    /// </summary>
    [Fact]
    public async Task Every_usage_diagnostic_reaches_a_real_consumer_project()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        // The stale marker is what makes TRC0401 fire; the rest come from the source.
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            StaleMap,
            TestContext.Current.CancellationToken);

        // TRC0403 needs its own stale file: it reads the gate skill, not AGENTS.md.
        await WriteGateSkillAsync(directory.Path, StaleGateSkill);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            HandWrittenProgram(),
            subdirectory: "src/Consumer");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);

        foreach (var id in DiagnosticIds.Where(id => !string.Equals(id, "TRC0402", StringComparison.Ordinal)))
        {
            build.Combined.ShouldContain(id, customMessage: build.Combined);
        }
    }

    /// <summary>
    /// A repository with its OWN instructions - the ordinary case, and the one
    /// the map never reached before: nothing generated named the reference file,
    /// so the map on disk was unfindable and no diagnostic said so.
    /// </summary>
    [Fact]
    public async Task Instructions_that_never_name_the_local_reference_are_reported()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            HouseRules,
            TestContext.Current.CancellationToken);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            CleanProgram,
            subdirectory: "src/Consumer");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("TRC0402", customMessage: build.Combined);

        // Staleness is a different question and belongs to a file this package
        // generated; the consumer's own file cannot be stale against anything.
        build.Combined.ShouldNotContain("TRC0401", customMessage: build.Combined);

        // The consumer's file is never touched, only read.
        (await File.ReadAllTextAsync(Path.Combine(directory.Path, "AGENTS.md"), TestContext.Current.CancellationToken))
            .ShouldBe(HouseRules);

        // One line closes it, and the message says which line to add.
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            $"{HouseRules}\nExact paths: Tracon.LocalReference.md, beside each project.\n",
            TestContext.Current.CancellationToken);

        var second = await ProcessRunner.RunAsync("dotnet", "build -c Release -t:Rebuild", projectDirectory, BuildTimeout);

        second.ExitCode.ShouldBe(0, second.Combined);
        second.Combined.ShouldNotContain("APG0", customMessage: second.Combined);
    }

    /// <summary>
    /// 🚨 A consumer who opted into NOTHING must hear nothing. The additional
    /// files reach the analyzer whatever the properties say, so without the
    /// opt-in gate TRC0402 fires here - and the line it asks for would name
    /// <c>Tracon.LocalReference.md</c>, which this build never writes.
    /// </summary>
    /// <remarks>
    /// Only a real build can prove it: the property travels to the analyzer
    /// through the generated MSBuild editorconfig, which exists on neither side
    /// of a unit test.
    /// </remarks>
    [Fact]
    public async Task A_consumer_who_opted_into_nothing_is_never_told_to_name_a_file_that_is_not_written()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            HouseRules,
            TestContext.Current.CancellationToken);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: false,
            CleanProgram,
            subdirectory: "src/Consumer");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldNotContain("TRC0402", customMessage: build.Combined);

        // The other half of the same fact: there is no file to point at.
        File.Exists(Path.Combine(projectDirectory, "Tracon.LocalReference.md"))
            .ShouldBeFalse(build.Combined);
    }

    /// <summary>
    /// Turning the pointer file off on its own also silences the request for a
    /// pointer to it. The two properties are separable, and the diagnostic
    /// follows the one that decides whether the file exists.
    /// </summary>
    [Fact]
    public async Task Turning_the_local_reference_off_silences_the_request_for_a_pointer()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            HouseRules,
            TestContext.Current.CancellationToken);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            CleanProgram,
            subdirectory: "src/Consumer",
            extraProperties: "<TraconWriteLocalReference>false</TraconWriteLocalReference>");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldNotContain("TRC0402", customMessage: build.Combined);
    }

    /// <summary>
    /// Every project in a solution reports it, exactly as TRC0401 does: the
    /// consumer silences both with one property, and consistency is worth more
    /// than the saved line.
    /// </summary>
    [Fact]
    public async Task Every_project_of_a_solution_reports_the_missing_pointer()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            HouseRules,
            TestContext.Current.CancellationToken);

        await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "src/First", projectName: "First");
        await CreateConsumerAsync(directory.Path, writeAgentsFile: true, CleanProgram, subdirectory: "src/Second", projectName: "Second");

        var solution = await ProcessRunner.RunAsync("dotnet", "new sln -n Both --format slnx", directory.Path, BuildTimeout);
        solution.ExitCode.ShouldBe(0, solution.Combined);

        var added = await ProcessRunner.RunAsync(
            "dotnet",
            "sln Both.slnx add src/First/First.csproj src/Second/Second.csproj",
            directory.Path,
            BuildTimeout);
        added.ExitCode.ShouldBe(0, added.Combined);

        var build = await ProcessRunner.RunAsync("dotnet", "build Both.slnx -c Release", directory.Path, BuildTimeout);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("First.csproj", customMessage: build.Combined);
        build.Combined.ShouldContain("Second.csproj", customMessage: build.Combined);

        // Distinct PROJECTS, not lines: a single project already contributes two
        // lines to the output (the warning and the summary), so counting lines
        // would stay green while one of the two projects said nothing.
        // Membership, not sequence: MSBuild builds the two projects in whichever
        // order it likes, and ShouldBe on a set still compares in order.
        var reporting = build.Combined
            .Split('\n')
            .Where(line => line.Contains("TRC0402", StringComparison.Ordinal))
            .SelectMany(line => new[] { "First.csproj", "Second.csproj" }.Where(project => line.Contains(project, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        reporting.ShouldBe(["First.csproj", "Second.csproj"], build.Combined);
    }

    /// <summary>
    /// The property has to reach the WHOLE family, and the family grows. A code
    /// left out of the NoWarn list is silent debt: the consumer sets the
    /// property, believes the family is off, and keeps getting one warning.
    /// </summary>
    /// <remarks>
    /// Two builds because two of the diagnostics are mutually exclusive by
    /// design - a generated AGENTS.md can be stale (TRC0401), a hand-written one
    /// can be missing the pointer (TRC0402), and no single file is both.
    /// </remarks>
    [Fact]
    public async Task One_property_silences_the_whole_usage_family()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        var agentsFile = Path.Combine(directory.Path, "AGENTS.md");

        await File.WriteAllTextAsync(agentsFile, StaleMap, TestContext.Current.CancellationToken);
        await WriteGateSkillAsync(directory.Path, StaleGateSkill);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            HandWrittenProgram(),
            subdirectory: "src/Consumer");

        var stale = await ProcessRunner.RunAsync(
            "dotnet",
            "build -c Release -t:Rebuild -p:TraconUsageDiagnostics=false",
            projectDirectory,
            BuildTimeout);

        stale.ExitCode.ShouldBe(0, stale.Combined);

        // The hand-written file swaps TRC0401 for TRC0402; everything else in
        // the family comes from the source and reports in both builds.
        await File.WriteAllTextAsync(agentsFile, HouseRules, TestContext.Current.CancellationToken);

        var handWritten = await ProcessRunner.RunAsync(
            "dotnet",
            "build -c Release -t:Rebuild -p:TraconUsageDiagnostics=false",
            projectDirectory,
            BuildTimeout);

        handWritten.ExitCode.ShouldBe(0, handWritten.Combined);

        foreach (var id in DiagnosticIds)
        {
            stale.Combined.ShouldNotContain(id, customMessage: stale.Combined);
            handWritten.Combined.ShouldNotContain(id, customMessage: handWritten.Combined);
        }
    }

    private static readonly string[] DiagnosticIds =
        ["TRC0101", "TRC0102", "TRC0201", "TRC0301", "TRC0302", "TRC0401", "TRC0402", "TRC0403"];

    /// <summary>A file this package generated, from a revision it no longer ships.</summary>
    private const string StaleMap =
        $"{MarkerOpening}00000000 · generated by docs-site/scripts/build-agent-map.mjs -->\n# Tracon\n";

    /// <summary>
    /// A file the consumer wrote. It carries no revision marker, and it never
    /// names the generated reference file - the shape TRC0402 exists for.
    /// </summary>
    private const string HouseRules = "# House rules\n\nRun the tests before you commit.\n";

    /// <summary>Where <c>tracon agent-skill</c> writes, relative to the repository root.</summary>
    private static readonly string GateSkillRelativePath =
        Path.Combine(".claude", "skills", "tracon", "SKILL.md");

    private const string GateSkillMarkerOpening = "<!-- Tracon gate skill \u00b7 revision: ";

    /// <summary>A gate skill the tool wrote, from a revision this package no longer ships.</summary>
    private static string StaleGateSkill { get; } = GateSkill("00000000");

    /// <summary>A skill of the same file name that the tool did not write.</summary>
    private const string HandWrittenSkill =
        "---\nname: deploy\ndescription: How this team deploys.\n---\n\n# Deploy\n\nRun the script.\n";

    private static string GateSkill(string revision) =>
        "---\nname: tracon\ndescription: Read before writing Tracon code.\n---\n\n"
        + $"{GateSkillMarkerOpening}{revision} \u00b7 written by `tracon agent-skill` -->\n\n"
        + "# Tracon gate\n\nRead the capability map first.\n";

    /// <summary>The revision the packed capability map actually carries.</summary>
    private static string CurrentRevision()
    {
        var map = Path.Combine(RepoPaths.Root, "src", "Tracon.Core", "buildTransitive", "Tracon.AgentMap.md");
        var first = File.ReadLines(map).First();
        var start = first.IndexOf(MarkerOpening, StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, $"'{map}' carries no revision marker.");
        start += MarkerOpening.Length;

        return first[start..first.IndexOf(' ', start)];
    }

    private static async Task WriteGateSkillAsync(string root, string content)
    {
        var path = Path.Combine(root, GateSkillRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The gate skill half of TRC0403, across the package boundary.
    /// </summary>
    /// <remarks>
    /// A unit test hands the analyzer its additional files directly and so
    /// proves nothing about the wiring: whether the build target finds the file
    /// at the repository root, and whether it reaches the analyzer at all, is
    /// decided inside the <c>.nupkg</c>. Measured while writing this - the path
    /// is built by an MSBuild function over four segments, and an empty result
    /// would have left the whole diagnostic silently dead with every unit test
    /// still green.
    /// </remarks>
    [Fact]
    public async Task A_gate_skill_from_an_older_map_is_reported_as_stale()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(
            directory.Path, writeAgentsFile: false, CleanProgram, subdirectory: "src/Consumer");

        await WriteGateSkillAsync(directory.Path, StaleGateSkill);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("TRC0403", customMessage: build.Combined);
    }

    /// <summary>
    /// The same file, stamped with the revision this package ships, says
    /// nothing. Without this the test above would also pass on a diagnostic
    /// that fires unconditionally.
    /// </summary>
    [Fact]
    public async Task A_gate_skill_from_the_installed_map_is_silent()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(
            directory.Path, writeAgentsFile: false, CleanProgram, subdirectory: "src/Consumer");

        await WriteGateSkillAsync(directory.Path, GateSkill(CurrentRevision()));

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldNotContain("TRC0403", customMessage: build.Combined);
    }

    /// <summary>
    /// <c>SKILL.md</c> is a name the harness owns, not Tracon. A skill the
    /// consumer wrote lands among the same additional files, and reporting it
    /// would be telling them their own file is out of date.
    /// </summary>
    [Fact]
    public async Task A_skill_this_package_did_not_write_is_never_reported()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);
        var projectDirectory = await CreateConsumerAsync(
            directory.Path, writeAgentsFile: false, CleanProgram, subdirectory: "src/Consumer");

        await WriteGateSkillAsync(directory.Path, HandWrittenSkill);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldNotContain("TRC0403", customMessage: build.Combined);
    }

    [Fact]
    public async Task A_generated_project_gets_the_map_without_being_asked()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        var projectDirectory = Path.Combine(directory.Path, "src", "Generated.Sample");
        var created = await fixture.NewAsync("Generated.Sample", projectDirectory, "--persistence memory --provider openai --ui false");
        created.ExitCode.ShouldBe(0, created.Combined);

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        File.Exists(Path.Combine(directory.Path, "AGENTS.md")).ShouldBeTrue(build.Combined);
    }

    private const string CleanProgram = """
        using Tracon;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddTracon();

        var app = builder.Build();
        app.MapTracon();
        app.Run();
        """;

    /// <summary>
    /// A consumer that hand-writes what the package already ships: an absent
    /// registration, an unregistered provider, a literal secret, a retry loop,
    /// and an agent wrapper. One build must report all five.
    /// </summary>
    /// <remarks>
    /// The credential-shaped value is analyzer INPUT - TRC0201 reports a string
    /// LITERAL, so the shape has to be real. It is assembled here rather than
    /// written out to keep the generated file the only place it exists.
    /// </remarks>
    private static string HandWrittenProgram()
    {
        var token = $"ghp_{new string('a', 36)}";

        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Tracon;
            using Microsoft.Agents.AI;
            using Microsoft.Extensions.AI;

            var builder = WebApplication.CreateBuilder(args);

            var app = builder.Build();
            app.MapTracon();
            app.Run();

            public static class Definitions
            {
                public static AgentDefinition Support() => new()
                {
                    Name = "support",
                    Instructions = "Answer support questions.",
                    Model = new ModelBinding { Provider = "anthropic", Model = "claude-sonnet-4-5" },
                };

                public static McpServerDefinition Github() => new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "default",
                    Name = "github",
                    Endpoint = new Uri("https://example.test/mcp"),
                    AuthorizationConfigurationKey = "{{token}}",
                };
            }

            public sealed class LoggingAgent(AIAgent inner) : DelegatingAIAgent(inner);

            public sealed class RetryingChatClient(IChatClient inner) : DelegatingChatClient(inner)
            {
                public override async Task<ChatResponse> GetResponseAsync(
                    IEnumerable<ChatMessage> messages,
                    ChatOptions? options = null,
                    CancellationToken cancellationToken = default)
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            return await base.GetResponseAsync(messages, options, cancellationToken);
                        }
                        catch (InvalidOperationException)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }
                    }

                    throw new InvalidOperationException("exhausted");
                }
            }
            """;
    }

    /// <summary>
    /// Writes a consumer project that references the packed meta package, and
    /// returns its directory. The meta package is used deliberately: a
    /// build/ asset would not survive that transitive hop, which is why the
    /// target ships under buildTransitive/.
    /// </summary>
    private async Task<string> CreateConsumerAsync(
        string root,
        bool writeAgentsFile,
        string program,
        string subdirectory = "Consumer",
        string projectName = "Consumer",
        string? extraProperties = null)
    {
        var projectDirectory = Path.Combine(root, subdirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(projectDirectory);
        await TemplateFixture.WriteLocalNuGetConfigAsync(root);

        var property = writeAgentsFile
            ? "\n    <TraconWriteAgentsFile>true</TraconWriteAgentsFile>"
            : string.Empty;

        if (extraProperties is not null)
        {
            property += $"\n    {extraProperties}";
        }

        var project = $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RootNamespace>{projectName}</RootNamespace>{property}
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Tracon" Version="{fixture.Version}" />
              </ItemGroup>

            </Project>
            """;

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, $"{projectName}.csproj"),
            project,
            TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            program,
            TestContext.Current.CancellationToken);

        return projectDirectory;
    }

    private static async Task InitialiseRepositoryAsync(string root)
    {
        var result = await ProcessRunner.RunAsync("git", "init", root, TimeSpan.FromMinutes(1));

        result.ExitCode.ShouldBe(0, result.Combined);
    }

    private static Task<ProcessResult> BuildAsync(string projectDirectory)
        => ProcessRunner.RunAsync("dotnet", "build -c Release", projectDirectory, BuildTimeout);
}
