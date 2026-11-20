namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="ITraceStore"/> contract.
/// </summary>
/// <remarks>
/// Because span ids are derived from W3C ids, writing the
/// same span twice must not produce a duplicate record; every implementation
/// must satisfy this rule.
/// </remarks>
public abstract class TraceStoreContract : TenantIsolationContract<ITraceStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// The write takes the tenant from the batch, the read from
    /// <see cref="ITenantContext"/>; if the two diverge, the trace can never
    /// be found.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        await Store.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = NewTraceId(),
            TenantId = tenantId,
            RunId = runId,
            Spans = [Span(name, parent: null)],
        }, cancellationToken);

        _seededRuns.Add(runId);
        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.GetTraceByRunAsync((Guid)key) is not null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The span store has no listing endpoint, so the read hook is its own
    /// lookup rather than <see cref="CountAsync"/>.
    /// </remarks>
    // 🚨 CountAsync reduces to a loop over the runs seeded so far. On the
    // untouched store the cancellation contract uses, that loop makes no
    // store call at all and the token would never reach the store.
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.GetTraceByRunAsync(TraconId.NewId(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The write goes to a run of its own so the read-back below can name it.
    /// </remarks>
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = TenantA;
        await SeedRunAsync(_cancellationRunId);

        await Store.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = NewTraceId(),
            TenantId = TenantA,
            RunId = _cancellationRunId,
            Spans = [Span("cancelled", parent: null)],
        }, cancellationToken);
    }

    /// <inheritdoc />
    // 🚨 NOT the inherited CountAsync. That one walks _seededRuns, and the
    // seed appends to that list only AFTER its write succeeds -- so after a
    // cancelled write the list is empty, the count is 0, and "left no trace"
    // would be true however the store behaved.
    protected override async ValueTask<bool> WroteAnythingAsync()
    {
        AmbientTenant.TenantId = TenantA;

        return await Store.GetTraceByRunAsync(_cancellationRunId) is not null;
    }

    // The run the cancellation write attaches its spans to.
    private readonly Guid _cancellationRunId = TraconId.NewId();

    /// <inheritdoc />
    /// <remarks>
    /// The span store has no listing endpoint; the count is reduced to how
    /// many of the runs seeded for that tenant are visible.
    /// </remarks>
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        var seen = 0;

        foreach (var runId in _seededRuns)
        {
            if (await ExistsAsync(tenantId, runId))
            {
                seen++;
            }
        }

        return seen;
    }

    // Every run seeded so far; the count walks over these.
    private readonly List<Guid> _seededRuns = [];

    /// <summary>Opens a run for spans to attach to.</summary>
    /// <param name="runId">The run id.</param>
    /// <returns>A completed task.</returns>
    protected abstract ValueTask SeedRunAsync(Guid runId);

    /// <summary>
    /// The tenant spans are written under. The store reads its own tenant
    /// context; if the write and read tenants do not match, the trace can
    /// never be found. This is why the value comes from the same source as
    /// the ambient tenant.
    /// </summary>
    protected string TenantId => AmbientTenant.TenantId;

    [Fact]
    public async Task Span_tree_round_trips()
    {
        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        var root = Span("tracon.run", parent: null);
        var child = Span("chat gpt-5.4-mini", parent: root.Id) with { Kind = TraceSpanKind.Client };

        await Store.WriteSpansAsync(Batch(runId, [root, child]));

        var trace = await Store.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.RunId.ShouldBe(runId);
        trace.Spans.Count.ShouldBe(2);

        var stored = trace.Spans
            .Single(span => string.Equals(span.Name, "chat gpt-5.4-mini", StringComparison.Ordinal));

        stored.ParentId.ShouldBe(root.Id);
        stored.Kind.ShouldBe(TraceSpanKind.Client);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_batch_without_a_run_is_refused_and_nothing_is_written(bool withSpans)
    {
        // The in-memory store accepted it and kept a trace under the empty identifier; a SQL store failed on its foreign key
        // with an error that names no cause. Every store answers the same, and an empty batch is no exception.
        var batch = Batch(Guid.Empty, withSpans ? [Span("tracon.run", parent: null)] : []);

        await Should.ThrowAsync<ArgumentException>(async () => await Store.WriteSpansAsync(batch));

        (await Store.GetTraceByRunAsync(Guid.Empty)).ShouldBeNull();
    }

    [Fact]
    public async Task Attributes_are_preserved()
    {
        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        var span = Span("tracon.run", parent: null) with
        {
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tracon.agent.name"] = "support",
                ["gen_ai.usage.input_tokens"] = "42",
            },
        };

        await Store.WriteSpansAsync(Batch(runId, [span]));

        var trace = await Store.GetTraceByRunAsync(runId);
        var stored = trace.ShouldNotBeNull().Spans.ShouldHaveSingleItem();

        stored.Attributes.Count.ShouldBe(2);
        string.Equals(stored.Attributes["tracon.agent.name"], "support", StringComparison.Ordinal)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Writing_the_same_span_twice_does_not_create_a_duplicate()
    {
        // Ids are DERIVED from W3C ids; the second write updates the same
        // row. Otherwise a retry would double the span tree.
        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        var span = Span("tracon.run", parent: null);

        await Store.WriteSpansAsync(Batch(runId, [span]));
        await Store.WriteSpansAsync(Batch(runId, [span with { Status = TraceSpanStatus.Error }]));

        var trace = await Store.GetTraceByRunAsync(runId);
        var stored = trace.ShouldNotBeNull().Spans.ShouldHaveSingleItem();

        stored.Status.ShouldBe(TraceSpanStatus.Error);
    }

    [Fact]
    public async Task Two_tenants_that_share_a_trace_id_each_read_only_their_own_spans()
    {
        // A caller that sends a W3C traceparent gives every run it starts the same trace id, and two
        // tenants can be called by the same gateway. The id names a trace inside one tenant, so a
        // second tenant's spans must not join the first tenant's trace, whichever store holds them.
        var runA = TraconId.NewId();
        var runB = TraconId.NewId();

        AmbientTenant.TenantId = TenantA;
        await SeedRunAsync(runA);
        await Store.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = _traceId,
            TenantId = TenantA,
            RunId = runA,
            Spans = [Span("tenant-a-root", parent: null)],
        });

        AmbientTenant.TenantId = TenantB;
        await SeedRunAsync(runB);
        await Store.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = _traceId,
            TenantId = TenantB,
            RunId = runB,
            Spans = [Span("tenant-b-root", parent: null)],
        });

        var seenByB = await Store.GetTraceByRunAsync(runB);
        seenByB.ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.ShouldBe("tenant-b-root");
        (await Store.GetTraceByRunAsync(runA)).ShouldBeNull();

        AmbientTenant.TenantId = TenantA;

        var seenByA = await Store.GetTraceByRunAsync(runA);
        seenByA.ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.ShouldBe("tenant-a-root");
        (await Store.GetTraceByRunAsync(runB)).ShouldBeNull();
    }

    [Fact]
    public async Task Two_runs_of_one_tenant_that_share_a_trace_id_each_read_only_their_own_spans()
    {
        // A caller that sends a W3C traceparent gives every run it starts the same trace id, and the
        // id is the caller's to choose. The trace belongs to the run: the first run must not read
        // 404, and the second must not read the spans of both.
        var runA = TraconId.NewId();
        var runB = TraconId.NewId();
        await SeedRunAsync(runA);
        await SeedRunAsync(runB);

        var rootA = Span("run-a-root", parent: null);
        var rootB = Span("run-b-root", parent: null);

        await Store.WriteSpansAsync(Batch(runA, [rootA, Span("run-a-child", parent: rootA.Id)]));
        await Store.WriteSpansAsync(Batch(runB, [rootB, Span("run-b-child", parent: rootB.Id)]));

        var traceA = await Store.GetTraceByRunAsync(runA);
        var traceB = await Store.GetTraceByRunAsync(runB);

        traceA.ShouldNotBeNull();
        traceB.ShouldNotBeNull();
        traceA.RunId.ShouldBe(runA);
        traceB.RunId.ShouldBe(runB);
        traceA.TraceId.ShouldBe(_traceId);
        traceB.TraceId.ShouldBe(_traceId);
        traceA.Id.ShouldNotBe(traceB.Id);
        traceA.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["run-a-child", "run-a-root"]);
        traceB.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["run-b-child", "run-b-root"]);
    }

    [Fact]
    public async Task A_run_written_under_two_trace_ids_reads_back_as_one_trace()
    {
        // A job that is retried runs the same run again and writes it under a new W3C id. The run
        // has one trace, whatever the number of writes: both attempts' spans, the id of the last
        // write, and a header that covers both.
        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        var firstId = NewTraceId();
        var secondId = NewTraceId();
        var origin = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

        var firstRoot = SpanAt("attempt-1", parent: null, firstId, origin, TimeSpan.FromSeconds(4));
        var firstChild = SpanAt("attempt-1-child", firstRoot.Id, firstId, origin.AddSeconds(1), TimeSpan.FromSeconds(2));
        var secondRoot = SpanAt("attempt-2", parent: null, secondId, origin.AddSeconds(10), TimeSpan.FromSeconds(5));
        var secondChild = SpanAt("attempt-2-child", secondRoot.Id, secondId, origin.AddSeconds(11), TimeSpan.FromSeconds(2));

        await Store.WriteSpansAsync(Batch(runId, [firstRoot, firstChild], firstId));
        await Store.WriteSpansAsync(Batch(runId, [secondRoot, secondChild], secondId));

        var trace = await Store.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.RunId.ShouldBe(runId);
        trace.TraceId.ShouldBe(secondId);
        trace.Spans.Count.ShouldBe(4);
        trace.StartedAt.ShouldBe(origin);
        trace.EndedAt.ShouldBe(origin.AddSeconds(15));
        trace.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(["attempt-1", "attempt-1-child", "attempt-2", "attempt-2-child"]);
    }

    [Fact]
    public async Task Two_tenants_that_write_one_run_id_keep_their_own_traces()
    {
        // A run id is a key in its own right only inside one tenant. In a SQL store the run row belongs
        // to one tenant and the foreign key of the trace checks that the row exists, not whose it is,
        // so a second tenant that names the same run id must still get a trace of its own.
        var runId = TraconId.NewId();

        AmbientTenant.TenantId = TenantA;
        await SeedRunAsync(runId);
        await Store.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = _traceId,
            TenantId = TenantA,
            RunId = runId,
            Spans = [Span("tenant-a-root", parent: null)],
        });

        await Store.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = _traceId,
            TenantId = TenantB,
            RunId = runId,
            Spans = [Span("tenant-b-root", parent: null)],
        });

        AmbientTenant.TenantId = TenantB;

        var seenByB = await Store.GetTraceByRunAsync(runId);
        seenByB.ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.ShouldBe("tenant-b-root");
        seenByB.TenantId.ShouldBe(TenantB);

        AmbientTenant.TenantId = TenantA;

        var seenByA = await Store.GetTraceByRunAsync(runId);
        seenByA.ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.ShouldBe("tenant-a-root");
        seenByA.TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task Concurrent_writes_for_one_run_leave_one_trace_with_every_span()
    {
        // The run's header is created by whichever write comes first and updated by the others. Two
        // headers, or a span lost to a header that was replaced, both show here.
        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        const int writes = 8;

        var batches = Enumerable.Range(0, writes)
            .Select(index => Batch(runId, [Span($"span-{index}", parent: null)]))
            .ToArray();

        await Task.WhenAll(batches.Select(batch => Store.WriteSpansAsync(batch).AsTask()));

        var trace = await Store.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.RunId.ShouldBe(runId);
        trace.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(Enumerable.Range(0, writes).Select(static index => $"span-{index}").Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Unrecorded_run_returns_empty()
        => (await Store.GetTraceByRunAsync(TraconId.NewId())).ShouldBeNull();

    [Fact]
    public async Task Empty_set_is_not_written()
    {
        var runId = TraconId.NewId();
        await SeedRunAsync(runId);

        await Store.WriteSpansAsync(Batch(runId, []));

        (await Store.GetTraceByRunAsync(runId)).ShouldBeNull();
    }

    private TraceSpanBatch Batch(Guid runId, IReadOnlyList<TraceSpan> spans, string? traceId = null)
        => new()
        {
            TraceId = traceId ?? _traceId,
            TenantId = TenantId,
            RunId = runId,
            Spans = spans,
        };

    // Each test instance gets its own trace id: xunit sets up a fresh
    // instance per test, so tests never overwrite one another's trace.
    private readonly string _traceId = NewTraceId();

    private TraceSpan Span(string name, Guid? parent)
        => SpanAt(name, parent, _traceId, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(120));

    private static TraceSpan SpanAt(string name, Guid? parent, string traceId, DateTimeOffset startedAt, TimeSpan duration)
    {
        var spanId = Guid.NewGuid().ToString("N")[..16];

        return new TraceSpan
        {
            Id = DeriveId(traceId, spanId),
            ParentId = parent,
            SpanId = spanId,
            Name = name,
            Kind = TraceSpanKind.Internal,
            StartedAt = startedAt,
            EndedAt = startedAt + duration,
            Status = TraceSpanStatus.Ok,
        };
    }

    private static string NewTraceId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Mirrors the production derivation exactly: the id is the first 16
    /// bytes of the SHA-256 hash of the W3C ids.
    /// </summary>
    private static Guid DeriveId(string traceId, string spanId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{traceId}:{spanId}"));

        return new Guid(hash.AsSpan(0, 16));
    }
}
