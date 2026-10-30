using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Where Tracon's own rings sit relative to an extension's ring, read from what each of
/// them does in a real host — the content guard inspects what a ring adds, the budget counts what
/// a ring sends, the response cache keys on the request a ring changed, and a ring that answers by
/// itself spends nothing.
/// </summary>
/// <remarks>
/// The claim is about positions in a pipeline that the DI-built registry assembles, so a unit test
/// of one ring proves none of it. The fallback chain and the circuit above the ring are in
/// <see cref="ChatClientRingFallbackTests"/>.
/// </remarks>
public sealed class ChatClientRingOrderTests
{
    private const string ProviderName = "order-model";
    private const string ModelName = "order-1";
    private const string AgentName = "ordered-agent";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Theory]
    [InlineData(ChatClientSlot.ModelCall)]
    [InlineData(ChatClientSlot.Turn)]
    public async Task The_content_guard_inspects_a_message_that_a_ring_adds(ChatClientSlot slot)
    {
        var guard = new RecordingContentGuard();
        var marker = $"RING-ADDED-{slot}";
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.EchoesUserMessage());

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgentExtension("adds", context => context.UseChatClient(slot, inner => new RecordingChatClientRing(inner, "adds", new RingLog(), marker)))
                .AddAgent(Definition(["adds"])),
            configureServices: services => services.AddSingleton<IContentGuard>(guard));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await response.Content.ReadAsStringAsync();

        // The guard sits below the ring, so the ring cannot send the model a message it never saw.
        provider.Requests.ShouldHaveSingleItem().Messages[^1].Text.ShouldBe(marker);
        guard.Inputs().ShouldContain(text => text.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// The limit of the claim above, pinned: the guard skips a system message on every agent (the
    /// definition's own instructions travel there), so a ring that adds one is not inspected. The
    /// documentation says so; this test fails the day the guard starts to inspect it.
    /// </summary>
    [Fact]
    public async Task The_content_guard_does_not_inspect_a_system_message_that_a_ring_adds()
    {
        var guard = new RecordingContentGuard();
        const string Marker = "RING-ADDED-SYSTEM";
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.RespondsWith("done"));

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgentExtension("adds-system", static context => context.UseChatClient(
                    ChatClientSlot.ModelCall,
                    static inner => new RecordingChatClientRing(inner, "adds-system", new RingLog(), Marker, ChatRole.System)))
                .AddAgent(Definition(["adds-system"])),
            configureServices: services => services.AddSingleton<IContentGuard>(guard));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await response.Content.ReadAsStringAsync();

        provider.Requests.ShouldHaveSingleItem().Messages[^1].Text.ShouldBe(Marker);
        guard.Inputs().ShouldContain(static text => text.Contains("hello", StringComparison.Ordinal));
        guard.Inputs().ShouldNotContain(static text => text.Contains(Marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// The ring sends the call twice. The budget sits below it, so it sees two calls: the first
    /// spends the budget and the second is refused. A budget above the ring would see one call
    /// and let the turn through. The run's own usage is read from what reaches the agent, so it
    /// cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task The_budget_counts_every_call_that_a_ModelCall_ring_sends()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(
            ModelName,
            static model => model.RespondsWith("first", inputTokens: 10, outputTokens: 5).RespondsWith("second", inputTokens: 10, outputTokens: 5));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgentExtension("twice", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new TwiceRing(inner)))
            .AddAgent(Definition(["twice"])));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var options = new TraconRunOptions { Budget = new AgentRunBudget { MaxTotalTokens = 15 } };

        await Should.ThrowAsync<TraconRunBudgetExceededException>(async () => await agent.RunAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            session: null,
            options,
            TestContext.Current.CancellationToken));

        provider.Requests.ShouldHaveSingleItem();
    }

    /// <summary>
    /// A compliance check that refuses a call answers by itself, as Purview does. The budget, the
    /// cache, the <c>chat</c> span and the provider are below it, so nothing is spent.
    /// </summary>
    [Fact]
    public async Task A_ModelCall_ring_that_answers_by_itself_spends_nothing()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.RespondsWith("answer", inputTokens: 10, outputTokens: 5));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgentExtension("refuses", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new RefusingRing(inner)))
            .AddAgent(Definition(["refuses"])));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain(RefusingRing.Answer);

        provider.Requests.ShouldBeEmpty();

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        (run.Usage?.InputTokens ?? 0).ShouldBe(0);
    }

    /// <summary>
    /// The <c>chat</c> span sits below the ring: every call that the ring sends gets its own span,
    /// and a ring that answers by itself leaves none.
    /// </summary>
    [Theory]
    [InlineData("twice", 2)]
    [InlineData("refuses", 0)]
    public async Task Each_call_that_a_ModelCall_ring_sends_gets_its_own_chat_span(string ring, int expectedSpans)
    {
        // A model name of this test alone: the listener sees the spans of every test in the process.
        var model = $"span-{ring}-1";
        var spans = 0;
        var provider = new FakeModelProvider(ProviderName).ForModel(model, static script => script.RespondsWith("first", "second"));

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.GetTagItem("gen_ai.operation.name") as string, "chat", StringComparison.Ordinal)
                    && string.Equals(activity.GetTagItem("gen_ai.request.model") as string, model, StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref spans);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgentExtension("twice", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new TwiceRing(inner)))
            .AddAgentExtension("refuses", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new RefusingRing(inner)))
            .AddAgent(Definition([ring]) with { Model = new ModelBinding { Provider = ProviderName, Model = model } }));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await response.Content.ReadAsStringAsync();

        provider.Requests.Count.ShouldBe(expectedSpans);
        Volatile.Read(ref spans).ShouldBe(expectedSpans);
    }

    [Fact]
    public async Task The_response_cache_keys_on_the_request_that_a_ModelCall_ring_changed()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel(ModelName, static model => model.RespondsWith("cached answer", "second answer"));

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgentExtension("normalizes", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new NormalizingRing(inner)))
                .AddAgent(Definition(["normalizes"]) with
                {
                    Model = new ModelBinding
                    {
                        Provider = ProviderName,
                        Model = ModelName,
                        ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) },
                    },
                }),
            configureServices: static services => services.AddDistributedMemoryCache());

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var first = await agent.RunAsync("Hello there", cancellationToken: TestContext.Current.CancellationToken);
        var second = await agent.RunAsync("hello THERE", cancellationToken: TestContext.Current.CancellationToken);

        // Two different user texts, one request after the ring: the second run is a cache hit.
        first.Text.ShouldBe("cached answer");
        second.Text.ShouldBe("cached answer");
        provider.Requests.ShouldHaveSingleItem().Messages[^1].Text.ShouldBe("HELLO THERE");
    }

    private static AgentDefinition Definition(IReadOnlyList<string> extensionNames)
        => new()
        {
            Name = AgentName,
            Instructions = "Answer.",
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            ExtensionNames = extensionNames,
            Origin = AgentDefinitionOrigin.Code,
        };

    private sealed class RecordingContentGuard : IContentGuard
    {
        private readonly List<string> _inputs = [];

        public string Name => "recording";

        public IReadOnlyList<string> Inputs()
        {
            lock (_inputs)
            {
                return [.. _inputs];
            }
        }

        public ValueTask<ContentGuardResult> InspectAsync(ContentGuardContext context, CancellationToken cancellationToken = default)
        {
            if (context.Direction == ContentGuardDirection.Input)
            {
                lock (_inputs)
                {
                    _inputs.Add(context.Text);
                }
            }

            return ValueTask.FromResult(ContentGuardResult.Allow);
        }
    }

    /// <summary>Sends every call to the client below it twice and returns the second answer.</summary>
    private sealed class TwiceRing(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var request = messages.ToList();
            await base.GetResponseAsync(request, options, cancellationToken).ConfigureAwait(false);
            return await base.GetResponseAsync(request, options, cancellationToken).ConfigureAwait(false);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = messages.ToList();

            await foreach (var _ in base.GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(false))
            {
                // The first answer is dropped; only its usage counts.
            }

            await foreach (var update in base.GetStreamingResponseAsync(request, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }
    }

    /// <summary>Answers every call by itself and never calls the client below it.</summary>
    private sealed class RefusingRing(IChatClient inner) : DelegatingChatClient(inner)
    {
        public const string Answer = "This request is not allowed by policy.";

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)));

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, Answer);
        }
    }

    /// <summary>Uppercases the text of the last user message before it passes the call on.</summary>
    private sealed class NormalizingRing(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetResponseAsync(Normalize(messages), options, cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetStreamingResponseAsync(Normalize(messages), options, cancellationToken);

        private static List<ChatMessage> Normalize(IEnumerable<ChatMessage> messages)
        {
            var request = messages.ToList();
            var last = request[^1];

            // A new message, not a change to the caller's: a ring must not change an object it was handed.
            request[^1] = new ChatMessage(last.Role, last.Text.ToUpperInvariant());
            return request;
        }
    }
}
