using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Verifies <see cref="TruncatingAIFunction"/>: a result under the byte limit
/// passes through untouched, a result over the limit is wrapped in a JSON
/// envelope whose own size never exceeds the limit, a multi-byte character is
/// never cut in half, and the trimming is reported as a
/// <see cref="RunEventType.ToolOutputTruncated"/> run event when a run scope
/// is present.
/// </summary>
/// <remarks>
/// <see cref="RawResultFunction"/> returns the raw CLR value from
/// <c>InvokeCoreAsync</c> directly — a reflection-registered tool
/// (<c>AddToolsFrom</c>/<c>AddTool(AIFunction)</c>) built through
/// <see cref="AIFunctionFactory"/>, whose default result marshaling boxes
/// every return value (including a plain <see langword="string"/> or
/// <see langword="null"/>) into a <see cref="JsonElement"/>. The generator's
/// own emitted wrapper (Phase 102) matches this raw form only for
/// <see langword="string"/>/primitive returns; a complex return type is
/// emitted as a <see cref="JsonElement"/> too, via the caller's own
/// <c>JsonSerializerContext</c> (<c>TRC0008</c>). Using the raw form here
/// keeps each test's assertion about the exact CLR value under test.
/// </remarks>
public sealed class TruncatingAIFunctionTests
{
    [Fact]
    public async Task A_result_within_the_limit_passes_through_unchanged()
    {
        var inner = new RawResultFunction("get_order", "short");
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 1024);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldBe("short");
    }

    [Fact]
    public async Task Null_result_uses_the_explicit_canonical_representation()
    {
        var inner = new RawResultFunction("silent_tool", result: null);
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldBe("null");
    }

    [Fact]
    public async Task Empty_string_result_passes_through_unchanged()
    {
        var inner = new RawResultFunction("empty_tool", string.Empty);
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task A_result_over_the_limit_is_wrapped_in_an_envelope()
    {
        var inner = new RawResultFunction("big_report", new string('a', 5000));
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 200);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        var envelope = ParseEnvelope(result);

        envelope.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        envelope.GetProperty("omittedBytes").GetInt32().ShouldBeGreaterThan(0);
        envelope.GetProperty("content").GetString()!.Length.ShouldBeLessThan(5000);
    }

    [Fact]
    public async Task The_finished_envelope_never_exceeds_the_byte_limit()
    {
        // Rich in quotes and backslashes: escaping expands these bytes, so a
        // bare byte-trim-then-escape approach would overflow the limit here.
        var quoteHeavy = string.Concat(Enumerable.Repeat("\"\\say \\\"hi\\\"\\", 400));
        var inner = new RawResultFunction("quote_tool", quoteHeavy);
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 256);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        var text = (string)result!;
        Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(256);

        // Must still be well-formed JSON despite the pathological content.
        Should.NotThrow(() => JsonDocument.Parse(text));
    }

    [Fact]
    public async Task A_multi_byte_character_is_never_cut_in_half()
    {
        // CJK text: every character is 3 bytes in UTF-8.
        var inner = new RawResultFunction("cjk_tool", string.Concat(Enumerable.Repeat("你好世界 ", 200)));
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 100);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        var envelope = ParseEnvelope(result);
        var content = envelope.GetProperty("content").GetString()!;

        // The content round-trips as valid UTF-8/UTF-16 with no replacement
        // characters or lone surrogates — proof no character was cut in half.
        content.ShouldNotContain('�');
    }

    [Fact]
    public async Task An_unsupported_raw_clr_result_fails_closed_without_using_ToString()
    {
        // A tool source generator's emitted wrapper returns a raw CLR object
        // directly; the default Object.ToString() would return a bare type
        // name, not the JSON a provider actually sends. Measuring THAT would
        // either wrongly skip a huge result or wrap a meaningless type name
        // in the envelope — so this class does not attempt it at all.
        var payload = new LongToString();
        var inner = new RawResultFunction("object_tool", payload);
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldBe(ToolResultText.UnsupportedResultText);
        result.ShouldNotBe(payload.ToString());
    }

    [Fact]
    public async Task A_JsonElement_result_over_the_limit_is_truncated_via_its_raw_text()
    {
        // The shape AIFunctionFactory boxes every non-AIContent return value
        // into. GetRawText() is exact — the same bytes a provider serializes
        // to the wire — unlike ToString(), which for a String-kind element
        // omits the surrounding quotes.
        var element = JsonDocument.Parse(
            $$"""{"orderId":"ORD-1","note":"{{new string('a', 5000)}}"}""").RootElement;

        var inner = new RawResultFunction("json_tool", element);
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 200);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        var envelope = ParseEnvelope(result);
        envelope.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        envelope.GetProperty("omittedBytes").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_non_string_result_within_the_limit_uses_its_canonical_text()
    {
        var inner = new RawResultFunction("counting_tool", 42);
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 1024);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldBe("42");
    }

    [Fact]
    public void Constructor_throws_for_a_zero_or_negative_limit()
    {
        var inner = new RawResultFunction("get_order", "ok");

        Should.Throw<ArgumentOutOfRangeException>(() => new TruncatingAIFunction(inner, maxOutputBytes: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => new TruncatingAIFunction(inner, maxOutputBytes: -1));
    }

    [Fact]
    public void Constructor_throws_for_a_positive_limit_too_small_to_ever_hold_an_envelope()
    {
        // 🚨 Below MinimumEnvelopeBytes, no input could ever be represented by
        // a compliant envelope — a silent budget violation, not a crash, is
        // the wrong failure mode; this must be rejected at construction.
        var inner = new RawResultFunction("get_order", "ok");

        Should.Throw<ArgumentOutOfRangeException>(
            () => new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes - 1));

        Should.NotThrow(() => new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes));
    }

    [Fact]
    public async Task Even_the_smallest_accepted_limit_never_produces_an_oversized_envelope()
    {
        var inner = new RawResultFunction("big_report", new string('a', 5000));
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        Encoding.UTF8.GetByteCount((string)result!).ShouldBeLessThanOrEqualTo(TruncatingAIFunction.MinimumEnvelopeBytes);
    }

    [Fact]
    public async Task A_call_that_is_canceled_before_returning_is_never_truncated()
    {
        var inner = new RawResultFunction(
            "cooperative_tool",
            invoke: (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<object?>("unreachable");
            });

        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: TruncatingAIFunction.MinimumEnvelopeBytes);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cts.Token));
    }

    [Fact]
    public async Task Truncation_is_recorded_as_a_run_event_with_the_tool_name_and_omitted_bytes()
    {
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = writer.RunId,
            RootRunId = writer.RunId,
            Writer = writer,
        });

        try
        {
            var inner = new RawResultFunction("big_report", new string('x', 500));
            var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 64);

            await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var events = new List<RunEvent>();
        await foreach (var runEvent in store.ReadEventsAsync(writer.RunId))
        {
            events.Add(runEvent);
        }

        var truncation = events.Where(static e => e.Type == RunEventType.ToolOutputTruncated).ShouldHaveSingleItem();
        truncation.ToolName.ShouldBe("big_report");
        truncation.Text.ShouldNotBeNull().ShouldContain("64", Case.Sensitive);
        truncation.Payload.ShouldNotBeNull().ShouldContain("\"maxOutputBytes\":64", Case.Sensitive);
    }

    [Fact]
    public async Task Truncation_still_happens_when_no_run_scope_is_present()
    {
        // Observability functionality does NOT break the tool: with no scope
        // there is nowhere to write the event, but the trimmed result is
        // still returned instead of the unbounded one.
        TraconRunContext.SetCurrent(null);

        var inner = new RawResultFunction("big_report", new string('x', 500));
        var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 64);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        var envelope = ParseEnvelope(result);
        envelope.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_store_failure_while_recording_the_truncation_event_does_not_change_the_returned_result()
    {
        // Observability functionality does NOT break the tool: the trimmed
        // result was already computed successfully before this side effect
        // runs, and a failure recording it must never cost that result.
        var writer = new RunEventWriter(
            new ThrowingOnAppendRunStore(),
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            TraconId.NewId(),
            metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = writer.RunId,
            RootRunId = writer.RunId,
            Writer = writer,
        });

        object? result;

        try
        {
            var inner = new RawResultFunction("big_report", new string('x', 500));
            var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 64);

            result = await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var envelope = ParseEnvelope(result);
        envelope.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task An_untouched_result_writes_no_truncation_event()
    {
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = writer.RunId,
            RootRunId = writer.RunId,
            Writer = writer,
        });

        try
        {
            var inner = new RawResultFunction("get_order", "short");
            var wrapped = new TruncatingAIFunction(inner, maxOutputBytes: 1024);

            await wrapped.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var events = new List<RunEvent>();
        await foreach (var runEvent in store.ReadEventsAsync(writer.RunId))
        {
            events.Add(runEvent);
        }

        events.ShouldNotContain(static e => e.Type == RunEventType.ToolOutputTruncated);
    }

    private static JsonElement ParseEnvelope(object? result)
    {
        result.ShouldBeOfType<string>();

        return JsonDocument.Parse((string)result!).RootElement;
    }

    private sealed class LongToString
    {
        public override string ToString() => new string('z', 5000);
    }

    /// <summary>
    /// A store whose <see cref="StartRunAsync"/> succeeds (so the writer is
    /// never disabled) but whose <see cref="AppendEventAsync"/> always fails
    /// — isolates the "recording the truncation event fails" case from the
    /// "the writer was never usable to begin with" case.
    /// </summary>
    private sealed class ThrowingOnAppendRunStore : IRunStore
    {
        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => new(new RunRecord
            {
                Id = info.RunId,
                AgentName = info.AgentName,
                Status = RunStatus.Running,
                StartedAt = info.StartedAt,
            });

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// A minimal <see cref="AIFunction"/> that returns a fixed raw CLR value
    /// (or runs a custom body) from <c>InvokeCoreAsync</c>, matching what the
    /// tool source generator emits — no <see cref="JsonElement"/> boxing.
    /// </summary>
    private sealed class RawResultFunction : AIFunction
    {
        private static readonly JsonElement EmptySchema =
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        private readonly Func<AIFunctionArguments, CancellationToken, ValueTask<object?>> _invoke;

        public RawResultFunction(string name, object? result)
            : this(name, (_, _) => new ValueTask<object?>(result))
        {
        }

        public RawResultFunction(string name, Func<AIFunctionArguments, CancellationToken, ValueTask<object?>> invoke)
        {
            Name = name;
            _invoke = invoke;
        }

        public override string Name { get; }

        public override string Description => string.Empty;

        public override JsonElement JsonSchema => EmptySchema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
            => _invoke(arguments, cancellationToken);
    }
}
