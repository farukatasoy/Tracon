using System.Globalization;
using System.Text;

namespace Tracon.Package.Tests.Infrastructure;

/// <summary>The kind of project a consumer builds against the packed packages.</summary>
internal enum ConsumerShape
{
    /// <summary>An ASP.NET Core host on the meta package and a store: the shape the guides teach.</summary>
    Web,

    /// <summary>A class library that hands its tools to a host, with the trim and AOT analyzers on.</summary>
    Library,

    /// <summary>A console host on the packages that promise Native AOT, with the trim and AOT analyzers on.</summary>
    TrimmableConsole,

    /// <summary>A test project on the shipped contract suite, on the oldest xunit line the package supports.</summary>
    ContractTests,
}

/// <summary>One consumer project: a shape and the settings a strict consumer turns on.</summary>
/// <param name="Shape">The kind of project.</param>
/// <param name="CentralPackages">Whether versions live in <c>Directory.Packages.props</c>.</param>
/// <param name="Nullable">Whether nullable reference types are enabled.</param>
/// <param name="ImplicitUsings">Whether the SDK adds the implicit using directives.</param>
/// <param name="Documentation">
/// Whether the compiler writes an XML documentation file, which makes a public member without a summary a warning.
/// The compiler does not apply that to generated code (measured: a generator that emitted a public undocumented type
/// stayed silent), so this switch guards the code written in the project, not the generator's output.
/// </param>
/// <param name="OldXunit">For <see cref="ConsumerShape.ContractTests"/>: the floor of the xunit.v3 range the contract package declares, instead of the current line.</param>
/// <param name="LangVersion">
/// The <c>LangVersion</c> the project pins, or <see langword="null"/> for the SDK default. Only <see cref="ConsumerShape.Web"/>
/// supports it, and the implicit usings must be off: the SDK writes them as <c>global using</c> directives, which are C# 10.
/// </param>
internal sealed record StrictCell(
    ConsumerShape Shape,
    bool CentralPackages,
    bool Nullable,
    bool ImplicitUsings,
    bool Documentation,
    bool OldXunit = false,
    string? LangVersion = null)
{
    /// <summary>A name for a test report.</summary>
    public string Name => string.Create(
        CultureInfo.InvariantCulture,
        $"{Shape}-cpm{Flag(CentralPackages)}-nullable{Flag(Nullable)}-usings{Flag(ImplicitUsings)}-docs{Flag(Documentation)}{(OldXunit ? "-xunit3" : string.Empty)}{(LangVersion is null ? string.Empty : "-lang" + LangVersion)}");

    private static int Flag(bool value) => value ? 1 : 0;
}

/// <summary>
/// Writes a consumer project that turns every strictness switch on: warnings as errors for the compiler AND for MSBuild,
/// every analyzer category, and (optionally) central package management, documentation files and the trim and AOT
/// analyzers. The code in it is clean by construction, so any warning of the build comes from a Tracon package.
/// </summary>
/// <remarks>
/// The source files are the ones the guides and package READMEs show, written so that none of the strict analyzers has
/// anything to say about them: types are <c>internal</c> in an executable and public in a library, every public member
/// carries a summary, nothing is awaited without <c>ConfigureAwait</c>, and no reference type is annotated with <c>?</c>
/// (that would be a warning when nullable is off). The NuGet feed is the packed local one; the version never comes from
/// nuget.org.
/// </remarks>
internal static class StrictConsumerProject
{
    private const string OldXunitVersion = "3.2.2";

    private const string CurrentXunitVersion = "4.0.2";

    private const string HostingVersion = "10.0.14";

    private static readonly Dictionary<ConsumerShape, (string Sdk, string? OutputType, string[] Extra)> Shapes = new()
    {
        [ConsumerShape.Web] = ("Microsoft.NET.Sdk.Web", null, []),
        [ConsumerShape.Library] = ("Microsoft.NET.Sdk", null, ["<IsAotCompatible>true</IsAotCompatible>", "<IsTrimmable>true</IsTrimmable>"]),
        [ConsumerShape.TrimmableConsole] = (
            "Microsoft.NET.Sdk",
            "Exe",
            [
                "<EnableTrimAnalyzer>true</EnableTrimAnalyzer>",
                "<EnableAotAnalyzer>true</EnableAotAnalyzer>",
                "<EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>",
                "<IsTrimmable>true</IsTrimmable>",
                "<InvariantGlobalization>true</InvariantGlobalization>",
            ]),
        // CA1515 asks an executable to keep its types internal, and a test class has to be public: the analyzers xunit
        // ships suppress it on the current line, not on the floor.
        [ConsumerShape.ContractTests] = ("Microsoft.NET.Sdk", "Exe", ["<IsTestProject>true</IsTestProject>", "<NoWarn>$(NoWarn);CA1707;CA1515</NoWarn>"]),
    };

