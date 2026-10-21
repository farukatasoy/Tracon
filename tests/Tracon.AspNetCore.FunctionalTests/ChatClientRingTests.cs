using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs (K-269): a blanket `using Tracon.Testing;`
// would make TraconTestHost ambiguous.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 232 (F-594): a chat client ring that an agent extension adds, through a real host — the
/// <see cref="ChatClientSlot.ModelCall"/> ring sees each call of the tool loop and the
/// <see cref="ChatClientSlot.Turn"/> ring one call per turn, on a chat agent and a harness agent;
/// the summarizer gets no ring; a failure names the extension; a registry that cannot place a ring
/// fails the compilation instead of dropping it.
/// </summary>
/// <remarks>
/// Each question crosses the DI boundary (the compiler and the registry are built inside the
/// catalog's chain) or the HTTP boundary; a registry unit test answers none of them.
/// </remarks>
public sealed class ChatClientRingTests
{
    private const string ProviderName = "ring-model";
    private const string ModelName = "ring-1";
    private const string AgentName = "ringed-agent";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_ModelCall_ring_sees_every_call_of_the_tool_loop_and_a_Turn_ring_sees_one_call(bool harness)
    {
        var log = new RingLog();
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.CallsTool("lookup").RespondsWith("found it"));

        await using var host = await StartAsync(
            provider,
            builder => builder.AddAgentExtension("rings", context =>
            {
                context.UseChatClient(ChatClientSlot.ModelCall, inner => new RecordingChatClientRing(inner, "model-call", log));
                context.UseChatClient(ChatClientSlot.Turn, inner => new RecordingChatClientRing(inner, "turn", log));
            }),
            Definition(["rings"], harness));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "look it up", SessionId = $"s-ring-{harness}" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("found it");
        body.ShouldNotContain("event: error");

        // The loop made two model calls: the user's question, then the tool result.
        provider.Requests.Count.ShouldBe(2);
        log.Of("model-call").Select(static call => call.LastRole).ShouldBe([ChatRole.User, ChatRole.Tool]);

