using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

public sealed class RunRecordingAgentTests
{
    [Fact]
    public async Task Basarili_calistirma_olay_sirasini_yazar()
    {
        var store = new InMemoryRunStore();
        var agent = CreateAgent(store, new FakeChatClient());

        await agent.RunAsync("merhaba");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.CompletedAt.ShouldNotBeNull();

        var types = await ReadEventTypesAsync(store, run.Id);
        types[0].ShouldBe(RunEventType.RunStarted);
        types[^1].ShouldBe(RunEventType.RunCompleted);
    }

    [Fact]
    public async Task Olay_sira_numaralari_bosluksuz_artar()
    {
        var store = new InMemoryRunStore();
        var agent = CreateAgent(store, new FakeChatClient());

        await agent.RunAsync("merhaba");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var sequences = new List<long>();
        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe(Enumerable.Range(0, sequences.Count).Select(static i => (long)i).ToList());
    }

    [Fact]
    public async Task Tool_cagrilari_olaya_donusur()
    {
        var store = new InMemoryRunStore();

        var client = new FakeChatClient(_ => new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_order", new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = 42 })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "kargoda")]),
            new ChatMessage(ChatRole.Assistant, "Siparisiniz kargoda."),
        ]));

        var agent = CreateAgent(store, client);

        await agent.RunAsync("siparisim nerede");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        var invoking = events.Where(static e => e.Type == RunEventType.ToolInvoking).ShouldHaveSingleItem();
        invoking.ToolName.ShouldBe("get_order");
        invoking.ToolCallId.ShouldBe("call-1");
        invoking.Payload!.ShouldContain("id=42");

        var invoked = events.Where(static e => e.Type == RunEventType.ToolInvoked).ShouldHaveSingleItem();
        invoked.ToolCallId.ShouldBe("call-1");
        invoked.Payload.ShouldBe("kargoda");
    }

    [Fact]
    public async Task Akisli_calistirma_metin_parcalarini_yazar()
    {
        var store = new InMemoryRunStore();

        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Mer"),
            new ChatResponseUpdate(ChatRole.Assistant, "haba"),
        ]);

        var agent = CreateAgent(store, client);

        var received = new List<string>();
        await foreach (var update in agent.RunStreamingAsync("selam"))
        {
            received.Add(update.Text);
        }

        received.ShouldBe(["Mer", "haba"]);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.IsStreaming.ShouldBeTrue();

        var deltas = (await ReadEventsAsync(store, run.Id))
            .Where(static e => e.Type == RunEventType.MessageDelta)
            .Select(static e => e.Text)
            .ToList();

        deltas.ShouldBe(["Mer", "haba"]);
    }

    [Fact]
    public async Task Calistirma_hatasi_kaydedilir_ve_yeniden_atilir()
    {
        var store = new InMemoryRunStore();
        var client = new FakeChatClient(_ => throw new InvalidOperationException("model patladi"));
        var agent = CreateAgent(store, client);

        await Should.ThrowAsync<InvalidOperationException>(async () => await agent.RunAsync("merhaba"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Message.ShouldBe("model patladi");
        run.Error.Type.ShouldBe("System.InvalidOperationException");
    }

    [Fact]
    public async Task Depo_hatasi_calistirmayi_kesmez()
    {
        // Gozlemlenebilirlik, islevselligi bozmamalidir.
        var store = new ThrowingRunStore();
        var agent = CreateAgent(store, new FakeChatClient());

        var response = await agent.RunAsync("merhaba");

        response.Text.ShouldBe("tamam");
        store.StartAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task Kayit_kapaliyken_hicbir_olay_yazilmaz()
    {
        var store = new InMemoryRunStore();
        var agent = CreateAgent(store, new FakeChatClient(), new AgentPrismRunRecordingOptions { Enabled = false });

        await agent.RunAsync("merhaba");

        (await store.QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sikistirma_tetiklenince_HistoryCompacted_olayi_yazilir()
    {
        var store = new InMemoryRunStore();
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.SlidingWindow,
                TriggerMessages = 2,
                MinimumPreservedTurns = 1,
            },
        };

        var agent = CreateAgent(store, compiler, definition);

        var longConversation = Enumerable.Range(0, 12)
            .Select(static i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"mesaj {i}"))
            .ToList();

        await agent.RunAsync(longConversation);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        events.ShouldContain(static e => e.Type == RunEventType.HistoryCompacted);
    }

    [Fact]
    public async Task Ozetleme_token_kullanimi_calistirma_toplamina_eklenir()
    {
        var store = new InMemoryRunStore();

        var mainClient = new FakeChatClient();
        var summarizerUsage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 };
        var summarizerClient = new FakeChatClient(
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ozet")) { Usage = summarizerUsage });

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(
                new FakeModelProvider(mainClient, name: "fake"),
                new FakeModelProvider(summarizerClient, name: "summarizer")),
            TestData.Registry());

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.Summarization,
                TriggerMessages = 2,
                MinimumPreservedGroups = 1,
                SummarizationModel = new ModelBinding { Provider = "summarizer", Model = "summarizer-model" },
            },
        };

        var agent = CreateAgent(store, compiler, definition);

        var longConversation = Enumerable.Range(0, 12)
            .Select(static i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"mesaj {i}"))
            .ToList();

        await agent.RunAsync(longConversation);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        // Ozetleme cagrisi agent'in kendi AgentResponse'undan tamamen ayri bir
        // yan-kanal cagrisidir; birlesim olmasa bu token'lar hicbir yere kaydolmaz.
        run.Usage.ShouldNotBeNull();
        (run.Usage!.InputTokens >= 100).ShouldBeTrue();
        (run.Usage.OutputTokens >= 20).ShouldBeTrue();
    }

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        FakeChatClient client,
        AgentPrismRunRecordingOptions? options = null)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return CreateAgent(store, compiler, TestData.Definition(), options);
    }

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        AgentDefinitionCompiler compiler,
        AgentDefinition definition,
        AgentPrismRunRecordingOptions? options = null)
        => new(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            options ?? new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

    private static async Task<List<RunEvent>> ReadEventsAsync(IRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();
        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }

    private static async Task<List<RunEventType>> ReadEventTypesAsync(IRunStore store, Guid runId)
        => (await ReadEventsAsync(store, runId)).Select(static e => e.Type).ToList();

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    private sealed class ThrowingRunStore : IRunStore
    {
        public int StartAttempts { get; private set; }

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
        {
            StartAttempts++;
            throw new InvalidOperationException("depo erisilemez");
        }

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");
    }
}
