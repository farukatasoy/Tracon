using System.Text.Json;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies that when a <see cref="ModelBinding.Fallbacks"/> link answers
/// instead of the primary binding, the run record and cost resolution reflect
/// the model that ACTUALLY ran (phase 62, F-44) — not the primary binding
/// fixed at compile time.
/// </summary>
/// <remarks>
/// Runs the FULL pipeline (<see cref="AgentDefinitionCompiler"/> →
/// <see cref="ModelProviderRegistry"/> → <see cref="FallbackChatClient"/> →
/// <see cref="RunRecordingAgent"/>), not a single layer in isolation: the
/// claim under test is that the ambient signal <c>FallbackChatClient</c>
/// writes actually reaches <c>RunRecordingAgent.CompleteAsync</c> across the
/// real MAF call chain.
/// </remarks>
public sealed class FallbackRecordingTests
{
    [Fact]
    public async Task Fallback_model_overrides_the_runs_model_id()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var pricingResolver = new SpyPricingResolver();

        var agent = CreateAgent(
            store,
            pricingResolver,
            primaryClient: new FakeChatClient(_ => throw new TraconProviderUnavailableException("circuit open")),
            fallbackClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.ModelId.ShouldBe("fallback-model");
        run.ModelProvider.ShouldBe("fallback");

        pricingResolver.LastProvider.ShouldBe("fallback");
        pricingResolver.LastModel.ShouldBe("fallback-model");
    }

    [Fact]
    public async Task Fallback_model_is_reflected_in_the_ByModel_statistics_breakdown()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var agent = CreateAgent(
            store,
            new SpyPricingResolver(),
            primaryClient: new FakeChatClient(_ => throw new TraconProviderUnavailableException("circuit open")),
            fallbackClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))));

        await agent.RunAsync("hello");

        var stats = await store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByModel.ShouldContain(model => model.ModelId == "fallback-model");
        stats.ByModel.ShouldNotContain(model => model.ModelId == "primary-model");
    }

    [Fact]
    public async Task No_fallback_leaves_the_primary_model_id_unchanged()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var pricingResolver = new SpyPricingResolver();

        var agent = CreateAgent(
            store,
            pricingResolver,
            primaryClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "primary answer"))),
            fallbackClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "should never run"))));

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.ModelId.ShouldBe("primary-model");
        run.ModelProvider.ShouldBe("primary");

        pricingResolver.LastProvider.ShouldBe("primary");
        pricingResolver.LastModel.ShouldBe("primary-model");
    }

    [Fact]
    public async Task Fallback_event_payload_carries_the_reason_the_primary_was_skipped()
    {
        // RunEventType.ModelFallbackUsed documents that the payload carries
        // "the reason the primary was skipped". An operator reading only the
        // run record has no other place to learn WHY the model changed.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var agent = CreateAgent(
            store,
            new SpyPricingResolver(),
            primaryClient: new FakeChatClient(_ => throw new TraconProviderUnavailableException("circuit open")),
            fallbackClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))));

        await agent.RunAsync("hello");

        var payload = await ReadFallbackPayloadAsync(store);

        using var document = JsonDocument.Parse(payload);

        document.RootElement.TryGetProperty("reason", out var reason).ShouldBeTrue();
        reason.GetString().ShouldBe("provider_unavailable");
    }

    [Fact]
    public async Task Fallback_reason_classifies_a_rate_limited_primary()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var agent = CreateAgent(
            store,
            new SpyPricingResolver(),
            primaryClient: new FakeChatClient(_ => throw new InvalidOperationException("HTTP 429 rate limit exceeded")),
            fallbackClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))));

        await agent.RunAsync("hello");

        using var document = JsonDocument.Parse(await ReadFallbackPayloadAsync(store));

        document.RootElement.GetProperty("reason").GetString().ShouldBe("rate_limited");
    }

    [Fact]
    public async Task Fallback_reason_does_not_carry_the_provider_error_text()
    {
        // 🚨 The reason is a CLASSIFIED value, never the provider's own
        // message: a provider message can quote the request or the response
        // body, and a run event is permanent.
        const string ProviderText = "quota for account acme-42 exhausted";

        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var agent = CreateAgent(
            store,
            new SpyPricingResolver(),
            primaryClient: new FakeChatClient(_ => throw new InvalidOperationException($"HTTP 503 {ProviderText}")),
            fallbackClient: new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))));

        await agent.RunAsync("hello");

        (await ReadFallbackPayloadAsync(store)).ShouldNotContain(ProviderText, Case.Sensitive);
    }

    private static async Task<string> ReadFallbackPayloadAsync(InMemoryRunStore store)
    {
        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            events.Add(runEvent);
        }

        var fallback = events
            .Where(static runEvent => runEvent.Type == RunEventType.ModelFallbackUsed)
            .ShouldHaveSingleItem();

        return fallback.Payload.ShouldNotBeNull();
    }

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        IRunPricingResolver pricingResolver,
        FakeChatClient primaryClient,
        FakeChatClient fallbackClient)
    {
        var primaryProvider = new FakeModelProvider(primaryClient, name: "primary");
        var fallbackProvider = new FakeModelProvider(fallbackClient, name: "fallback");

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(primaryProvider, fallbackProvider),
            TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = new ModelBinding
            {
                Provider = "primary",
                Model = "primary-model",
                Fallbacks = [new ModelFallback { Provider = "fallback", Model = "fallback-model" }],
            },
        };

        return new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            modelId: "primary-model",
            modelProvider: "primary",
            pricingResolver: pricingResolver);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    private sealed class SpyPricingResolver : IRunPricingResolver
    {
        public string? LastProvider { get; private set; }

        public string? LastModel { get; private set; }

        public RunCost? Resolve(string? provider, string? model, RunUsage? usage)
        {
            LastProvider = provider;
            LastModel = model;

            if (model is null || usage is null)
            {
                return null;
            }

            return new RunCost { Source = PricingSource.Unknown };
        }
    }
}