        // The turn ring is outside the loop: one call, and the loop ran inside it.
        log.Of("turn").ShouldHaveSingleItem().LastRole.ShouldBe(ChatRole.User);
        log.Entries.ShouldBe(["turn", "model-call", "model-call"]);
    }

    [Fact]
    public async Task An_agent_that_names_no_ring_extension_keeps_the_pipeline_it_had()
    {
        var log = new RingLog();
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await StartAsync(
            provider,
            builder => builder.AddAgentExtension(
                "rings",
                context => context.UseChatClient(ChatClientSlot.ModelCall, inner => new RecordingChatClientRing(inner, "model-call", log))),
            Definition([], harness: false));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hi" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await response.Content.ReadAsStringAsync();

        provider.Requests.ShouldHaveSingleItem();
        log.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_compaction_summarizer_gets_no_ring()
    {
        var factoryCalls = 0;
        var log = new RingLog();
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await StartAsync(
            provider,
            builder => builder
                .AddModelProvider(new FakeModelProvider("summarizer").RespondsWith("summary"))
                .AddAgentExtension("rings", context => context.UseChatClient(ChatClientSlot.ModelCall, inner =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return new RecordingChatClientRing(inner, "model-call", log);
                })),
            Definition(["rings"], harness: false) with
            {
                Compaction = new CompactionSettings
                {
                    Strategy = CompactionStrategyKind.Summarization,
                    TriggerMessages = 20,
                    SummarizationModel = new ModelBinding { Provider = "summarizer", Model = "summarizer-1" },
                },
            });

        var agent = await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        // The agent holds two model clients: its own and the summarizer. Only its own took the ring.
        factoryCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_ring_factory_that_throws_fails_the_run_naming_the_extension_and_not_its_message()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await StartAsync(
            provider,
            builder => builder.AddAgentExtension(
                "broken-ring",
                static context => context.UseChatClient(
                    ChatClientSlot.ModelCall,
                    static _ => throw new InvalidOperationException("The host db.internal refused LEAK-MARKER-9"))),
            Definition(["broken-ring"], harness: false));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hi" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        body.ShouldContain("'broken-ring'");
        body.ShouldContain(nameof(InvalidOperationException));
        body.ShouldNotContain("LEAK-MARKER-9");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_ring_factory_that_returns_null_fails_the_compilation_naming_the_extension()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await StartAsync(
            provider,
            builder => builder.AddAgentExtension(
                "empty-ring",
                static context => context.UseChatClient(ChatClientSlot.Turn, static _ => null!)),
            Definition(["empty-ring"], harness: false));

        var failure = await CompileFailureAsync(host);

        failure.Message.ShouldContain("'empty-ring'", Case.Sensitive);
        failure.AgentName.ShouldBe(AgentName);
    }

    /// <summary>
    /// Microsoft Agent Framework looks for a tool loop in the chat client and adds its own above
    /// one in which it finds none; Tracon reads the loop's settings through the same lookup. A Turn
    /// ring that answers no <see cref="IChatClient.GetService"/> call hides Tracon's loop.
    /// </summary>
    [Fact]
    public async Task A_Turn_ring_that_hides_the_tool_loop_fails_the_compilation_naming_the_extension()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await StartAsync(
            provider,
            builder => builder.AddAgentExtension(
                "opaque-ring",
                static context => context.UseChatClient(ChatClientSlot.Turn, static inner => new OpaqueChatClient(inner))),
            Definition(["opaque-ring"], harness: false));

        var failure = await CompileFailureAsync(host);

        failure.Message.ShouldContain("'opaque-ring'", Case.Sensitive);
        failure.Message.ShouldContain(nameof(DelegatingChatClient), Case.Sensitive);
    }

    /// <summary>
    /// A host that replaces <see cref="IModelProviderRegistry"/> builds the client in one step,
    /// with no place for a ring. Building the agent without the ring would drop a compliance check
    /// without a word.
    /// </summary>
    [Fact]
    public async Task A_registry_of_the_host_cannot_place_a_ring_and_the_compilation_names_the_extension()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks the answer up.")
                .AddAgentExtension("rings", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => inner))
                .AddAgent(Definition(["rings"], harness: false))
                .AddAgent(Definition([], harness: false) with { Name = "plain-agent" }),
            configureServices: services => services.AddSingleton<IModelProviderRegistry>(new HostRegistry(provider)));

        var failure = await CompileFailureAsync(host);

        failure.Message.ShouldContain("'rings'", Case.Sensitive);
        failure.Message.ShouldContain(nameof(IModelProviderRegistry), Case.Sensitive);
        provider.Requests.ShouldBeEmpty();

        // An agent with no ring still compiles on the host's registry.
        var plain = await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync("plain-agent", culture: null, TestContext.Current.CancellationToken);
        plain.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_ring_passes_the_runs_cancellation_to_the_provider()
    {
        var log = new RingLog();
        var provider = new HangingModelProvider(ProviderName, respectsCancellation: true);

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgentExtension("rings", context =>
            {
                context.UseChatClient(ChatClientSlot.ModelCall, inner => new RecordingChatClientRing(inner, "model-call", log));
                context.UseChatClient(ChatClientSlot.Turn, inner => new RecordingChatClientRing(inner, "turn", log));
            })
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = ProviderName, Model = "hanging-1" },
                ExtensionNames = ["rings"],
                Origin = AgentDefinitionOrigin.Code,
            }));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        using var cancellation = new CancellationTokenSource();

        var run = agent.RunAsync("wait", cancellationToken: cancellation.Token);
        await provider.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        // Both rings saw the canceled call pass up through them; neither swallowed it.
        log.Of("model-call").ShouldHaveSingleItem().Failure.ShouldNotBeNull();
        log.Of("turn").ShouldHaveSingleItem().Failure.ShouldNotBeNull();
    }

    private static async Task<TraconCompilationException> CompileFailureAsync(TraconTestHost host)
    {
        TraconCompilationException? failure = null;

        try
        {
            await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken);
        }
        catch (TraconCompilationException exception)
        {
            failure = exception;
        }

        return failure.ShouldNotBeNull("the agent compiled");
    }

    private static AgentDefinition Definition(IReadOnlyList<string> extensionNames, bool harness)
        => new()
        {
            Name = AgentName,
            Instructions = "Answer from the lookup.",
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            ToolNames = ["lookup"],
            ExtensionNames = extensionNames,
            Harness = harness ? new HarnessSettings() : null,
            Origin = AgentDefinitionOrigin.Code,
        };

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, Action<ITraconBuilder> configure, AgentDefinition definition)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider)
                .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks the answer up.");
            configure(builder);
            builder.AddAgent(definition);
        });

    /// <summary>A ring that passes calls through and answers no service lookup.</summary>
    private sealed class OpaqueChatClient(IChatClient inner) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => inner.GetResponseAsync(messages, options, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => inner.GetStreamingResponseAsync(messages, options, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The ring owns nothing.
        }
    }

    /// <summary>A host's own registry: it builds the provider's raw client and no ring.</summary>
    private sealed class HostRegistry(IModelProvider provider) : IModelProviderRegistry
    {
        public IReadOnlyList<ModelProviderDescriptor> List()
            => [new ModelProviderDescriptor { Name = provider.Name, Models = provider.Models }];

        public IChatClient CreateChatClient(ModelBinding binding)
            => provider.CreateChatClient(binding).AsBuilder().UseFunctionInvocation().Build();

        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => new(CreateChatClient(binding));

        public ValueTask<IChatClient> CreateSetupChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => new(CreateChatClient(binding));

        public ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => new(false);
    }
}
