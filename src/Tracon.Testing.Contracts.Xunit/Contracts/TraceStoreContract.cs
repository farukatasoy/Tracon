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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
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
        });

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
    /// The span store has no listing endpoint; the count is reduced to how
    /// many of the runs seeded for that tenant are visible.
    /// </remarks>
    protected override async ValueTask<int> CountAsync(string tenantId)
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

    private TraceSpanBatch Batch(Guid runId, IReadOnlyList<TraceSpan> spans)
        => new()
        {
            TraceId = _traceId,
            TenantId = TenantId,
            RunId = runId,
            Spans = spans,
        };

    // Each test instance gets its own trace id: xunit sets up a fresh
    // instance per test, so tests never overwrite one another's trace.
    private readonly string _traceId = NewTraceId();

    private TraceSpan Span(string name, Guid? parent)
    {
        var spanId = Guid.NewGuid().ToString("N")[..16];

        return new TraceSpan
        {
            Id = DeriveId(_traceId, spanId),
            ParentId = parent,
            SpanId = spanId,
            Name = name,
            Kind = TraceSpanKind.Internal,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow.AddMilliseconds(120),
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
