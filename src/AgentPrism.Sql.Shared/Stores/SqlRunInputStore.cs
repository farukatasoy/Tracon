using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Calistirma girdilerini SQL'de saklayan depo (Faz 47).</summary>
/// <remarks>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryRunInputStore"/> ile birebir aynidir
/// ve ortak sozlesme testleriyle korunur.
/// </para>
/// <para>
/// 🚨 Mesajlar <c>json</c> sutununda saklanir, <c>jsonb</c>'de <strong>degil</strong>:
/// <c>ChatMessage</c> icerikleri polimorfiktir ve <c>$type</c> ayraci nesnenin
/// ilk ozelligi olmak zorundadir (K-027). Sutun tipi
/// <see cref="SqlDialect.AddJson"/> ile degil, semada belirlenir; buradaki
/// gorev yalnizca metni dogru serilestirmektir.
/// </para>
/// </remarks>
internal sealed class SqlRunInputStore : IRunInputStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir SQL girdi deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlRunInputStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = CreateCommand(_sql.InsertRunInput);
        Dialect.AddUuid(command, "run_id", record.RunId);
        DbHelpers.Add(command, "tenant_id", record.TenantId);
        Dialect.AddJson(
            command,
            "messages",
            JsonSerializer.Serialize(record.Messages, AgentPrismJsonContext.Default.IReadOnlyListChatMessage));
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RunInputRecord?> GetAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectRunInput);
        Dialect.AddUuid(command, "run_id", runId);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers
            .ReadSingleAsync(command, reader => Read(reader, tenantId, runId), cancellationToken)
            .ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static RunInputRecord Read(DbDataReader reader, string tenantId, Guid runId)
        => new()
        {
            RunId = runId,
            TenantId = tenantId,
            Messages = JsonSerializer.Deserialize(
                reader.GetString(0),
                AgentPrismJsonContext.Default.IReadOnlyListChatMessage) ?? [],
            CreatedAt = DbHelpers.GetTimestamp(reader, 1),
        };
}
