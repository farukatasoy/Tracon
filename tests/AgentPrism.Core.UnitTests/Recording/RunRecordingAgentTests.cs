using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Recording;

public sealed class RunRecordingAgentTests
{
    [Fact]
    public async Task Successful_run_writes_the_event_sequence()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store, new FakeChatClient());

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.CompletedAt.ShouldNotBeNull();

        var types = await ReadEventTypesAsync(store, run.Id);
        types[0].ShouldBe(RunEventType.RunStarted);
        types[^1].ShouldBe(RunEventType.RunCompleted);
    }

    [Fact]
    public async Task Document_channel_message_is_recorded_separately_and_never_mistaken_for_the_query()
    {
        // 🚨 A document channel message is ChatRole.User too and, when
        // documents are given, precedes the actual query in the message
        // list - RunStarted.Text (the ONLY persisted source for eval-case
        // promotion, phase 45) must still capture the REAL query, not the
        // document's own content.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store, new FakeChatClient());

        var messages = new List<ChatMessage>
        {
            DocumentChannelMessageBuilder.Build(new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." }),
            new ChatMessage(ChatRole.User, "Summarize the attached policy."),
        };

        await agent.RunAsync(messages);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        events[0].Type.ShouldBe(RunEventType.RunStarted);
        events[0].Text.ShouldBe("Summarize the attached policy.");

        var documentEvent = events.Where(static e => e.Type == RunEventType.DocumentAttached).ShouldHaveSingleItem();
        documentEvent.Text.ShouldBe("policy.md");
    }

    [Fact]
    public async Task Event_sequence_numbers_increase_without_gaps()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store, new FakeChatClient());

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var sequences = new List<long>();
        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe(Enumerable.Range(0, sequences.Count).Select(static i => (long)i).ToList());
    }

    [Fact]
    public async Task Tool_calls_turn_into_events()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var client = new FakeChatClient(_ => new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_order", new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = 42 })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "in transit")]),
            new ChatMessage(ChatRole.Assistant, "Your order is in transit."),
        ]));

        var agent = CreateAgent(store, client);

        await agent.RunAsync("where is my order");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        var invoking = events.Where(static e => e.Type == RunEventType.ToolInvoking).ShouldHaveSingleItem();
        invoking.ToolName.ShouldBe("get_order");
        invoking.ToolCallId.ShouldBe("call-1");
        invoking.Payload!.ShouldContain("id=42");

        var invoked = events.Where(static e => e.Type == RunEventType.ToolInvoked).ShouldHaveSingleItem();
        invoked.ToolCallId.ShouldBe("call-1");
        invoked.Payload.ShouldBe("in transit");
    }

    [Fact]
    public async Task Streaming_run_writes_text_deltas()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "He"),
            new ChatResponseUpdate(ChatRole.Assistant, "llo"),
        ]);

        var agent = CreateAgent(store, client);

        var received = new List<string>();
        await foreach (var update in agent.RunStreamingAsync("hi"))
        {
            received.Add(update.Text);
        }

        received.ShouldBe(["He", "llo"]);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.IsStreaming.ShouldBeTrue();

        var deltas = (await ReadEventsAsync(store, run.Id))
            .Where(static e => e.Type == RunEventType.MessageDelta)
            .Select(static e => e.Text)
            .ToList();

        deltas.ShouldBe(["He", "llo"]);
    }

    [Fact]
    public async Task Run_failure_is_recorded_and_rethrown()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var client = new FakeChatClient(_ => throw new InvalidOperationException("model crashed"));
        var agent = CreateAgent(store, client);

        await Should.ThrowAsync<InvalidOperationException>(async () => await agent.RunAsync("hello"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Message.ShouldBe("model crashed");
        run.Error.Type.ShouldBe("System.InvalidOperationException");
    }

    [Fact]
    public async Task Error_classifier_is_never_called_on_a_successful_run()
    {
        // Classification is on the hot path and runs only on the error path;
        // it must not allocate on a successful run (docs/arsiv/fazlar/44-HATA-SINIFLANDIRMA.md).
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var spy = new SpyRunErrorClassifier();
        var agent = CreateAgent(store, new FakeChatClient(), spy);

        await agent.RunAsync("hello");

        spy.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Classifier_result_is_written_to_the_record_on_a_failed_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var spy = new SpyRunErrorClassifier();
        var client = new FakeChatClient(_ => throw new InvalidOperationException("model crashed"));
        var agent = CreateAgent(store, client, spy);

        await Should.ThrowAsync<InvalidOperationException>(async () => await agent.RunAsync("hello"));

        spy.CallCount.ShouldBe(1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Error.ShouldNotBeNull();
        run.Error!.Class.ShouldBe(RunErrorClass.Unknown);
        run.Error.Fingerprint.ShouldBe("spy");
    }

    [Fact]
    public async Task Content_filtered_empty_response_is_recorded_as_content_filtered()
    {
        // A silent empty response is the hardest case to debug: the user sees an
        // empty reply and the record leaves no trace. The record type must
        // therefore be machine-readable, so alert rules can rely on it instead
        // of guessing from the message text.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var client = new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.ContentFilter,
        });

        var agent = CreateAgent(store, client);

        await Should.ThrowAsync<AgentPrismContentFilteredException>(async () => await agent.RunAsync("hi"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Type.ShouldBe(AgentPrismContentFilteredException.ContentFilteredErrorType);
    }

    [Fact]
    public async Task Streaming_content_filter_is_also_recorded_as_content_filtered()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { FinishReason = ChatFinishReason.ContentFilter },
        ]);

        var agent = CreateAgent(store, client);

        await Should.ThrowAsync<AgentPrismContentFilteredException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("hi"))
            {
                // Frames are consumed; the error arrives at the end of the stream.
            }
        });

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe(AgentPrismContentFilteredException.ContentFilteredErrorType);
    }

    [Fact]
    public async Task Store_failure_does_not_interrupt_the_run()
    {
        // Observability must not break functionality.
        var store = new ThrowingRunStore();
        var agent = CreateAgent(store, new FakeChatClient());

        var response = await agent.RunAsync("hello");

        response.Text.ShouldBe("tamam"); // must match FakeChatClient's default reply text
        store.StartAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task No_event_is_written_while_recording_is_disabled()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store, new FakeChatClient(), new AgentPrismRunRecordingOptions { Enabled = false });

        await agent.RunAsync("hello");

        (await store.QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    [Fact]
    public async Task HistoryCompacted_event_is_written_when_compaction_triggers()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
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
            .Select(static i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"message {i}"))
            .ToList();

        await agent.RunAsync(longConversation);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var events = await ReadEventsAsync(store, run.Id);

        events.ShouldContain(static e => e.Type == RunEventType.HistoryCompacted);
    }

    [Fact]
    public async Task Summarization_token_usage_is_added_to_the_run_total()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var mainClient = new FakeChatClient();
        var summarizerUsage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 };
        var summarizerClient = new FakeChatClient(
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")) { Usage = summarizerUsage });

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
            .Select(static i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"message {i}"))
            .ToList();

        await agent.RunAsync(longConversation);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        // The summarization call is a side-channel call, entirely separate from the
        // agent's own AgentResponse; without merging, these tokens would go unrecorded.
        run.Usage.ShouldNotBeNull();
        (run.Usage!.InputTokens >= 100).ShouldBeTrue();
        (run.Usage.OutputTokens >= 20).ShouldBeTrue();
    }

    /// <summary>
    /// Phase 20: verifies that the pricing resolver is actually called and its result is
    /// written to the store. Unlike the other tests, this one constructs
    /// <see cref="RunRecordingAgent"/> WITH modelId/modelProvider/pricingResolver — this
    /// path is what caught a bug recorded in KARARLAR.md (RunEventWriter.CompleteAsync
    /// accepted the new `cost` parameter but never wrote it to RunCompletion).
    /// </summary>
    [Fact]
    public async Task Cost_pipeline_is_computed_and_written_end_to_end()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var usage = new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 500_000, TotalTokenCount = 1_500_000 };
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage });

        var pricedProvider = new FakeModelProvider(client, name: "fake", models:
        [
            new ModelDescriptor { Name = "priced-model", InputCostPerMillionTokens = 2m, OutputCostPerMillionTokens = 4m },
        ]);

        var compiler = new AgentDefinitionCompiler(TestData.Providers(pricedProvider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([pricedProvider]),
            Options.Create(new AgentPrismOptions()));

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Cost.ShouldNotBeNull();
        run.Cost.Source.ShouldBe(PricingSource.Catalog);
        run.Cost.InputCost.ShouldBe(2m);
        run.Cost.OutputCost.ShouldBe(2m);
    }

    /// <summary>Same pipeline, but with no price defined for the model: cost must be null and source must be Unknown.</summary>
    [Fact]
    public async Task Cost_pipeline_writes_unknown_for_an_unpriced_model()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());

        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 10, TotalTokenCount = 20 };
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage });

        var provider = new FakeModelProvider(client, name: "fake", models: [new ModelDescriptor { Name = "unpriced-model" }]);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "unpriced-model" } };

        var resolver = new RunPricingResolver(new ModelProviderRegistry([provider]), Options.Create(new AgentPrismOptions()));

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            modelId: "unpriced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Cost.ShouldNotBeNull();
        run.Cost.Source.ShouldBe(PricingSource.Unknown);
        run.Cost.InputCost.ShouldBeNull();
        run.Cost.OutputCost.ShouldBeNull();
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
        FakeChatClient client,
        IRunErrorClassifier errorClassifier)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            errorClassifier: errorClassifier);
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
            throw new InvalidOperationException("store unavailable");
        }

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask UpdateRunCostAsync(
            Guid runId,
            RunCost? cost,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");
    }
}
