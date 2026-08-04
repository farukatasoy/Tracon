using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>Workflow tanimlarini PostgreSQL'de saklayan depo.</summary>
/// <remarks>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryWorkflowDefinitionStore"/> ile birebir
/// aynidir ve ortak sozlesme testleriyle korunur.
/// </para>
/// <para>
/// Kiraci kimligi <em>parametre olarak</em> alinir, <c>ITenantContext</c>'ten
/// okunmaz. Sebep: kontrol noktasi deposu ve workflow deposu MAF'in yurutme
/// hattindan da cagrilir; orada HTTP baglami yoktur ve ortam kiracisi yanlis
/// degere duserdi. Ayni tercih <see cref="SqlAgentSkillStore"/> icin de
/// yapilmisti.
/// </para>
/// </remarks>
internal sealed class SqlWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir workflow tanim deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlWorkflowDefinitionStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectWorkflow);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                name,
                reader.GetInt32(1),
                tenantId,
                DbHelpers.GetTimestamp(reader, 2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectWorkflows);
        DbHelpers.Add(command, "tenant_id", tenantId);

        // Ad `definition` yukunde DEGIL, sutunda yasar; okurken JSON'dan degil
        // sutundan alinmasi gerekir. Sorgu adi secmedigi icin burada yuke
        // gomulu olmayan tek alan odur ve ayri bir sorgu yerine SELECT
        // listesine eklenmistir.
        return await DbHelpers.ReadListAsync(
            command,
            reader => ReadDefinition(
                reader.GetString(0),
                reader.GetString(3),
                reader.GetInt32(1),
                tenantId,
                DbHelpers.GetTimestamp(reader, 2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<WorkflowDefinition> SaveAsync(
        string tenantId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(definition);

        var payload = JsonSerializer.Serialize(
            WorkflowDefinitionPayload.FromDefinition(definition),
            AgentPrismJsonContext.Default.WorkflowDefinitionPayload);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.UpsertWorkflow);
        DbHelpers.Add(command, "id", AgentPrismId.NewId());
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", definition.Name);
        Dialect.AddJsonb(command, "definition", payload);
        Dialect.AddTimestamp(command, "now", now);

        var written = await DbHelpers.ReadSingleAsync(
                command,
                static reader => new WrittenVersion(reader.GetInt32(0), DbHelpers.GetTimestamp(reader, 1)),
                cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"'{definition.Name}' workflow tanimi kaydedilemedi: veritabani surum bilgisi dondurmedi.");

        return definition with
        {
            TenantId = tenantId,
            Version = written.Version,
            UpdatedAt = written.UpdatedAt,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.DeleteWorkflow);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static WorkflowDefinition ReadDefinition(
        string payload,
        string name,
        int version,
        string tenantId,
        DateTimeOffset updatedAt)
    {
        var deserialized = JsonSerializer.Deserialize(payload, AgentPrismJsonContext.Default.WorkflowDefinitionPayload)
            ?? throw new AgentPrismException($"'{name}' workflow tanimi okunamadi: veritabanindaki JSON yuku bos.");

        return deserialized.ToDefinition(name, version, tenantId, updatedAt);
    }

    private sealed record WrittenVersion(int Version, DateTimeOffset UpdatedAt);
}
