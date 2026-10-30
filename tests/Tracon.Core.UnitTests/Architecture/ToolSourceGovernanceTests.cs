using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every place in <c>src/</c> that gives a model request a tool is a known source, and each source is either run
/// through the tool chain or named as one that stays outside it, with the decision that says so.
/// </summary>
/// <remarks>
/// <para>
/// The tool chain (<c>ToolWrapperChain</c>) holds the rules every tool call follows: the authorization handler, the
/// argument validator, the budget gate, the timeout. A source that does not pass through it is not covered by a
/// layer added to the chain later. Two of the tools of this kind (the knowledge search and the conversation memory
/// search) were added without the chain and nobody noticed, because nothing listed the sources.
/// </para>
/// <para>
/// The gate does not read the code for meaning. A source is a call that makes a tool function or a framework provider
/// that supplies tools (<see cref="Patterns"/>); a NEW one fails the test until someone adds it to
/// <see cref="Chained"/> (and names the file that composes it) or to <see cref="Outside"/> (and names the decision
/// that keeps it out). A source that disappears fails too, so the lists cannot go stale. A kind that appears
/// twice in one file is counted, so a second call in a file that already has one is new too.
/// </para>
/// <para>
/// What the gate cannot see: a tool that reaches the model through a shape the patterns do not name (a framework
/// provider type that is not in <see cref="FrameworkProvider"/>, a tool function made in a way other than the
/// ones listed), and the options of a provider (a <c>TextSearchProvider</c> listed here may or may not offer a
/// tool, depending on how it is built; the list says how this code builds it).
/// </para>
/// </remarks>
public sealed partial class ToolSourceGovernanceTests
{
    /// <summary>
    /// The files that build the chain for a tool: <c>ToolWrapperChain.Compose</c> directly, or
    /// <c>BuiltInToolGovernance.Govern</c>. A tool reaches the chain only through one of them.
    /// </summary>
    private static readonly string[] ChainEntries =
    [
        "src/Tracon.Core/Compilation/AgentDefinitionCompiler.Memory.cs",
        "src/Tracon.Core/Memory/ConversationMemoryScopeProvider.cs",
        "src/Tracon.Core/Tools/BuiltInToolGovernance.cs",
        "src/Tracon.Core/Tools/ToolRegistry.cs",
        "src/Tracon.Core/Workspace/SessionWorkspaceProvider.cs",
        "src/Tracon.Mcp/Internal/McpTenantTools.cs",
    ];

    /// <summary>Sources whose tools run through the chain. The value is the chain entry that composes them.</summary>
    private static readonly Dictionary<string, string> Chained = new(StringComparer.Ordinal)
    {
        // Registered in code (AddTool, AddToolsFrom, AddScopedTool) and composed by the registry when it is built.
        ["src/Tracon.Core/TraconBuilderExtensions.Tools.cs :: AIFunctionFactory.Create ×2"] = "src/Tracon.Core/Tools/ToolRegistry.cs",
        ["src/Tracon.Core/Tools/ToolMethodScanner.cs :: AIFunctionFactory.Create"] = "src/Tracon.Core/Tools/ToolRegistry.cs",

        // The built-in tools (generate_image; speak, transcribe and list_voices), registered like a tool of your own.
        ["src/Tracon.Core/Images/GenerateImageTool.cs :: AIFunction subclass:GenerateImageTool"] = "src/Tracon.Core/Tools/ToolRegistry.cs",
        ["src/Tracon.Voice/Tools/VoiceToolBase.cs :: AIFunction subclass:VoiceToolBase"] = "src/Tracon.Core/Tools/ToolRegistry.cs",

        // The tools of an MCP server, composed with the tenant's tool set.
        ["src/Tracon.Mcp/Internal/McpConnection.cs :: AIFunctionFactory.Create"] = "src/Tracon.Mcp/Internal/McpTenantTools.cs",

        // The tools Tracon adds to a run itself, composed by BuiltInToolGovernance.
        ["src/Tracon.Core/Knowledge/VectorSearchToolFactory.cs :: AIFunctionFactory.Create"] = "src/Tracon.Core/Compilation/AgentDefinitionCompiler.Memory.cs",
        ["src/Tracon.Core/Memory/ConversationMemoryScopeProvider.cs :: framework provider:ChatHistoryMemoryProvider"] = "src/Tracon.Core/Memory/ConversationMemoryScopeProvider.cs",
        ["src/Tracon.Core/Memory/ConversationMemoryScopeProvider.cs :: Tracon context provider:ConversationMemoryScopeProvider"] = "src/Tracon.Core/Memory/ConversationMemoryScopeProvider.cs",
        ["src/Tracon.Core/Workspace/SessionWorkspaceProvider.cs :: framework provider:FileAccessProvider"] = "src/Tracon.Core/Workspace/SessionWorkspaceProvider.cs",
        ["src/Tracon.Core/Workspace/SessionWorkspaceProvider.cs :: Tracon context provider:SessionWorkspaceProvider"] = "src/Tracon.Core/Workspace/SessionWorkspaceProvider.cs",
    };

