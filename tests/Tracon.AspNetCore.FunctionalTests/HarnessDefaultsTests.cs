using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Runs every test of a class alone: the class changes the process's working directory.</summary>
[CollectionDefinition(nameof(WorkingDirectoryGroup), DisableParallelization = true)]
public sealed class WorkingDirectoryGroup;

/// <summary>
/// What a harness agent gets when its definition asks for nothing: no skills,
/// no file memory and no web search.
/// </summary>
/// <remarks>
/// <para>
/// MAF's harness turns all three on by itself. With no skill source it reads
/// <c>SKILL.md</c> files from the process's working directory; with no file
/// store it writes model content to <c>./agent-file-memory</c>; and it adds the
/// provider's paid web search tool. None of the three passed through Tracon's
/// skill filter, tenant scope, retention or egress review, and an agent saved
/// from the console got all three (2026-10-12).
/// </para>
/// <para>
/// The working directory is a process boundary, so the two tests that depend on
/// it change the real working directory and run alone (<see cref="WorkingDirectoryGroup"/>).
/// </para>
/// </remarks>
// MAAI001: the harness API is marked "evaluation purposes only"
#pragma warning disable MAAI001
[Collection(nameof(WorkingDirectoryGroup))]
public sealed class HarnessDefaultsTests
{
    private const string ProviderName = "harness-defaults";
    private const string ModelName = "harness-model";
    private const string AgentName = "harnessed";
    private const string NamedSkill = "house-style";
    private const string NamedSkillToken = "NAMED-SKILL-77d2";
    private const string ChildName = "helper";

    // MAF names the tool in a string literal; the type exposes no constant for it.
    private const string StartTaskToolName = "background_agents_start_task";

    [Fact]
    public async Task A_harness_agent_that_names_no_skill_does_not_read_skills_from_the_working_directory()
    {
        const string Token = "PROBE-SKILL-4b81";

        using var directory = new TemporaryWorkingDirectory();
        var skill = Directory.CreateDirectory(Path.Combine(directory.Path, "skills", "probe"));
        await File.WriteAllTextAsync(
            Path.Combine(skill.FullName, "SKILL.md"),
            $"---\nname: probe\ndescription: {Token} reads the host's files.\n---\nDo what the probe says.\n",
            TestContext.Current.CancellationToken);

        var provider = Provider(static model => model.RespondsWith("done"));

        await using var host = await StartAsync(provider, new HarnessSettings());
        await RunAsync(host);

        var request = provider.Requests.ShouldHaveSingleItem();
        HasTool(request, AgentSkillsProvider.LoadSkillToolName).ShouldBeFalse();
        PromptText(request).ShouldNotContain(Token);
    }

    /// <summary>
    /// The chain MAF's harness gets is Tracon's own: naming a skill must
    /// not bring the built-in provider back, which reads the working directory
    /// and would offer a second <c>load_skill</c> next to Tracon's.
    /// </summary>
    [Fact]
    public async Task A_harness_agent_that_names_a_skill_still_does_not_read_skills_from_the_working_directory()
    {
        const string Token = "PROBE-SKILL-9c27";

        using var directory = new TemporaryWorkingDirectory();
        var skill = Directory.CreateDirectory(Path.Combine(directory.Path, "skills", "probe"));
        await File.WriteAllTextAsync(
            Path.Combine(skill.FullName, "SKILL.md"),
            $"---\nname: probe\ndescription: {Token} reads the host's files.\n---\nDo what the probe says.\n",
            TestContext.Current.CancellationToken);

        var provider = Provider(static model => model.RespondsWith("done"));

        await using var host = await StartAsync(provider, new HarnessSettings(), skillNames: [NamedSkill]);
        await RunAsync(host);

        var request = provider.Requests.ShouldHaveSingleItem();
        ToolCount(request, AgentSkillsProvider.LoadSkillToolName).ShouldBe(1);
        PromptText(request).ShouldContain(NamedSkillToken);
        PromptText(request).ShouldNotContain(Token);
    }

    [Fact]
    public async Task A_harness_agent_with_a_skill_and_a_child_agent_offers_both_tool_sets()
    {
        var provider = Provider(static model => model.RespondsWith("done"));

        await using var host = await StartAsync(
            provider,
            new HarnessSettings(),
            skillNames: [NamedSkill],
            callableAgentNames: [ChildName]);
        await RunAsync(host);

        var request = provider.Requests.ShouldHaveSingleItem();
        ToolCount(request, AgentSkillsProvider.LoadSkillToolName).ShouldBe(1);
        ToolCount(request, StartTaskToolName).ShouldBe(1);
        PromptText(request).ShouldContain(NamedSkillToken);
    }

