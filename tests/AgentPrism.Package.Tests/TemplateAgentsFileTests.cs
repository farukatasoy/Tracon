using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// The consumer-facing half of Phase 73: the <c>buildTransitive</c> target that
/// writes <c>AGENTS.md</c>, and the usage analyzer that reaches a consumer
/// through the package.
/// </summary>
/// <remarks>
/// These behaviours cross the package boundary, so a unit test cannot prove
/// them: the target only exists inside the <c>.nupkg</c>, and the analyzer only
/// loads when NuGet propagates <c>analyzers/dotnet/cs</c>. Every test here
/// consumes the packed <c>AgentPrism</c> meta package from the local feed, which
/// also proves the assets flow across the transitive reference.
/// </remarks>
public sealed class TemplateAgentsFileTests(TemplateFixture fixture)
{
    /// <summary>
    /// Mirrors <c>agentMapBudgetBytes</c> in
    /// <c>docs-site/scripts/build-agent-map.mjs</c>, which is the gate that
    /// enforces it. Repeated here so a consumer-visible file is checked where
    /// the consumer sees it.
    /// </summary>
    private const int AgentMapBudgetBytes = 10240;

    private const string MarkerOpening = "<!-- AgentPrism agent map · revision: ";

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
        build.Combined.ShouldContain("APG0401");
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
        var solution = await ProcessRunner.RunAsync("dotnet", "new sln -n Both", directory.Path, BuildTimeout);
        solution.ExitCode.ShouldBe(0, solution.Combined);

        var added = await ProcessRunner.RunAsync(
            "dotnet",
            "sln Both.sln add src/First/First.csproj src/Second/Second.csproj",
            directory.Path,
            BuildTimeout);
        added.ExitCode.ShouldBe(0, added.Combined);

        var build = await ProcessRunner.RunAsync("dotnet", "build Both.sln -c Release", directory.Path, BuildTimeout);

        build.ExitCode.ShouldBe(0, build.Combined);

