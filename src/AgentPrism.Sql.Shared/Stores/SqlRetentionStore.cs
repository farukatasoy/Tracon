using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <see cref="IRetentionStore"/>'un veri duzlemi uygulamasi: sayma, parti parti
/// silme ve arsiv icin okuma.
/// </summary>
/// <remarks>
/// <para>
/// Hedef basina SQL metni ELLE COPYALANMAZ: <see cref="RetentionTargetRegistry"/>
/// tablo/kosulu tanimlar, <see cref="SqlDialect"/> saglayiciya ozgu 3 parti
/// sablonunu (say/oku/sil) uygular. Gerekce: karar K-198.
/// </para>
/// <para>
/// Arsiv satirlari, sutun semasini onceden bilmeden ELLE (yansimasiz) JSON'a
/// cevrilir — bu depo <c>AgentPrism.PostgreSql</c> icine derlenir ve o paket
/// AOT uyumlulugunu korumak zorundadir.
/// </para>
/// </remarks>
internal sealed class SqlRetentionStore : IRetentionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;

    /// <summary>Yeni bir saklama veri duzlemi deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlRetentionStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
    }

    /// <inheritdoc />
    public async ValueTask<long> CountOlderThanAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionCountSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            null => 0L,
            long count => count,
            int count => count,
            _ => Convert.ToInt64(result, CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionArchiveSelectSql(
            definition.Table,
            Where(definition, tenantId),
            definition.OrderColumn);

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        return await DbHelpers
            .ReadListAsync(command, reader => new ArchiveRow { Json = RowToJson(reader) }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionDeleteBatchSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
        string target,
        string? tenantId,
        long maxRows,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(
            definition.Table,
            definition.RowLimitOrderExpression,
            tenantId is null ? null : definition.TenantPredicate);

        var command = _context.CreateCommand(sql);
        _dialect.AddInt64(command, "n", maxRows);
        AddTenant(command, tenantId);

        var rows = await DbHelpers
            .ReadListAsync(command, static reader => DbHelpers.GetNullableTimestamp(reader, 0), cancellationToken)
            .ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>Hedefin kosuluna kiraci suzgecini ekler.</summary>
    /// <param name="definition">Hedef tanimi.</param>
    /// <param name="tenantId">Kiraci; <see langword="null"/> ise suzgec yoktur.</param>
    /// <returns>Calistirilacak <c>WHERE</c> kosulu.</returns>
    private static string Where(RetentionTargetDefinition definition, string? tenantId)
        => tenantId is null
            ? definition.WherePredicate
            : SqlDialect.Combine(definition.WherePredicate, definition.TenantPredicate);

    /// <summary>Kiraci parametresini yalnizca gerekiyorsa baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="tenantId">Kiraci; <see langword="null"/> ise hicbir sey baglanmaz.</param>
    private static void AddTenant(DbCommand command, string? tenantId)
    {
        if (tenantId is not null)
        {
            DbHelpers.Add(command, "tenant_id", tenantId);
        }
    }

    /// <summary>
    /// Bir satiri, sutun semasini onceden bilmeden tek satirlik bir JSON
    /// nesnesine cevirir (JSONL'in bir satiri).
    /// </summary>
    private static string RowToJson(DbDataReader reader)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var name = reader.GetName(ordinal);

                if (reader.IsDBNull(ordinal))
                {
                    writer.WriteNull(name);

                    continue;
                }

                WriteValue(writer, name, reader.GetValue(ordinal));
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object value)
    {
        switch (value)
        {
            case string text:
                writer.WriteString(name, text);

                break;
            case bool flag:
                writer.WriteBoolean(name, flag);

                break;
            case Guid id:
                writer.WriteString(name, id);

                break;
            case DateTime dateTime:
                writer.WriteString(
                    name,
                    DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));

                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteString(name, dateTimeOffset);

                break;
            case short int16:
                writer.WriteNumber(name, int16);

                break;
            case int int32:
                writer.WriteNumber(name, int32);

                break;
            case long int64:
                writer.WriteNumber(name, int64);

                break;
            case decimal number:
                writer.WriteNumber(name, number);

                break;
            case double real:
                writer.WriteNumber(name, real);

                break;
            case float single:
                writer.WriteNumber(name, single);

                break;
            case byte[] bytes:
                writer.WriteBase64String(name, bytes);

                break;
            default:
                // Saglayiciya ozgu az rastlanan bir tip (ornegin DateOnly):
                // metin gosterimi kaybetmeden okunabilir kalir.
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));

                break;
        }
    }
}
