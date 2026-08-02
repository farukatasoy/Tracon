namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="ITraceStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 6'da eklendi. Span kimlikleri W3C kimliklerinden turetildigi icin ayni
/// span'in iki kez yazilmasi tekrar kaydi uretmemelidir; iki uygulama da bu
/// kurali saglamalidir.
/// </remarks>
public abstract class TraceStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected ITraceStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<ITraceStore> CreateStoreAsync();

    /// <summary>Span'lerin baglanacagi bir calistirma acar.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    protected abstract ValueTask SeedRunAsync(Guid runId);

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    /// <summary>
    /// Span'lerin yazilacagi kiraci. PostgreSQL deposu okurken kendi kiraci
    /// baglamini kullanir; yazma ve okuma ayni kiraciya dusmezse trace hicbir
    /// zaman bulunamaz.
    /// </summary>
    protected virtual string TenantId => "default";

    [Fact]
    public async Task Span_agaci_gidip_gelir()
    {
        var runId = AgentPrismId.NewId();
        await SeedRunAsync(runId);

        var root = Span("agentprism.run", parent: null);
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
    public async Task Oznitelikler_korunur()
    {
        var runId = AgentPrismId.NewId();
        await SeedRunAsync(runId);

        var span = Span("agentprism.run", parent: null) with
        {
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agentprism.agent.name"] = "support",
                ["gen_ai.usage.input_tokens"] = "42",
            },
        };

        await Store.WriteSpansAsync(Batch(runId, [span]));

        var trace = await Store.GetTraceByRunAsync(runId);
        var stored = trace.ShouldNotBeNull().Spans.ShouldHaveSingleItem();

        stored.Attributes.Count.ShouldBe(2);
        string.Equals(stored.Attributes["agentprism.agent.name"], "support", StringComparison.Ordinal)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Ayni_span_iki_kez_yazilirsa_tekrar_olusmaz()
    {
        // Kimlikler W3C kimliklerinden TURETILIR; ikinci yazma ayni satiri
        // gunceller. Aksi halde yeniden deneme sonrasi span agaci ikiye katlanirdi.
        var runId = AgentPrismId.NewId();
        await SeedRunAsync(runId);

        var span = Span("agentprism.run", parent: null);

        await Store.WriteSpansAsync(Batch(runId, [span]));
        await Store.WriteSpansAsync(Batch(runId, [span with { Status = TraceSpanStatus.Error }]));

        var trace = await Store.GetTraceByRunAsync(runId);
        var stored = trace.ShouldNotBeNull().Spans.ShouldHaveSingleItem();

        stored.Status.ShouldBe(TraceSpanStatus.Error);
    }

    [Fact]
    public async Task Kayitsiz_calistirma_bos_doner()
        => (await Store.GetTraceByRunAsync(AgentPrismId.NewId())).ShouldBeNull();

    [Fact]
    public async Task Bos_kume_yazilmaz()
    {
        var runId = AgentPrismId.NewId();
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

    // Her test ornegi kendi trace kimligini alir: xunit her test icin yeni bir
    // ornek kurar, boylece testler birbirinin trace'ini ezmez.
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
    /// Uretim kodundaki turetmeyi birebir taklit eder: kimlik W3C kimliklerinin
    /// SHA-256 ozetinin ilk 16 baytidir.
    /// </summary>
    private static Guid DeriveId(string traceId, string spanId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{traceId}:{spanId}"));

        return new Guid(hash.AsSpan(0, 16));
    }
}