        var files = Directory.EnumerateFiles(directory.Path, "AGENTS.md", SearchOption.AllDirectories).ToList();
        files.Count.ShouldBe(1, string.Join(", ", files));
        files[0].ShouldBe(Path.Combine(directory.Path, "AGENTS.md"));
    }

    /// <summary>
    /// Every diagnostic a GENERATED map can produce, in one real build. APG0402
    /// is the one that cannot appear here - it reports a file this package did
    /// not write - and <see cref="Instructions_that_never_name_the_local_reference_are_reported"/>
    /// proves it reaches a consumer on its own.
    /// </summary>
    [Fact]
    public async Task Every_usage_diagnostic_reaches_a_real_consumer_project()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        // The stale marker is what makes APG0401 fire; the rest come from the source.
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            StaleMap,
            TestContext.Current.CancellationToken);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            HandWrittenProgram(),
            subdirectory: "src/Consumer");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);

        foreach (var id in DiagnosticIds.Where(id => !string.Equals(id, "APG0402", StringComparison.Ordinal)))
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
        build.Combined.ShouldContain("APG0402", customMessage: build.Combined);

        // Staleness is a different question and belongs to a file this package
        // generated; the consumer's own file cannot be stale against anything.
        build.Combined.ShouldNotContain("APG0401", customMessage: build.Combined);

        // The consumer's file is never touched, only read.
        (await File.ReadAllTextAsync(Path.Combine(directory.Path, "AGENTS.md"), TestContext.Current.CancellationToken))
            .ShouldBe(HouseRules);

        // One line closes it, and the message says which line to add.
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "AGENTS.md"),
            $"{HouseRules}\nExact paths: AgentPrism.LocalReference.md, beside each project.\n",
            TestContext.Current.CancellationToken);

        var second = await ProcessRunner.RunAsync("dotnet", "build -c Release -t:Rebuild", projectDirectory, BuildTimeout);

        second.ExitCode.ShouldBe(0, second.Combined);
        second.Combined.ShouldNotContain("APG0", customMessage: second.Combined);
    }

    /// <summary>
    /// 🚨 A consumer who opted into NOTHING must hear nothing. The additional
    /// files reach the analyzer whatever the properties say, so without the
    /// opt-in gate APG0402 fires here - and the line it asks for would name
    /// <c>AgentPrism.LocalReference.md</c>, which this build never writes.
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
        build.Combined.ShouldNotContain("APG0402", customMessage: build.Combined);

        // The other half of the same fact: there is no file to point at.
        File.Exists(Path.Combine(projectDirectory, "AgentPrism.LocalReference.md"))
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
            extraProperties: "<AgentPrismWriteLocalReference>false</AgentPrismWriteLocalReference>");

        var build = await BuildAsync(projectDirectory);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldNotContain("APG0402", customMessage: build.Combined);
    }

    /// <summary>
    /// Every project in a solution reports it, exactly as APG0401 does: the
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

        var solution = await ProcessRunner.RunAsync("dotnet", "new sln -n Both", directory.Path, BuildTimeout);
        solution.ExitCode.ShouldBe(0, solution.Combined);

        var added = await ProcessRunner.RunAsync(
            "dotnet",
            "sln Both.sln add src/First/First.csproj src/Second/Second.csproj",
            directory.Path,
            BuildTimeout);
        added.ExitCode.ShouldBe(0, added.Combined);

        var build = await ProcessRunner.RunAsync("dotnet", "build Both.sln -c Release", directory.Path, BuildTimeout);

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
            .Where(line => line.Contains("APG0402", StringComparison.Ordinal))
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
    /// design - a generated AGENTS.md can be stale (APG0401), a hand-written one
    /// can be missing the pointer (APG0402), and no single file is both.
    /// </remarks>
    [Fact]
    public async Task One_property_silences_the_whole_usage_family()
    {
        using var directory = new TempDirectory();
        await InitialiseRepositoryAsync(directory.Path);

        var agentsFile = Path.Combine(directory.Path, "AGENTS.md");

        await File.WriteAllTextAsync(agentsFile, StaleMap, TestContext.Current.CancellationToken);

        var projectDirectory = await CreateConsumerAsync(
            directory.Path,
            writeAgentsFile: true,
            HandWrittenProgram(),
            subdirectory: "src/Consumer");

        var stale = await ProcessRunner.RunAsync(
            "dotnet",
            "build -c Release -t:Rebuild -p:AgentPrismUsageDiagnostics=false",
            projectDirectory,
            BuildTimeout);

        stale.ExitCode.ShouldBe(0, stale.Combined);

        // The hand-written file swaps APG0401 for APG0402; everything else in
        // the family comes from the source and reports in both builds.
        await File.WriteAllTextAsync(agentsFile, HouseRules, TestContext.Current.CancellationToken);

        var handWritten = await ProcessRunner.RunAsync(
            "dotnet",
            "build -c Release -t:Rebuild -p:AgentPrismUsageDiagnostics=false",
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
        ["APG0101", "APG0102", "APG0201", "APG0301", "APG0302", "APG0401", "APG0402"];

    /// <summary>A file this package generated, from a revision it no longer ships.</summary>
    private const string StaleMap =
        $"{MarkerOpening}00000000 · generated by docs-site/scripts/build-agent-map.mjs -->\n# AgentPrism\n";

    /// <summary>
    /// A file the consumer wrote. It carries no revision marker, and it never
    /// names the generated reference file - the shape APG0402 exists for.
    /// </summary>
    private const string HouseRules = "# House rules\n\nRun the tests before you commit.\n";

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
        using AgentPrism;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddAgentPrism();

        var app = builder.Build();
        app.MapAgentPrism();
        app.Run();
        """;

    /// <summary>
    /// A consumer that hand-writes what the package already ships: an absent
    /// registration, an unregistered provider, a literal secret, a retry loop,
    /// and an agent wrapper. One build must report all five.
    /// </summary>
    /// <remarks>
    /// The credential-shaped value is analyzer INPUT - APG0201 reports a string
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
            using AgentPrism;
            using Microsoft.Agents.AI;
            using Microsoft.Extensions.AI;

            var builder = WebApplication.CreateBuilder(args);

            var app = builder.Build();
            app.MapAgentPrism();
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
            ? "\n    <AgentPrismWriteAgentsFile>true</AgentPrismWriteAgentsFile>"
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
                <PackageReference Include="AgentPrism" Version="{fixture.Version}" />
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