    /// <summary>The packages a shape references, with the version of each.</summary>
    /// <param name="version">The packed version of every Tracon package.</param>
    /// <param name="cell">The cell.</param>
    /// <returns>The package ids and versions.</returns>
    public static IReadOnlyList<(string Id, string Version)> Packages(string version, StrictCell cell) => cell.Shape switch
    {
        ConsumerShape.Web => [("Tracon", version), ("Tracon.PostgreSql", version)],
        ConsumerShape.Library => [("Tracon.Core", version)],
        ConsumerShape.TrimmableConsole =>
        [
            ("Tracon.Core", version),
            ("Tracon.OpenAI", version),
            ("Microsoft.Extensions.Hosting", HostingVersion),
        ],
        ConsumerShape.ContractTests =>
        [
            ("Tracon", version),
            ("Tracon.Testing", version),
            ("Tracon.Testing.Contracts.Xunit", version),
            ("xunit.v3", cell.OldXunit ? OldXunitVersion : CurrentXunitVersion),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(cell)),
    };

    /// <summary>Writes the project into <paramref name="directory"/>.</summary>
    /// <param name="version">The packed version of every Tracon package.</param>
    /// <param name="directory">An existing, empty directory.</param>
    /// <param name="cell">The cell.</param>
    /// <returns>A task that completes when the files are written.</returns>
    public static async Task WriteAsync(string version, string directory, StrictCell cell)
    {
        await TemplateFixture.WriteLocalNuGetConfigAsync(directory);

        var packages = Packages(version, cell);
        var (sdk, outputType, extra) = Shapes[cell.Shape];

        await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.csproj"), Project(cell, packages, sdk, outputType, extra));

        if (cell.CentralPackages)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Packages.props"), CentralPackages(packages));
        }

