using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Unit-level coverage of <see cref="StructuredResponseValidatingAgent"/>: the
/// gating logic, the built-in well-formedness check, and the fail-closed rule
/// (docs/131, 131.1-131.3). HTTP/DI/run-record behavior is covered separately
/// in <c>Tracon.AspNetCore.FunctionalTests</c> — see
/// <c>.agents/ortak/test-seviyeleri.md</c>.
/// </summary>
public sealed class StructuredResponseValidatingAgentTests
{
    private static AgentDescriptor Descriptor(AgentResponseFormat? format) => new()
    {
        Name = "structured-agent",
        Origin = AgentDefinitionOrigin.Code,
        SourceName = "test",
        Model = new ModelBinding { Provider = "fake", Model = "fake-model", ResponseFormat = format },
    };

    private static StructuredResponseValidatingAgent Wrap(
        FakeChatClient chatClient,
        AgentResponseFormat? format,
        IStructuredResponseValidator? validator = null,
        bool enabled = true,
        int maxRepairAttempts = 0)
        => new(
            chatClient.AsAIAgent(),
            Descriptor(format),
            validator ?? new StaticValidator(StructuredResponseValidationResult.Valid),
            new TraconStructuredResponseOptions { Enabled = enabled, MaxRepairAttempts = maxRepairAttempts },
            NullLogger<StructuredResponseValidatingAgent>.Instance);

    [Fact]
    public async Task Disabled_option_lets_an_invalid_response_through_unchanged()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var validator = new StaticValidator(StructuredResponseValidationResult.Invalid("would reject"));
        var wrapped = Wrap(chatClient, Json(), validator, enabled: false);

        var response = await wrapped.RunAsync("hello");

        response.Text.ShouldBe("not json");
        validator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task No_response_format_lets_an_invalid_response_through_unchanged()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var wrapped = Wrap(chatClient, format: null);

        var response = await wrapped.RunAsync("hello");