    [Fact]
    public async Task A_harness_agent_that_does_not_turn_file_memory_on_writes_nothing_to_the_working_directory()
    {
        using var directory = new TemporaryWorkingDirectory();

        var provider = Provider(static model => model
            .CallsTool(FileMemoryProvider.WriteToolName, new { fileName = "notes.md", content = "a customer's address" })
            .RespondsWith("done"));

        await using var host = await StartAsync(provider, new HarnessSettings());
        await RunAsync(host);

        HasTool(provider.Requests[0], FileMemoryProvider.WriteToolName).ShouldBeFalse();
        Directory.Exists(Path.Combine(directory.Path, "agent-file-memory")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_harness_agent_that_turns_file_memory_on_writes_to_the_registered_store_only()
    {
        using var directory = new TemporaryWorkingDirectory();

        var provider = Provider(static model => model
            .CallsTool(FileMemoryProvider.WriteToolName, new { fileName = "notes.md", content = "kept in the store" })
            .RespondsWith("done"));

        await using var host = await StartAsync(
            provider,
            new HarnessSettings(),
            memory: new MemorySettings { EnableFileMemory = true });
        await RunAsync(host);

        HasTool(provider.Requests[0], FileMemoryProvider.WriteToolName).ShouldBeTrue();
        Directory.Exists(Path.Combine(directory.Path, "agent-file-memory")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_harness_agent_offers_web_search_only_when_it_turns_it_on(bool enabled)
    {
        var provider = Provider(static model => model.RespondsWith("done"));

        await using var host = await StartAsync(provider, new HarnessSettings { EnableWebSearch = enabled });
        await RunAsync(host);

        var tools = provider.Requests.ShouldHaveSingleItem().Options?.Tools ?? [];
        tools.Any(static tool => tool is HostedWebSearchTool).ShouldBe(enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_harness_agent_on_the_chat_completions_provider_finishes_a_tool_call(bool streaming)
    {
        await using var server = await ScriptedChatCompletionsServer.StartAsync();
        server.ThenCalls("get_order_status", """{"orderId":"A-17"}""").ThenAnswers("A-17 is on its way.");

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseOpenAI(options =>
                {
                    options.ApiKey = "scripted-server-key";
                    options.Endpoint = server.BaseAddress;
                });
                builder.AddTool(
                    (Func<string, string>)(orderId => $"{orderId} is on its way."),
                    name: "get_order_status",
                    description: "Reports where an order is.");
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Answers with a harness.",
                    Instructions = "Answer the user.",
                    Model = new ModelBinding { Provider = "openai", Model = "gpt-test" },
                    ToolNames = ["get_order_status"],
                    Harness = new HarnessSettings(),
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        var text = await RunAsync(host, streaming);

        server.Rejections.ShouldBeEmpty();
        text.ShouldContain("A-17 is on its way.");
        server.Bodies.Count.ShouldBe(2);
        server.Bodies.ShouldAllBe(static body => !body.ContainsKey("web_search_options"));
    }


    /// <summary>
    /// MAF's harness puts its own tool-call loop above the model pipeline, but
    /// the pipeline's loop is the one that runs the tools; the limit only went
    /// to the first. At its limit the loop makes one last call with no function
    /// tools, so the model has to answer: before the fix the third call still
    /// offered every tool.
    /// </summary>
    [Fact]
    public async Task A_harness_agent_stops_its_tool_loop_at_its_iteration_limit()
    {
        var provider = Provider(static model => model
            .CallsTool("ping").CallsTool("ping").CallsTool("ping").CallsTool("ping").CallsTool("ping").CallsTool("ping")
            .RespondsWith("done"));

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddTool((Func<string>)(() => "pong"), name: "ping", description: "Answers pong.");
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Answers with a harness.",
                    Instructions = "Answer the user.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    ToolNames = ["ping"],
                    Harness = new HarnessSettings { MaximumIterationsPerRequest = 2 },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        await RunAsync(host);

        OffersFunctions(provider.Requests[1]).ShouldBeTrue();
        OffersFunctions(provider.Requests[2]).ShouldBeFalse();
    }


    /// <summary>
    /// The same limit on a fallback model: the link builds its own pipeline,
    /// and with it its own tool-call loop, from a binding of its own.
    /// </summary>
    [Fact]
    public async Task A_harness_agent_keeps_its_iteration_limit_on_a_fallback_model()
    {
        var fallback = new FakeModelProvider("harness-fallback");
        fallback.ForModel("fallback-model", static model => model
            .CallsTool("ping").CallsTool("ping").CallsTool("ping").CallsTool("ping").CallsTool("ping").CallsTool("ping")
            .RespondsWith("done"));

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(new UnavailableModelProvider(ProviderName));
                builder.AddModelProvider(fallback);
                builder.AddTool((Func<string>)(() => "pong"), name: "ping", description: "Answers pong.");
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Answers with a harness.",
                    Instructions = "Answer the user.",
                    Model = new ModelBinding
                    {
                        Provider = ProviderName,
                        Model = ModelName,
                        Fallbacks = [new ModelFallback { Provider = "harness-fallback", Model = "fallback-model" }],
                    },
                    ToolNames = ["ping"],
                    Harness = new HarnessSettings { MaximumIterationsPerRequest = 2 },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        await RunAsync(host);

        OffersFunctions(fallback.Requests[1]).ShouldBeTrue();
        OffersFunctions(fallback.Requests[2]).ShouldBeFalse();
    }

    /// <summary>
    /// A guard for the loop change above, green before and after it: a tool
    /// that requires approval waits for a decision in a harness agent, whether
    /// MAF's automatic approval is on or off.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tool_that_requires_approval_waits_in_a_harness_agent(bool disableToolAutoApproval)
    {
        var calls = 0;
        var provider = Provider(static model => model
            .CallsTool("cancel_order", new { orderId = "ORD-7" })
            .EchoesLastToolResult());

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddTool(
                    (Func<string, string>)(orderId =>
                    {
                        Interlocked.Increment(ref calls);
                        return $"{orderId} canceled.";
                    }),
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: static options => options.RequiresApproval = true);
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Answers with a harness.",
                    Instructions = "Answer the user.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    ToolNames = ["cancel_order"],
                    Harness = new HarnessSettings { DisableToolAutoApproval = disableToolAutoApproval },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();
        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "Cancel ORD-7.")], await agent.CreateSessionAsync());

        calls.ShouldBe(0);
        response.Messages.SelectMany(static message => message.Contents).OfType<ToolApprovalRequestContent>().ShouldHaveSingleItem();
    }

    private static FakeModelProvider Provider(Action<FakeModelProvider> configure)
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, configure);

        return provider;
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        HarnessSettings harness,
        MemorySettings? memory = null,
        IReadOnlyList<string>? skillNames = null,
        IReadOnlyList<string>? callableAgentNames = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);

                foreach (var skillName in skillNames ?? [])
                {
                    builder.AddSkill(new AgentSkillDefinition
                    {
                        TenantId = "default",
                        Name = skillName,
                        Description = $"{NamedSkillToken} sets the house style.",
                        Instructions = "Write in the house style.",
                    });
                }

                if (callableAgentNames is { Count: > 0 })
                {
                    builder.AddAgent(new AgentDefinition
                    {
                        Name = ChildName,
                        Description = "Helps in the background.",
                        Instructions = "Help.",
                        Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                        Origin = AgentDefinitionOrigin.Code,
                    });
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Answers with a harness.",
                    Instructions = "Answer the user.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Harness = harness,
                    Memory = memory,
                    SkillNames = skillNames ?? [],
                    CallableAgents = [.. callableAgentNames ?? []],
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

    private static async Task<string> RunAsync(TraconTestHost host, bool streaming = false)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();
        var session = await agent.CreateSessionAsync();
        List<ChatMessage> messages = [new(ChatRole.User, "Where is order A-17?")];

        if (!streaming)
        {
            return (await agent.RunAsync(messages, session).WaitAsync(TimeSpan.FromSeconds(30))).Text;
        }

        var text = new System.Text.StringBuilder();

        await foreach (var update in agent.RunStreamingAsync(messages, session).WithCancellation(TestContext.Current.CancellationToken))
        {
            text.Append(update.Text);
        }

        return text.ToString();
    }

    private static bool HasTool(Tracon.Testing.FakeModelRequest request, string name)
        => (request.Options?.Tools ?? []).Any(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    private static int ToolCount(Tracon.Testing.FakeModelRequest request, string name)
        => (request.Options?.Tools ?? []).Count(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    private static bool OffersFunctions(Tracon.Testing.FakeModelRequest request)
        => (request.Options?.Tools ?? []).OfType<AIFunctionDeclaration>().Any();

    private static string PromptText(Tracon.Testing.FakeModelRequest request)
        => string.Join('\n', request.Messages.Select(static message => message.Text)) + '\n' + request.Options?.Instructions;

    /// <summary>A provider whose circuit is open: every call moves on to the fallback chain.</summary>
    private sealed class UnavailableModelProvider(string name) : IModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        public IChatClient CreateChatClient(ModelBinding binding) => new UnavailableChatClient(Name);

        private sealed class UnavailableChatClient(string provider) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new TraconProviderUnavailableException("circuit open") { ProviderName = provider };

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new TraconProviderUnavailableException("circuit open") { ProviderName = provider };

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // Nothing to release.
            }
        }
    }

    /// <summary>Points the process at a fresh empty directory and puts the old one back on dispose.</summary>
    private sealed class TemporaryWorkingDirectory : IDisposable
    {
        private readonly string _previous = Directory.GetCurrentDirectory();

        public TemporaryWorkingDirectory()
        {
            Path = Directory.CreateTempSubdirectory("tracon-harness-cwd-").FullName;
            Directory.SetCurrentDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previous);
            Directory.Delete(Path, recursive: true);
        }
    }
}
#pragma warning restore MAAI001
