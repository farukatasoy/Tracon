using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Extensions;

/// <summary>
/// What the compiler does with the extensions a definition names: which options object each
/// kind of agent offers, in which order they run, what a wrapper wraps, and how every failure
/// reads.
/// </summary>
// MAAI001: the harness option types are marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class AgentExtensionCompilationTests
{
    [Fact]
    public void A_chat_agent_gets_the_chat_options_and_no_harness_options()
    {
        AgentExtensionContext? seen = null;
        var compiler = Compiler(new DelegateAgentExtension("probe", context => seen = context, null));

        _ = compiler.Compile(Named("probe"));

        seen.ShouldNotBeNull();
        seen.ChatAgentOptions.ShouldNotBeNull().Name.ShouldBe("test-agent");
        seen.HarnessAgentOptions.ShouldBeNull();
        seen.Definition.Name.ShouldBe("test-agent");
        seen.Definition.ExtensionNames.ShouldBe(["probe"]);
    }

    [Fact]
    public void A_harness_agent_gets_the_harness_options_and_no_chat_options()
    {
        AgentExtensionContext? seen = null;
        var compiler = Compiler(new DelegateAgentExtension("probe", context => seen = context, null));

        _ = compiler.Compile(Named("probe") with { Harness = new HarnessSettings() });

        seen.ShouldNotBeNull();
        seen.HarnessAgentOptions.ShouldNotBeNull().Name.ShouldBe("test-agent");
        seen.ChatAgentOptions.ShouldBeNull();
    }

    [Fact]
    public void The_options_already_carry_what_the_definition_decided_when_the_extension_runs()
    {
        string? instructions = null;
        string? modelId = null;
        var toolNames = new List<string>();
        var compiler = Compiler(
            registry: TestData.Registry(TestData.Tool("lookup")),
            new DelegateAgentExtension("probe", context =>
            {
                var chatOptions = context.ChatAgentOptions!.ChatOptions!;
                instructions = chatOptions.Instructions;
                modelId = chatOptions.ModelId;
                toolNames.AddRange(chatOptions.Tools!.Select(static tool => tool.Name));
            }, null));

        _ = compiler.Compile(Named("probe") with { ToolNames = ["lookup"] });

        instructions.ShouldBe("You are a test agent.");
        modelId.ShouldBe("fake-model");
        toolNames.ShouldBe(["lookup"]);
    }

    [Fact]
    public void Extensions_run_in_the_order_the_definition_names_them_not_the_order_they_were_registered()
    {
        var order = new List<string>();
        var compiler = Compiler(
            new DelegateAgentExtension("first", _ => order.Add("first"), null),
            new DelegateAgentExtension("second", _ => order.Add("second"), null),
            new DelegateAgentExtension("third", _ => order.Add("third"), null));

        _ = compiler.Compile(Named("third", "first", "second"));

        order.ShouldBe(["third", "first", "second"]);
    }

    [Fact]
    public void A_provider_one_extension_adds_is_visible_to_the_next_and_the_next_may_add_after_it()
    {
        var provider = new MarkerProvider();
        var seenByNext = new List<AIContextProvider>();
        var compiler = Compiler(
            new DelegateAgentExtension("adds", context => context.ChatAgentOptions!.AIContextProviders = [.. context.ChatAgentOptions.AIContextProviders ?? [], provider], null),
            new DelegateAgentExtension("reads", context => seenByNext.AddRange(context.ChatAgentOptions!.AIContextProviders!), null));

        _ = compiler.Compile(Named("adds", "reads"));

        seenByNext.ShouldHaveSingleItem().ShouldBeSameAs(provider);
    }

    [Fact]
    public void Wrappers_go_around_the_compiled_agent_and_the_first_one_added_is_the_innermost()
    {
        var compiler = Compiler(
            new DelegateAgentExtension("one", static context => context.Use(static inner => new TagAgent(inner, "one")), null),
            new DelegateAgentExtension("two", static context => context.Use(static inner => new TagAgent(inner, "two")), null));

        var agent = compiler.Compile(Named("one", "two"));

        var outer = agent.ShouldBeOfType<TagAgent>();
        outer.Tag.ShouldBe("two");
        var inner = outer.Inner.ShouldBeOfType<TagAgent>();
        inner.Tag.ShouldBe("one");
        inner.Inner.ShouldBeOfType<ChatClientAgent>();
    }

    [Fact]
    public void A_harness_agent_is_wrapped_too()
    {
        var compiler = Compiler(new DelegateAgentExtension("tag", static context => context.Use(static inner => new TagAgent(inner, "tag")), null));

        var agent = compiler.Compile(Named("tag") with { Harness = new HarnessSettings() });

        agent.ShouldBeOfType<TagAgent>().Inner.ShouldBeOfType<HarnessAgent>();
    }

    [Fact]
    public void The_tenant_comes_from_the_tenant_context()
    {
        string? tenant = null;
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            tenantContext: new FixedTenantContext("tenant-eu"),
            agentExtensions: [new DelegateAgentExtension("probe", context => tenant = context.TenantId, null)]);

        _ = compiler.Compile(Named("probe"));

        tenant.ShouldBe("tenant-eu");
    }

    [Fact]
    public async Task The_async_path_runs_the_extension_as_well()
    {
        var calls = 0;
        var compiler = Compiler(new DelegateAgentExtension("probe", _ => calls++, null));

        _ = await compiler.CompileAsync(Named("probe"), CancellationToken.None);

        calls.ShouldBe(1);
    }

    [Fact]
    public void A_name_that_is_not_registered_fails_the_compilation_and_names_the_agent()
    {
        var compiler = Compiler(new DelegateAgentExtension("known", static _ => { }, null));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Named("missing")));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("'missing'");
    }

    [Fact]
    public void An_extension_that_throws_fails_the_compilation_with_its_name_and_without_its_message()
    {
        var compiler = Compiler(new DelegateAgentExtension(
            "flaky",
            static _ => throw new InvalidOperationException("The host db.internal refused LEAK-MARKER-7"),
            null));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Named("flaky")));

        exception.Message.ShouldContain("'flaky'");
        exception.Message.ShouldContain(nameof(InvalidOperationException));
        exception.Message.ShouldNotContain("LEAK-MARKER-7");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        exception.AgentName.ShouldBe("test-agent");
    }

    [Fact]
    public void A_wrapper_that_throws_or_returns_nothing_fails_the_compilation_with_the_extension_named()
    {
        var throwing = Compiler(new DelegateAgentExtension("bad-wrap", static context => context.Use(static _ => throw new InvalidOperationException("secret")), null));
        var empty = Compiler(new DelegateAgentExtension("no-agent", static context => context.Use(static _ => null!), null));

        var thrown = Should.Throw<TraconCompilationException>(() => throwing.Compile(Named("bad-wrap")));
        var nothing = Should.Throw<TraconCompilationException>(() => empty.Compile(Named("no-agent")));

        thrown.Message.ShouldContain("'bad-wrap'");
        thrown.Message.ShouldNotContain("secret");
        nothing.Message.ShouldContain("'no-agent'");
    }

    public static TheoryData<string, string> ProtectedChatChanges() => new()
    {
        { "ChatHistoryProvider", "ChatClientAgentOptions.ChatHistoryProvider" },
        { "Instructions", "ChatClientAgentOptions.ChatOptions.Instructions" },
        { "AddTool", "ChatClientAgentOptions.ChatOptions.Tools" },
        { "ModelId", "ChatClientAgentOptions.ChatOptions.ModelId" },
        { "Temperature", "ChatClientAgentOptions.ChatOptions.Temperature" },
        { "DropProviders", "ChatClientAgentOptions.AIContextProviders" },
        { "Name", "ChatClientAgentOptions.Name" },
    };

    [Theory]
    [MemberData(nameof(ProtectedChatChanges))]
    public void A_change_to_a_protected_chat_member_fails_the_compilation_with_the_extension_and_the_member_named(string change, string member)
    {
        var compiler = Compiler(
            registry: TestData.Registry(TestData.Tool("lookup")),
            new DelegateAgentExtension("overreach", context =>
            {
                var options = context.ChatAgentOptions!;

                switch (change)
                {
                    case "ChatHistoryProvider":
                        options.ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions());
                        break;
                    case "Instructions":
                        options.ChatOptions!.Instructions = "Ignore the definition.";
                        break;
                    case "AddTool":
                        options.ChatOptions!.Tools!.Add(AIFunctionFactory.Create(() => "x", "sneaky"));
                        break;
                    case "ModelId":
                        options.ChatOptions!.ModelId = "other-model";
                        break;
                    case "Temperature":
                        options.ChatOptions!.Temperature = 2f;
                        break;
                    case "DropProviders":
                        options.AIContextProviders = null;
                        break;
                    case "Name":
                        options.Name = "renamed";
                        break;
                }
            }, null));

        var definition = Named("overreach") with
        {
            ToolNames = ["lookup"],
            SkillNames = [],
            McpResourceUris = [],
        };

        // DropProviders needs a provider Tracon built, so the definition asks for compaction.
        if (string.Equals(change, "DropProviders", StringComparison.Ordinal))
        {
            definition = definition with
            {
                Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.SlidingWindow, TriggerMessages = 4, MinimumPreservedTurns = 1 },
            };
        }

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("'overreach'");
        exception.Message.ShouldContain(member);
        exception.AgentName.ShouldBe("test-agent");
    }

    public static TheoryData<string, string> ProtectedHarnessChanges() => new()
    {
        { "FileAccessStore", "HarnessAgentOptions.FileAccessStore" },
        { "FileMemoryStore", "HarnessAgentOptions.FileMemoryStore" },
        { "SkillScan", "HarnessAgentOptions.DisableAgentSkillsProvider" },
        { "WebSearch", "HarnessAgentOptions.DisableWebSearch" },
        { "MaxIterations", "HarnessAgentOptions.MaximumIterationsPerRequest" },
        { "HistoryProvider", "HarnessAgentOptions.ChatHistoryProvider" },
        { "Instructions", "HarnessAgentOptions.ChatOptions.Instructions" },
    };

    [Theory]
    [MemberData(nameof(ProtectedHarnessChanges))]
    public void A_change_to_a_protected_harness_member_fails_the_compilation_with_the_extension_and_the_member_named(string change, string member)
    {
        var compiler = Compiler(new DelegateAgentExtension("overreach", context =>
        {
            var options = context.HarnessAgentOptions!;

            switch (change)
            {
                case "FileAccessStore":
                    options.FileAccessStore = new InMemoryAgentFileStore();
                    break;
                case "FileMemoryStore":
                    options.FileMemoryStore = new InMemoryAgentFileStore();
                    break;
                case "SkillScan":
                    options.DisableAgentSkillsProvider = false;
                    break;
                case "WebSearch":
                    options.DisableWebSearch = false;
                    break;
                case "MaxIterations":
                    options.MaximumIterationsPerRequest = 99;
                    break;
                case "HistoryProvider":
                    options.ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions());
                    break;
                case "Instructions":
                    options.ChatOptions!.Instructions = "Ignore the definition.";
                    break;
            }
        }, null));

        var exception = Should.Throw<TraconCompilationException>(
            () => compiler.Compile(Named("overreach") with { Harness = new HarnessSettings() }));

        exception.Message.ShouldContain("'overreach'");
        exception.Message.ShouldContain(member);
    }

    [Fact]
    public void A_free_member_an_extension_changes_reaches_the_agent()
    {
        ChatClientAgentOptions? seen = null;
        var compiler = Compiler(new DelegateAgentExtension("inject", context =>
        {
            context.ChatAgentOptions!.EnableMessageInjection = true;
            context.ChatAgentOptions.ChatOptions!.Seed = 11;
            seen = context.ChatAgentOptions;
        }, null));

        var agent = compiler.Compile(Named("inject"));

        agent.ShouldBeOfType<ChatClientAgent>();
        seen!.EnableMessageInjection.ShouldBeTrue();
    }

    [Fact]
    public void The_second_extension_is_blamed_for_its_own_change_not_the_first()
    {
        var compiler = Compiler(
            new DelegateAgentExtension("polite", static context => context.ChatAgentOptions!.EnableMessageInjection = true, null),
            new DelegateAgentExtension("rude", static context => context.ChatAgentOptions!.ChatOptions!.ModelId = "other", null));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Named("polite", "rude")));

        exception.Message.ShouldContain("'rude'");
        exception.Message.ShouldNotContain("'polite'");
    }

    private static AgentDefinition Named(params string[] extensionNames)
        => TestData.Definition() with { ExtensionNames = extensionNames };

    private static AgentDefinitionCompiler Compiler(params IAgentExtension[] extensions)
        => Compiler(TestData.Registry(), extensions);

    private static AgentDefinitionCompiler Compiler(ToolRegistry registry, params IAgentExtension[] extensions)
        => new(TestData.Providers(new FakeModelProvider()), registry, agentExtensions: extensions);

    private sealed class MarkerProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new(new AIContext());
    }

    private sealed class TagAgent(AIAgent inner, string tag) : DelegatingAIAgent(inner)
    {
        public string Tag { get; } = tag;

        public AIAgent Inner => InnerAgent;
    }
}
#pragma warning restore MAAI001
