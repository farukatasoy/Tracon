namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="ITraceStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 6'da eklendi. Span kimlikleri W3C kimliklerinden turetildigi icin ayni
/// span'in iki kez yazilmasi tekrar kaydi uretmemelidir; iki uygulama da bu
/// kurali saglamalidir.
/// </remarks>
public abstract class TraceStoreContract : TenantIsolationContract<ITraceStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// Yazma kiraciyi partiden alir, okuma <see cref="ITenantContext"/>'ten;
    /// ikisi ayrisirsa trace hicbir zaman bulunamaz.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = AgentPrismId.NewId();
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
    /// Span deposunda listeleme ucu yoktur; sayim, o kiraci icin tohumlanan
    /// calistirmalarin kacinin gorulebildigine indirgenir.
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

    // Tohumlanan her calistirma; sayim bunlarin uzerinden yurur.
    private readonly List<Guid> _seededRuns = [];

    /// <summary>Span'lerin baglanacagi bir calistirma acar.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    protected abstract ValueTask SeedRunAsync(Guid runId);

    /// <summary>
    /// Span'lerin yazilacagi kiraci. Depo okurken kendi kiraci baglamini
    /// kullanir; yazma ve okuma ayni kiraciya dusmezse trace hicbir zaman
    /// bulunamaz. Bu yuzden deger ambient kiraciyla ayni kaynaktan gelir.
    /// </summary>
    protected string TenantId => AmbientTenant.TenantId;

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
