using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Diagnostics;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The telemetry ring of a model-call pipeline sits where a call really reaches the provider: below the budget, the
/// response cache and the hosted tool ring, above the content guard. A call one of those rings answers, or refuses, never
/// reaches it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The shared telemetry client is put in by the registry with the other rings, and the order they are put in is the
/// only thing that places it. Above the budget it would open a <c>chat</c> span for a turn the budget refused and write an
/// error for it; above the cache it would open one for a hit, which spends nothing and writes no span. The tests that
/// compare the ring with a client built over a bare pipeline cannot see this, so these run the pipeline the registry builds,
/// with every ring that can answer a call turned on, and count the spans of a lane that belongs to the test.
/// </para>
/// </remarks>
public sealed class ChatPipelineTelemetryPositionTests
{
    private const string Provider = "openai-responses";
    private const string Model = "fake-model";

    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "question")];

    public static TheoryData<bool> Modes => new() { false, true };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_call_the_response_cache_answers_opens_no_chat_span_and_reaches_no_provider(bool streaming)
    {
        using var probe = new ChatSpanProbe();
        var provider = new CountingClient();
        var client = Registry(probe, provider, new WarningLogFactory()).CreateChatClient(Binding());

        await CallAsync(client, streaming);
        ChatSpans(probe).Count.ShouldBe(1, "the first call is a miss: it reaches the provider and writes its span");

        await CallAsync(client, streaming);
        await CallAsync(client, streaming);

        provider.Calls.ShouldBe(1, "the second and third call are hits");
        ChatSpans(probe).Count.ShouldBe(1, "a hit opens no span and records no duration: the ring sits below the cache");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_turn_the_budget_refuses_opens_no_chat_span_and_logs_no_operation_error(bool streaming)
    {
        using var probe = new ChatSpanProbe();
        var warnings = new WarningLogFactory();
        var provider = new CountingClient();
        var client = Registry(probe, provider, warnings).CreateChatClient(Binding(cache: false));
        var budget = new AgentRunBudget { MaxTotalTokens = 5 };
        budget.RecordUsage(10);
        budget.IsExhausted.ShouldBeTrue();

        await InScopeAsync(Scope(budget), async () =>
        {
            await Should.ThrowAsync<TraconRunBudgetExceededException>(async () => await CallAsync(client, streaming));
        });

        provider.Calls.ShouldBe(0, "the blocked turn never reaches the provider");
        ChatSpans(probe).ShouldBeEmpty("the budget throws before the telemetry ring: no span, no error type, no run-trace row");
        warnings.Entries.ShouldBeEmpty("the ring never saw the turn, so it logs no operation error");
    }

    [Fact]
    public async Task The_hosted_tool_ring_reads_the_response_the_provider_really_answered_and_a_replay_is_not_billed_again()
    {
        using var probe = new ChatSpanProbe();
        var provider = new CountingClient(new WebSearchToolCallContent("ws_1"));
        var client = Registry(probe, provider, new WarningLogFactory()).CreateChatClient(Binding());
        var first = Scope();
        var second = Scope();

        await InScopeAsync(first, () => CallAsync(client, streaming: false));
        await InScopeAsync(second, () => CallAsync(client, streaming: false));

        provider.Calls.ShouldBe(1, "the second call was a cache hit");
        first.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m, "the search of the real call is billed once");
        second.NonTokenCost!.Seal().ShouldBeNull("a replayed response carries a search nobody ran again");
        ChatSpans(probe).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_call_that_reaches_the_provider_through_every_ring_writes_one_span_with_its_usage()
    {
        using var probe = new ChatSpanProbe();
        var provider = new CountingClient();
        var client = Registry(probe, provider, new WarningLogFactory()).CreateChatClient(Binding(cache: false));
        var budget = new AgentRunBudget { MaxTotalTokens = 1_000 };

        await InScopeAsync(Scope(budget), () => CallAsync(client, streaming: false));

        provider.Calls.ShouldBe(1);
        var span = ChatSpans(probe).ShouldHaveSingleItem();
        span.GetTagItem("gen_ai.usage.input_tokens").ShouldBe(7);
        span.GetTagItem("gen_ai.usage.output_tokens").ShouldBe(3);
        budget.ConsumedTokens.ShouldBe(10, "the budget counts the call once, with the ring in its place");
    }

    /// <summary>
    /// The model-call spans of the probe's source. The tool loop writes its own span to the same source (it takes the source
    /// from the ring under it), once for every call it makes, whatever the rings below answer.
    /// </summary>
    private static List<System.Diagnostics.Activity> ChatSpans(ChatSpanProbe probe)
        => [.. probe.Spans().Where(static span => string.Equals(span.GetTagItem("gen_ai.operation.name")?.ToString(), "chat", StringComparison.Ordinal))];

    private static ModelProviderRegistry Registry(ChatSpanProbe probe, CountingClient provider, WarningLogFactory warnings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<TraconOptions>>(Prices());

        return new ModelProviderRegistry(
            [new FakeModelProvider(provider, name: Provider)],
            distributedCache: new FakeDistributedCache(),
            services: services.BuildServiceProvider(),
            telemetryLanes: probe.NewLanes(warnings));
    }

    private static ModelBinding Binding(bool cache = true)
        => new()
        {
            Provider = Provider,
            Model = Model,
            ResponseCache = cache ? new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(5) } : null,
        };

    private static StaticOptionsMonitor<TraconOptions> Prices()
    {
        var options = new TraconOptions();
        options.Pricing.Currency = "USD";
        options.Pricing.HostedTools[Provider] = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["WebSearch"] = new HostedToolPriceOverride { PerCall = 0.01m },
        };

        return new StaticOptionsMonitor<TraconOptions>(options);
    }

    private static AgentRunScope Scope(AgentRunBudget? budget = null)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            Budget = budget,
            ToolUsage = new ToolUsageAccumulator(),
            HostedCalls = new HostedCallLedger(),
            NonTokenCost = new NonTokenCostAccumulator("USD", budget),
        };

    private static async Task InScopeAsync(AgentRunScope scope, Func<Task> body)
    {
        TraconRunContext.SetCurrent(scope);

        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static async Task CallAsync(IChatClient client, bool streaming)
    {
        var options = new ChatOptions { ModelId = Model };

        if (streaming)
        {
            await foreach (var unused in client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken))
            {
            }

            return;
        }

        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
    }

    /// <summary>The provider's raw client: answers with a fixed text and usage, and counts the calls that reach it.</summary>
    private sealed class CountingClient : IChatClient
    {
        private readonly AIContent[] _extra;
        private int _calls;

        public CountingClient(params AIContent[] extra) => _extra = extra;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. _extra, new TextContent("answer")]))
            {
                Usage = new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3, TotalTokenCount = 10 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            yield return new ChatResponseUpdate(ChatRole.Assistant, "answer");

            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3, TotalTokenCount = 10 })]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