        response.Text.ShouldBe("not json");
    }

    [Fact]
    public async Task Text_format_is_never_validated()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var validator = new StaticValidator(StructuredResponseValidationResult.Invalid("would reject"));
        var wrapped = Wrap(chatClient, new AgentResponseFormat { Kind = AgentResponseFormatKind.Text }, validator);

        var response = await wrapped.RunAsync("hello");

        response.Text.ShouldBe("not json");
        validator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Valid_json_passes_through_and_reaches_the_validator()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"ok\":true}")));
        var validator = new StaticValidator(StructuredResponseValidationResult.Valid);
        var wrapped = Wrap(chatClient, Json(), validator);

        var response = await wrapped.RunAsync("hello");

        response.Text.ShouldBe("{\"ok\":true}");
        validator.CallCount.ShouldBe(1);
        validator.LastContext!.ResponseText.ShouldBe("{\"ok\":true}");
        validator.LastContext.Kind.ShouldBe(AgentResponseFormatKind.Json);
    }

    [Fact]
    public async Task Empty_response_is_rejected_without_calling_the_validator()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
        var validator = new StaticValidator(StructuredResponseValidationResult.Valid);
        var wrapped = Wrap(chatClient, Json(), validator);

        var exception = await Should.ThrowAsync<TraconStructuredResponseException>(
            async () => await wrapped.RunAsync("hello"));

        exception.Message.ShouldBe("The response is empty.");
        exception.ErrorType.ShouldBe(TraconStructuredResponseException.StructuredResponseInvalidErrorType);
        validator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Malformed_json_is_rejected_without_calling_the_validator()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{not json")));
        var validator = new StaticValidator(StructuredResponseValidationResult.Valid);
        var wrapped = Wrap(chatClient, Json(), validator);

        var exception = await Should.ThrowAsync<TraconStructuredResponseException>(
            async () => await wrapped.RunAsync("hello"));

        exception.Message.ShouldBe("The response is not valid JSON.");
        validator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Consumer_validator_rejection_throws_with_its_own_reason()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"score\":999}")));
        var validator = new StaticValidator(StructuredResponseValidationResult.Invalid("'score' must be between 0 and 100."));
        var wrapped = Wrap(chatClient, JsonSchema(), validator);

        var exception = await Should.ThrowAsync<TraconStructuredResponseException>(
            async () => await wrapped.RunAsync("hello"));

        exception.Message.ShouldBe("'score' must be between 0 and 100.");
    }

    [Fact]
    public async Task A_throwing_validator_rejects_fail_closed_instead_of_propagating()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"ok\":true}")));
        var wrapped = Wrap(chatClient, Json(), new ThrowingValidator());

        var exception = await Should.ThrowAsync<TraconStructuredResponseException>(
            async () => await wrapped.RunAsync("hello"));

        exception.ShouldNotBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Streaming_forwards_every_update_before_validating()
    {
        var updates = new List<ChatResponseUpdate>
        {
            new(ChatRole.Assistant, "{\"ok\""),
            new(ChatRole.Assistant, ":false}"),
        };
        var chatClient = new FakeChatClient(streamingUpdates: updates);
        var validator = new StaticValidator(StructuredResponseValidationResult.Invalid("ok must be true"));
        var wrapped = Wrap(chatClient, Json(), validator);

        var seen = new List<string>();

        var exception = await Should.ThrowAsync<TraconStructuredResponseException>(async () =>
        {
            await foreach (var update in wrapped.RunStreamingAsync("hello"))
            {
                seen.Add(update.Text);
            }
        });

        // Every chunk reached the caller BEFORE the rejection - content
        // already streamed to the client cannot be un-sent (docs/131 §131.5).
        seen.ShouldBe(["{\"ok\"", ":false}"]);
        exception.Message.ShouldBe("ok must be true");
        validator.LastContext!.ResponseText.ShouldBe("{\"ok\":false}");
    }

    [Fact]
    public async Task A_canceled_run_propagates_cancellation_not_a_validation_error()
    {
        var chatClient = new FakeChatClient(_ => throw new OperationCanceledException());
        var wrapped = Wrap(chatClient, Json());

        await Should.ThrowAsync<OperationCanceledException>(async () => await wrapped.RunAsync("hello"));
    }

    [Fact]
    public async Task Rejection_writes_a_StructuredResponseRejected_event_on_the_ambient_run_scope()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var wrapped = Wrap(chatClient, JsonSchema("order-schema"), new StaticValidator(StructuredResponseValidationResult.Valid));

        var runStore = new InMemoryRunStore();
        var runId = Guid.NewGuid();

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "structured-agent",
            StartedAt = DateTimeOffset.UtcNow,
        });

        var writer = new RunEventWriter(runStore, new TraconRunRecordingOptions(), NullLogger.Instance, runId);

        TraconRunContext.SetCurrent(new AgentRunScope { RunId = runId, RootRunId = runId, Writer = writer });

        try
        {
            await Should.ThrowAsync<TraconStructuredResponseException>(async () => await wrapped.RunAsync("hello"));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var events = await CollectAsync(runStore.ReadEventsAsync(runId));
        var rejected = events.ShouldHaveSingleItem();

        rejected.Type.ShouldBe(RunEventType.StructuredResponseRejected);
        rejected.Text.ShouldBe("The response is not valid JSON.");
        rejected.Payload.ShouldNotBeNull();
        rejected.Payload.ShouldContain("\"kind\":\"JsonSchema\"", Case.Sensitive);
        rejected.Payload.ShouldContain("\"schemaName\":\"order-schema\"", Case.Sensitive);
        rejected.Payload.ShouldContain("\"reason\":\"The response is not valid JSON.\"", Case.Sensitive);

        // The raw response text never reaches the event.
        rejected.Payload.ShouldNotContain("not json", Case.Insensitive);
    }

    /// <summary>A store whose writes throw. Proves observability failures never block the reject decision.</summary>
    [Fact]
    public async Task A_store_that_cannot_write_the_event_still_lets_the_run_fail()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
        var wrapped = Wrap(chatClient, Json(), new StaticValidator(StructuredResponseValidationResult.Valid));

        var runId = Guid.NewGuid();
        var writer = new RunEventWriter(
            new ThrowingAppendStore(), new TraconRunRecordingOptions(), NullLogger.Instance, runId);

        TraconRunContext.SetCurrent(new AgentRunScope { RunId = runId, RootRunId = runId, Writer = writer });

        try
        {
            var exception = await Should.ThrowAsync<TraconStructuredResponseException>(
                async () => await wrapped.RunAsync("hello"));

            exception.Message.ShouldBe("The response is empty.");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task MaxRepairAttempts_zero_behaves_exactly_like_no_repair_a_single_call_that_throws()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 0);

        await Should.ThrowAsync<TraconStructuredResponseException>(async () => await wrapped.RunAsync("hello"));

        chatClient.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_repair_turn_recovers_from_an_invalid_first_response()
    {
        var responses = new Queue<string>(["not json", "{\"ok\":true}"]);
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, responses.Dequeue())));
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 1);

        var response = await wrapped.RunAsync("hello");

        response.Text.ShouldBe("{\"ok\":true}");
        chatClient.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Repair_attempts_are_capped_at_MaxRepairAttempts_then_the_run_still_fails()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 2);

        var exception = await Should.ThrowAsync<TraconStructuredResponseException>(
            async () => await wrapped.RunAsync("hello"));

        // MaxRepairAttempts=2 permits at most THREE model calls: the original turn plus two repairs.
        chatClient.CallCount.ShouldBe(3);
        exception.Message.ShouldBe("The response is not valid JSON.");
    }

    [Fact]
    public async Task A_repair_turn_carries_the_original_message_the_invalid_response_and_a_correction()
    {
        var responses = new Queue<string>(["not json", "{\"ok\":true}"]);
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, responses.Dequeue())));
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 1);

        await wrapped.RunAsync("hello");

        chatClient.LastRequest.Count.ShouldBe(3);
        chatClient.LastRequest[0].Role.ShouldBe(ChatRole.User);
        chatClient.LastRequest[0].Text.ShouldBe("hello");
        chatClient.LastRequest[1].Role.ShouldBe(ChatRole.Assistant);
        chatClient.LastRequest[1].Text.ShouldBe("not json");
        chatClient.LastRequest[2].Role.ShouldBe(ChatRole.User);
        chatClient.LastRequest[2].Text.ShouldContain("not valid JSON", Case.Sensitive);
    }

    [Fact]
    public async Task Streaming_never_repairs_even_when_MaxRepairAttempts_is_positive()
    {
        var updates = new List<ChatResponseUpdate> { new(ChatRole.Assistant, "not json") };
        var chatClient = new FakeChatClient(streamingUpdates: updates);
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 2);

        await Should.ThrowAsync<TraconStructuredResponseException>(async () =>
        {
            await foreach (var _ in wrapped.RunStreamingAsync("hello"))
            {
            }
        });

        chatClient.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Repair_events_carry_1_based_monotonic_attempt_and_maxAttempts_numbers()
    {
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json")));
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 2);

        var runStore = new InMemoryRunStore();
        var runId = Guid.NewGuid();

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "structured-agent",
            StartedAt = DateTimeOffset.UtcNow,
        });

        var writer = new RunEventWriter(runStore, new TraconRunRecordingOptions(), NullLogger.Instance, runId);

        TraconRunContext.SetCurrent(new AgentRunScope { RunId = runId, RootRunId = runId, Writer = writer });

        try
        {
            await Should.ThrowAsync<TraconStructuredResponseException>(async () => await wrapped.RunAsync("hello"));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var events = await CollectAsync(runStore.ReadEventsAsync(runId));

        var rejected = events.Where(static e => e.Type == RunEventType.StructuredResponseRejected).ToList();
        var repairAttempted = events.Where(static e => e.Type == RunEventType.StructuredResponseRepairAttempted).ToList();

        rejected.Count.ShouldBe(3);
        rejected[0].Payload!.ShouldContain("\"attempt\":1", Case.Sensitive);
        rejected[0].Payload!.ShouldContain("\"maxAttempts\":3", Case.Sensitive);
        rejected[1].Payload!.ShouldContain("\"attempt\":2", Case.Sensitive);
        rejected[2].Payload!.ShouldContain("\"attempt\":3", Case.Sensitive);

        repairAttempted.Count.ShouldBe(2);
        repairAttempted[0].Text.ShouldBe("2/3");
        repairAttempted[0].Payload!.ShouldContain("\"attempt\":2", Case.Sensitive);
        repairAttempted[1].Text.ShouldBe("3/3");
    }

    [Fact]
    public async Task A_discarded_attempts_usage_folds_into_the_side_channel_and_the_returned_attempts_does_not()
    {
        var responses = new Queue<string>(["not json", "{\"ok\":true}"]);
        var chatClient = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, responses.Dequeue()))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
        });
        var wrapped = Wrap(chatClient, Json(), maxRepairAttempts: 1);

        var extraUsage = new SideChannelUsageAccumulator();

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            ExtraUsage = extraUsage,
        });

        try
        {
            await wrapped.RunAsync("hello");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        // Only the FIRST (discarded) response's usage was folded in - the second
        // (returned) response's usage is counted through the normal path instead.
        var usage = extraUsage.ToRunUsage();
        usage.ShouldNotBeNull();
        usage!.InputTokens.ShouldBe(10);
        usage.OutputTokens.ShouldBe(5);
    }

    private static AgentResponseFormat Json() => new() { Kind = AgentResponseFormatKind.Json };

    private static AgentResponseFormat JsonSchema(string? schemaName = null) => new()
    {
        Kind = AgentResponseFormatKind.JsonSchema,
        Schema = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
        SchemaName = schemaName,
    };

    private static async Task<List<RunEvent>> CollectAsync(IAsyncEnumerable<RunEvent> events)
    {
        var list = new List<RunEvent>();

        await foreach (var runEvent in events)
        {
            list.Add(runEvent);
        }

        return list;
    }

    private sealed class StaticValidator(StructuredResponseValidationResult result) : IStructuredResponseValidator
    {
        public int CallCount { get; private set; }

        public StructuredResponseValidationContext? LastContext { get; private set; }

        public ValueTask<StructuredResponseValidationResult> ValidateAsync(
            StructuredResponseValidationContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastContext = context;

            return new ValueTask<StructuredResponseValidationResult>(result);
        }
    }

    private sealed class ThrowingValidator : IStructuredResponseValidator
    {
        public ValueTask<StructuredResponseValidationResult> ValidateAsync(
            StructuredResponseValidationContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    /// <summary>Delegates everything to a real in-memory store except the event append, which always throws.</summary>
    private sealed class ThrowingAppendStore : IRunStore
    {
        private readonly InMemoryRunStore _inner = new();

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => _inner.StartRunAsync(info, cancellationToken);

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => _inner.CompleteRunAsync(completion, cancellationToken);

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => _inner.GetRunAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryRunsAsync(query, cancellationToken);

        public ValueTask<RunStatistics> GetStatisticsAsync(
            RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => _inner.GetStatisticsAsync(query, cancellationToken);

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(
            Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => _inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

        public ValueTask RecordToolInvocationAsync(
            ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => _inner.RecordToolInvocationAsync(invocation, cancellationToken);

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
            Guid runId, CancellationToken cancellationToken = default)
            => _inner.ListToolInvocationsAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
            ToolUsageQuery query, CancellationToken cancellationToken = default)
            => _inner.GetToolUsageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
            ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => _inner.GetExperimentResultsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
            RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => _inner.GetTimeSeriesAsync(query, cancellationToken);

        public ValueTask UpdateRunCostAsync(
            Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default)
            => _inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

        public ValueTask TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
            => _inner.TouchHeartbeatAsync(runIds, at, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
            => _inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken);
    }
}