        foreach (var (name, content) in Sources(cell))
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), content);
        }
    }

    private static string Project(StrictCell cell, IReadOnlyList<(string Id, string Version)> packages, string sdk, string? outputType, string[] extra)
    {
        var properties = new List<string>
        {
            "<TargetFramework>net10.0</TargetFramework>",
            "<Nullable>" + (cell.Nullable ? "enable" : "disable") + "</Nullable>",
            "<ImplicitUsings>" + (cell.ImplicitUsings ? "enable" : "disable") + "</ImplicitUsings>",
            "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>",
            "<MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>",
            "<EnableNETAnalyzers>true</EnableNETAnalyzers>",
            "<AnalysisLevel>latest-all</AnalysisLevel>",
            "<AnalysisMode>All</AnalysisMode>",
            "<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>",
        };

        if (outputType is not null)
        {
            properties.Insert(0, "<OutputType>" + outputType + "</OutputType>");
        }

        if (cell.Documentation)
        {
            properties.Add("<GenerateDocumentationFile>true</GenerateDocumentationFile>");
        }

        if (cell.LangVersion is not null)
        {
            properties.Add("<LangVersion>" + cell.LangVersion + "</LangVersion>");
        }

        properties.AddRange(extra);

        var references = new StringBuilder();

        foreach (var (id, packageVersion) in packages)
        {
            references.Append("    <PackageReference Include=\"").Append(id).Append('"');

            if (!cell.CentralPackages)
            {
                references.Append(" Version=\"").Append(packageVersion).Append('"');
            }

            references.AppendLine(" />");
        }

        return $"""
            <Project Sdk="{sdk}">

              <PropertyGroup>
                {string.Join(Environment.NewLine + "    ", properties)}
              </PropertyGroup>

              <ItemGroup>
            {references}  </ItemGroup>

            </Project>
            """;
    }

    private static string CentralPackages(IReadOnlyList<(string Id, string Version)> packages)
    {
        var rows = string.Join(
            Environment.NewLine,
            packages.Select(static package => $"    <PackageVersion Include=\"{package.Id}\" Version=\"{package.Version}\" />"));

        return $"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
            {rows}
              </ItemGroup>
            </Project>
            """;
    }

    private static IEnumerable<(string Name, string Content)> Sources(StrictCell cell)
    {
        var shape = cell.Shape;

        if (cell.LangVersion is not null)
        {
            if (shape != ConsumerShape.Web)
            {
                throw new ArgumentOutOfRangeException(nameof(cell), "A pinned language version is only written for the web shape.");
            }

            // The same program the guides show, in the syntax of the oldest language version the packages accept: no
            // file-scoped namespace (C# 10), no primary constructor and no collection expression (C# 12).
            yield return ("OrderTools.cs", InBlockNamespace(ToolsInternal));
            yield return ("SupportRunner.cs", SupportRunnerWithConstructor);
            yield return ("Program.cs", WebProgram.Replace("[\"get_order_status\"]", "new System.Collections.Generic.List<string> { \"get_order_status\" }", StringComparison.Ordinal));
            yield break;
        }

        switch (shape)
        {
            case ConsumerShape.Web:
                yield return ("OrderTools.cs", ToolsInternal);
                yield return ("SupportRunner.cs", SupportRunner);
                yield return ("Program.cs", WebProgram);
                break;
            case ConsumerShape.Library:
                yield return ("OrderTools.cs", ToolsInternal.Replace("internal static class", "public static class", StringComparison.Ordinal));
                yield return ("ContosoToolsExtensions.cs", LibraryExtension);
                yield return ("ImageUsage.cs", UsageRecord);
                break;
            case ConsumerShape.TrimmableConsole:
                yield return ("OrderTools.cs", ToolsInternal);
                yield return ("Program.cs", ConsoleProgram);
                break;
            case ConsumerShape.ContractTests:
                yield return ("SubmitOrderToolContract.cs", ToolContract);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cell));
        }
    }

    private const string ToolsInternal = """
        using System.ComponentModel;
        using Tracon;

        namespace Consumer;

        /// <summary>Order tools.</summary>
        internal static class OrderTools
        {
            /// <summary>Returns the shipping status of an order.</summary>
            /// <param name="orderId">The order number.</param>
            /// <returns>The status text.</returns>
            [TraconTool("get_order_status", "Returns the shipping status of an order.")]
            public static string GetOrderStatus([Description("The order number.")] string orderId)
                => string.Concat("Order ", orderId, " has shipped.");
        }
        """;

    /// <summary>Rewrites <c>namespace Consumer;</c> as the block form, indenting what follows it.</summary>
    private static string InBlockNamespace(string source)
    {
        const string FileScoped = "namespace Consumer;\n";

        var at = source.IndexOf(FileScoped, StringComparison.Ordinal);
        var body = source[(at + FileScoped.Length)..].TrimEnd('\n');
        var indented = string.Join('\n', body.Split('\n').Select(static line => line.Length == 0 ? line : "    " + line));

        return string.Concat(source.AsSpan(0, at), "namespace Consumer\n{", indented.Length == 0 ? string.Empty : "\n" + indented, "\n}\n");
    }

    private const string SupportRunnerWithConstructor = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Agents.AI;
        using Tracon;

        namespace Consumer
        {
            /// <summary>Runs the support agent in a durable session.</summary>
            internal sealed class SupportRunner
            {
                private readonly IAgentCatalog _catalog;
                private readonly AgentSessionManager _sessions;

                /// <summary>Creates the runner.</summary>
                /// <param name="catalog">The agent catalog.</param>
                /// <param name="sessions">The session manager.</param>
                public SupportRunner(IAgentCatalog catalog, AgentSessionManager sessions)
                {
                    _catalog = catalog;
                    _sessions = sessions;
                }

                /// <summary>Asks one question.</summary>
                /// <param name="customerId">The session id.</param>
                /// <param name="question">The question.</param>
                /// <param name="cancellationToken">The token.</param>
                /// <returns>The answer text.</returns>
                public async Task<string> AskAsync(string customerId, string question, CancellationToken cancellationToken)
                {
                    var agent = await _catalog.ResolveAsync("support", culture: null, cancellationToken).ConfigureAwait(false);

                    if (agent is null)
                    {
                        return string.Empty;
                    }

                    var session = await _sessions.GetOrCreateSessionAsync(agent, customerId, cancellationToken).ConfigureAwait(false);
                    var response = await agent.RunAsync(question, session, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await _sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);

                    return response.Text;
                }
            }
        }
        """;

    private const string SupportRunner = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Agents.AI;
        using Tracon;

        namespace Consumer;

        /// <summary>Runs the support agent in a durable session.</summary>
        internal sealed class SupportRunner(IAgentCatalog catalog, AgentSessionManager sessions)
        {
            /// <summary>Asks one question.</summary>
            /// <param name="customerId">The session id.</param>
            /// <param name="question">The question.</param>
            /// <param name="cancellationToken">The token.</param>
            /// <returns>The answer text.</returns>
            public async Task<string> AskAsync(string customerId, string question, CancellationToken cancellationToken)
            {
                var agent = await catalog.ResolveAsync("support", culture: null, cancellationToken).ConfigureAwait(false);

                if (agent is null)
                {
                    return string.Empty;
                }

                var session = await sessions.GetOrCreateSessionAsync(agent, customerId, cancellationToken).ConfigureAwait(false);
                var response = await agent.RunAsync(question, session, cancellationToken: cancellationToken).ConfigureAwait(false);
                await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);

                return response.Text;
            }
        }
        """;

    private const string WebProgram = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Consumer;
        using Tracon;

        var builder = WebApplication.CreateBuilder(args);

        var tracon = builder.AddTracon().AddGeneratedTools();

        tracon.UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName));
        tracon.UsePostgreSql(builder.Configuration.GetSection(TraconPostgreSqlOptions.SectionName));
        tracon.UseMcp();
        tracon.UseUI();

        tracon.AddAgent(new AgentDefinition
        {
            Name = "support",
            DisplayName = "Support Assistant",
            Description = "Answers order and shipping questions.",
            Instructions = "You are a support assistant. Answer briefly and clearly.",
            Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-5.4-mini" },
            ToolNames = ["get_order_status"],
        });

        builder.Services.AddScoped(provider => new SupportRunner(
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentSessionManager>()));

        var app = builder.Build();

        app.MapTracon("/tracon");

        app.Run();
        """;

    private const string ConsoleProgram = """
        using System;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Tracon;

        var builder = Host.CreateApplicationBuilder(args);

        builder.AddTracon()
            .AddGeneratedTools()
            .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                Instructions = "You are a support assistant.",
                Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-5.4-mini" },
                ToolNames = ["get_order_status"],
            });

        using var host = builder.Build();

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        return catalog is null ? 1 : 0;
        """;

    private const string LibraryExtension = """
        using Tracon;

        namespace Consumer;

        /// <summary>Hands the library's tools to a host.</summary>
        public static class ContosoToolsExtensions
        {
            /// <summary>Registers the tools generated in this library.</summary>
            /// <param name="builder">The builder.</param>
            /// <returns>The same builder.</returns>
            public static ITraconBuilder AddContosoTools(this ITraconBuilder builder)
                => builder.AddGeneratedTools();
        }
        """;

    /// <summary>
    /// A tool that reports which provider did its work writes a provider name that no agent binds. The library never
    /// registers a provider, so a rule that read this as a model binding would fail the build.
    /// </summary>
    private const string UsageRecord = """
        using Tracon;

        namespace Consumer;

        internal static class ImageUsage
        {
            public static ToolCallUsage Report() => new()
            {
                Unit = ToolUsageUnits.Images,
                Quantity = 1m,
                Provider = "openai",
                Model = "gpt-image-1",
            };
        }
        """;

    private const string ToolContract = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.AI;
        using Tracon;
        using Tracon.Testing.Contracts;
        using Tracon.Testing.Contracts.Tools;

        namespace Consumer;

        /// <summary>Runs the published custom-tool contract against a consumer tool.</summary>
        public sealed class SubmitOrderToolContract : RepeatableToolContract
        {
            /// <inheritdoc />
            protected override string ExpectedResultText => "ready";

            /// <inheritdoc />
            protected override ValueTask<TraconToolRegistration> CreateRegistrationAsync()
                => new(new TraconToolRegistration(
                    AIFunctionFactory.Create((Func<string>)(() => "ready"), "submit_order", "Submits an order to fulfillment."))
                {
                    RequiresApproval = true,
                    Effect = ToolEffect.External,
                    RequiredPermission = "orders.submit",
                    Timeout = TimeSpan.FromSeconds(30),
                    SafeToRepeat = true,
                    MaxOutputBytes = 768,
                });
        }
        """;
}