    /// <summary>Sources whose tools stay outside the chain, each with the reason and the decision.</summary>
    private static readonly Dictionary<string, string> Outside = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs :: HostedToolRegistry.Create"] =
            "A hosted tool runs at the model provider; the call never reaches the process, so there is no body to wrap (K-1044).",
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.cs :: SubAgentToolNames.CreateFunctions"] =
            "A sub-agent in tool mode: ChildAgentInvoker carries the depth, the budget, the tenant and the wait limits.",
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs :: framework provider:BackgroundAgentsProvider"] =
            "The background sub-agents: ChildAgentInvoker carries the budget and the wait.",
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.Skills.cs :: framework provider:AgentSkillsProviderBuilder"] =
            "load_skill, read_skill_resource and run_skill_script are the framework's; the script budget is checked at the entry of the runner.",
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.Memory.cs :: framework provider:FileMemoryProvider"] =
            "The file memory tools report no cost and change only Tracon's own store.",
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.Memory.cs :: framework provider:TodoProvider"] =
            "The todo tools report no cost and change only the session state.",
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs :: AsHarnessAgent"] =
            "The harness agent's own providers (mode_* and the rest) report no cost and change only the agent's own state.",
        ["src/Tracon.Core/Graph/SubAgentToolNames.cs :: AsAIFunction"] =
            "Makes the function of an agent called as a tool; the wrapper owns the depth, the budget, the tenant and the wait limits.",
        ["src/Tracon.Core/Extensions/AgentOptionsGuard.cs :: extension provider ×2"] =
            "A provider an agent extension adds brings its own tools; the guard lets the provider through and does not inspect its tools (K-1030).",
    };

    /// <summary>Sources the patterns see that add no tool to the request, and why.</summary>
    private static readonly Dictionary<string, string> NoTools = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Core/Compilation/AgentDefinitionCompiler.Memory.cs :: framework provider:TextSearchProvider"] =
            "Searches before each turn (the default) and adds messages; its on-demand mode, which has a tool, is not used.",
        ["src/Tracon.Mcp/Internal/McpResourceContextProvider.cs :: Tracon context provider:McpResourceContextProvider"] =
            "Adds the text of the declared resources to the instructions; no tool.",
    };

    // A pattern with an "id" group names the type it found, so a second provider type in a file that already has one is new.
    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("AIFunctionFactory.Create", FunctionFactory()),
        ("AIFunction subclass", FunctionSubclass()),
        ("AsAIFunction", AsFunction()),
        ("framework provider", FrameworkProvider()),
        ("AsHarnessAgent", Harness()),
        ("HostedToolRegistry.Create", HostedTool()),
        ("SubAgentToolNames.CreateFunctions", SubAgentTool()),
        ("Tracon context provider", TraconProvider()),
        ("extension provider", ExtensionProvider()),
    ];

    [Fact]
    public void Every_source_of_tools_is_known_and_is_in_the_chain_or_named_as_outside_it()
    {
        var root = FindRepositoryRoot();
        var found = Sources(root);

        var listed = Chained.Keys.Concat(Outside.Keys).Concat(NoTools.Keys).ToHashSet(StringComparer.Ordinal);
        var unknown = found.Where(source => !listed.Contains(source)).Order(StringComparer.Ordinal).ToList();
        var gone = listed.Where(source => !found.Contains(source)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A new source of tools was added. Run it through the tool chain (BuiltInToolGovernance.Govern, or register it in the " +
            "tool registry) and add it to Chained, or, if it must stay outside, add it to Outside with the decision that says so.");
        gone.ShouldBeEmpty("A known source of tools is gone; remove it from the lists.");
        Chained.Keys.Intersect(Outside.Keys, StringComparer.Ordinal).Concat(Chained.Keys.Intersect(NoTools.Keys, StringComparer.Ordinal))
            .Concat(Outside.Keys.Intersect(NoTools.Keys, StringComparer.Ordinal)).ShouldBeEmpty("A source is in more than one list.");
    }

    [Fact]
    public void A_chained_source_names_a_file_that_really_builds_the_chain()
    {
        var root = FindRepositoryRoot();

        foreach (var (source, entry) in Chained)
        {
            ChainEntries.Contains(entry, StringComparer.Ordinal).ShouldBeTrue($"'{source}' names '{entry}', which is not a chain entry.");
            ChainCall().IsMatch(CodeOf(File.ReadAllText(Path.Combine(root, entry)))).ShouldBeTrue($"'{entry}' does not build the chain.");
        }
    }

    [Fact]
    public void Only_the_known_files_build_the_chain()
    {
        var root = FindRepositoryRoot();

        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsSource)
            .Where(path => ChainCall().IsMatch(CodeOf(File.ReadAllText(path))))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            [.. ChainEntries],
            customMessage: "A file builds the tool chain that is not a known entry (or an entry no longer does). A chain composed by hand " +
            "misses the layers added to ToolWrapperChain later; call BuiltInToolGovernance.Govern instead.");
    }

    [Fact]
    public void A_source_outside_the_chain_names_a_decision()
    {
        foreach (var (source, reason) in Outside)
        {
            DecisionReference().IsMatch(reason).ShouldBeTrue($"'{source}' stays outside the chain and does not name the decision that says so.");
        }
    }

    [Fact]
    public void A_source_that_adds_no_tool_says_why()
    {
        foreach (var (source, reason) in NoTools)
        {
            reason.ShouldNotBeNullOrWhiteSpace($"'{source}' adds no tool and does not say why.");
        }
    }

    [Theory]
    [InlineData("var tool = AIFunctionFactory.Create(Body, name: \"x\");", "AIFunctionFactory.Create")]
    [InlineData("internal sealed class SpeakTool : AIFunction", "AIFunction subclass:SpeakTool")]
    [InlineData("internal abstract class VoiceToolBase<T>(T options) : AIFunction", "AIFunction subclass:VoiceToolBase")]
    [InlineData("var tool = child.AsAIFunction(new AIFunctionFactoryOptions());", "AsAIFunction")]
    [InlineData("internal sealed class Wrapper<T> : AIContextProvider", "Tracon context provider:Wrapper")]
    [InlineData("var a = AIFunctionFactory.Create(A, \"a\"); var b = AIFunctionFactory.Create(B, \"b\");", "AIFunctionFactory.Create ×2")]
    [InlineData("providers.Add(new FileMemoryProvider(store, null, null));", "framework provider:FileMemoryProvider")]
    [InlineData("_inner = new ChatHistoryMemoryProvider(store, c, 8, init, options, logs);", "framework provider:ChatHistoryMemoryProvider")]
    [InlineData("return client.AsHarnessAgent(options, logs, services);", "AsHarnessAgent")]
    [InlineData("tools.Add(HostedToolRegistry.Create(hosted, context, name));", "HostedToolRegistry.Create")]
    [InlineData("tools.AddRange(SubAgentToolNames.CreateFunctions(children));", "SubAgentToolNames.CreateFunctions")]
    [InlineData("internal sealed class SessionWorkspaceProvider : AIContextProvider, IDisposable", "Tracon context provider:SessionWorkspaceProvider")]
    [InlineData("internal sealed class Wrapper(Foo foo) : MessageAIContextProvider", "Tracon context provider:Wrapper")]
    [InlineData(".AppendOnly(nameof(ChatClientAgentOptions.AIContextProviders), static options => options.AIContextProviders)", "extension provider")]
    public void The_patterns_see_each_kind_of_source(string source, string expected)
        => KindsOf(CodeOf(source)).ShouldContain(expected, StringComparer.Ordinal);

    [Theory]
    [InlineData("/// <c>AddTool(AIFunctionFactory.Create(...))</c>")]
    [InlineData("// providers.Add(new TodoProvider(options));")]
    [InlineData("var text = \"use AddTool(AIFunctionFactory.Create(...))\";")]
    [InlineData("var memory = new ChatHistoryMemoryProviderOptions();")]
    [InlineData("public sealed class ConversationMemoryEraser : ISubjectContentEraser")]
    [InlineData("internal sealed class AuthorizingAIFunction : DelegatingAIFunction")]
    public void The_patterns_leave_comments_strings_and_other_types_alone(string source)
        => KindsOf(CodeOf(source)).ShouldBeEmpty();

    /// <summary>The sources in <c>src/</c>, as <c>file :: kind</c>.</summary>
    private static HashSet<string> Sources(string root)
        => Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsSource)
            .SelectMany(path =>
            {
                var file = Path.GetRelativePath(root, path).Replace('\\', '/');

                return KindsOf(CodeOf(File.ReadAllText(path))).Select(kind => $"{file} :: {kind}");
            })
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The kinds of source in a piece of code; a kind with an "id" group is followed by the name it found, and a kind found more than once by its count.</summary>
    private static IEnumerable<string> KindsOf(string code)
        => Patterns
            .SelectMany(pattern => pattern.Pattern.Matches(code).Select(match =>
                match.Groups["id"] is { Success: true } id ? $"{pattern.Name}:{id.Value}" : pattern.Name))
            .GroupBy(static kind => kind, StringComparer.Ordinal)
            .Select(static group => group.Count() == 1 ? group.Key : $"{group.Key} ×{group.Count()}");

    // The Roslyn analyzers of Tracon.Generators describe the tool rules in text; the generated client is a client.
    private static bool IsSource(string path)
        => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           && !path.Contains($"{Path.DirectorySeparatorChar}Tracon.Generators{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
           && !path.Contains($"{Path.DirectorySeparatorChar}Tracon.Client{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>The code of a file without its comments and the text of its string literals.</summary>
    private static string CodeOf(string source)
    {
        var withoutStrings = StringLiteral().Replace(source, "\"\"");
        var withoutBlocks = BlockComment().Replace(withoutStrings, string.Empty);

        return LineComment().Replace(withoutBlocks, string.Empty);
    }

    [GeneratedRegex("\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\r\n]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\bAIFunctionFactory\.Create\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex FunctionFactory();

    // A class that IS a tool function. A wrapper (DelegatingAIFunction) is a layer of the chain, not a source.
    [GeneratedRegex(
        @"\bclass\s+(?<id>\w+)\s*(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*(?:[\w<>.]+\s*,\s*)*AIFunction\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex FunctionSubclass();

    [GeneratedRegex(@"\.AsAIFunction\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex AsFunction();

    // The framework's own providers that supply tools to a run. TextSearchProvider supplies one only in its on-demand mode.
    [GeneratedRegex(
        @"\bnew\s+(?<id>AgentModeProvider|AgentSkillsProvider|AgentSkillsProviderBuilder|BackgroundAgentsProvider|ChatHistoryMemoryProvider|FileAccessProvider|FileMemoryProvider|TextSearchProvider|TodoProvider)\s*\(",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex FrameworkProvider();

    [GeneratedRegex(@"\.AsHarnessAgent\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex Harness();

    [GeneratedRegex(@"\bHostedToolRegistry\.Create\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex HostedTool();

    [GeneratedRegex(@"\bSubAgentToolNames\.CreateFunctions\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex SubAgentTool();

    [GeneratedRegex(
        @"\bclass\s+(?<id>\w+)\s*(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*(?:[\w<>.]+\s*,\s*)*(?:Message)?AIContextProvider\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex TraconProvider();

    // The guard of the agent extension seam lists the provider property as append-only: that is where a
    // provider an extension adds is let through.
    [GeneratedRegex(@"\bAppendOnly\(\s*nameof\(\s*[\w.]+\.AIContextProviders\s*\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ExtensionProvider();

    [GeneratedRegex(@"\bToolWrapperChain\.Compose\(|\.Govern\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ChainCall();

    [GeneratedRegex(@"\b(?:K-\d+|Phase \d+|F-\d+)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex DecisionReference();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
