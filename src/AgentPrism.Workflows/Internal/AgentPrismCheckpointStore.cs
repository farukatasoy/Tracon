using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace AgentPrism;

/// <summary>
/// Microsoft Agent Framework'un kontrol noktasi deposunu AgentPrism'in
/// <see cref="IWorkflowCheckpointStore"/> sozlesmesine baglar.
/// </summary>
/// <remarks>
/// <para>
/// Uyarlama <strong>bu pakette</strong> yasar. Sebep: kontrol noktasi deposu
/// <c>AgentPrism.PostgreSql</c> icindedir ve o paket workflow motorunun
/// tiplerini gormemelidir. Sozlesme <see cref="JsonElement"/> ile ifade edilir,
/// MAF tipine cevrim yalnizca burada yapilir.
/// </para>
/// <para>
/// 🚨 <strong>Kontrol noktasi kimligini AgentPrism uretir.</strong> MAF bir
/// <see cref="CheckpointInfo"/> bekler ve icerigi bizim kararimizdir; zaman
/// sirali bir UUID kullanmak, listenin dogal siralamasini kimligin kendisine
/// tasir.
/// </para>
/// <para>
/// Kiraci ve calistirma kimligi <em>ortam kapsamindan</em> okunur: MAF'in
/// yazma cagrisi hicbir baglam parametresi tasimaz. Ayni cozum Faz 14'te
/// <c>PostgresAgentFileStore</c> icin yapilmisti (K-114).
/// </para>
/// </remarks>
internal sealed class AgentPrismCheckpointStore : ICheckpointStore<JsonElement>
{
    private readonly IWorkflowCheckpointStore _store;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir uyarlama olusturur.</summary>
    /// <param name="store">AgentPrism kontrol noktasi deposu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public AgentPrismCheckpointStore(IWorkflowCheckpointStore store, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent = null)
    {
        var scope = AgentPrismRunContext.Current;
        var checkpointId = AgentPrismId.NewId().ToString("n", System.Globalization.CultureInfo.InvariantCulture);

        await _store.CreateAsync(
            new WorkflowCheckpointRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = scope?.TenantId ?? _tenantContext.TenantId,
                SessionId = sessionId,
                CheckpointId = checkpointId,
                ParentCheckpointId = parent?.CheckpointId,
                RunId = scope?.RunId,
                CreatedAt = DateTimeOffset.UtcNow,
                State = value,
            },
            CancellationToken.None).ConfigureAwait(false);

        return new CheckpointInfo(sessionId, checkpointId);
    }

    /// <inheritdoc />
    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var tenantId = AgentPrismRunContext.Current?.TenantId ?? _tenantContext.TenantId;

        var state = await _store
            .ReadAsync(tenantId, sessionId, key.CheckpointId, CancellationToken.None)
            .ConfigureAwait(false);

        // Kiraci eslesmezse depo null doner ve buraya "bulunamadi" olarak gelir.
        // Mesajda kiracidan soz EDILMEZ: baska bir kiracinin kontrol noktasinin
        // var oldugu bilgisi de sizdirilmamalidir.
        return state ?? throw new AgentPrismException(
            $"Checkpoint '{key.CheckpointId}' was not found.");
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent = null)
    {
        var tenantId = AgentPrismRunContext.Current?.TenantId ?? _tenantContext.TenantId;

        var records = await _store.ListAsync(tenantId, sessionId, CancellationToken.None).ConfigureAwait(false);

        // Ebeveyn filtresi bellekte uygulanir: bir oturumun kontrol noktalari
        // MaxSuperSteps ile sinirlidir ve ayri bir sorgu yolu acmak, iki depo
        // uygulamasinda da tekrarlanacak bir sutun filtresi demekti.
        var filtered = withParent is null
            ? records
            : [.. records.Where(record =>
                string.Equals(record.ParentCheckpointId, withParent.CheckpointId, StringComparison.Ordinal))];

        return [.. filtered.Select(record => new CheckpointInfo(record.SessionId, record.CheckpointId))];
    }
}
