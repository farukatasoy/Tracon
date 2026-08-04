using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Span'leri PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <para>
/// Bir trace'in span'leri <strong>tek islemde</strong> yazilir: yazma
/// calistirma bittikten sonra bir kez yapilir ve kismi bir trace okumak
/// waterfall gorunumunde bosluk olarak gorunurdu.
/// </para>
/// <para>
/// Span kimlikleri W3C kimliklerinden turetildigi icin ekleme <c>ON CONFLICT
/// DO UPDATE</c> ile yapilir; ayni span iki kez yazilirsa tekrar kaydi olusmaz.
/// </para>
/// </remarks>
internal sealed class SqlTraceStore : ITraceStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir span deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlTraceStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Spans.Count == 0)
        {
            return;
        }

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var traceId = await UpsertTraceAsync(connection, transaction, batch, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var span in batch.Spans)
                {
                    await UpsertSpanAsync(connection, transaction, traceId, span, cancellationToken)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<RunTrace?> GetTraceByRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var header = CreateCommand(_sql.SelectTraceByRun);
        DbHelpers.Add(header, "run_id", runId);
        DbHelpers.Add(header, "tenant_id", _tenantContext.TenantId);

        var trace = await DbHelpers.ReadSingleAsync(header, ReadTrace, cancellationToken).ConfigureAwait(false);

        if (trace is null)
        {
            return null;
        }

        var spans = CreateCommand(_sql.SelectSpans);
        DbHelpers.Add(spans, "trace_id", trace.Id);

        return trace with
        {
            Spans = await DbHelpers.ReadListAsync(spans, ReadSpan, cancellationToken).ConfigureAwait(false),
        };
    }

    private async ValueTask<Guid> UpsertTraceAsync(
        DbConnection connection,
        DbTransaction transaction,
        TraceSpanBatch batch,
        CancellationToken cancellationToken)
    {
        var startedAt = batch.Spans.Min(static span => span.StartedAt);
        var endedAt = batch.Spans.Max(static span => span.EndedAt);

        var command = _context.CreateCommand(_sql.UpsertTrace, connection, transaction);

        DbHelpers.Add(command, "id", AgentPrismId.NewId());
        DbHelpers.Add(command, "tenant_id", batch.TenantId);
        DbHelpers.Add(command, "trace_id", batch.TraceId);
        Dialect.AddUuid(command, "run_id", batch.RunId);
        Dialect.AddTimestamp(command, "started_at", startedAt);
        Dialect.AddTimestamp(command, "ended_at", endedAt);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return (Guid)result!;
    }

    private async ValueTask UpsertSpanAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid traceId,
        TraceSpan span,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_sql.UpsertSpan, connection, transaction);

        DbHelpers.Add(command, "id", span.Id);
        DbHelpers.Add(command, "trace_id", traceId);
        Dialect.AddUuid(command, "parent_span_id", span.ParentId);
        DbHelpers.Add(command, "span_id", span.SpanId);
        DbHelpers.Add(command, "name", span.Name);
        DbHelpers.Add(command, "kind", (short)span.Kind);
        Dialect.AddTimestamp(command, "started_at", span.StartedAt);
        Dialect.AddTimestamp(command, "ended_at", span.EndedAt);
        Dialect.AddJsonb(command, "attributes", WriteAttributes(span.Attributes));
        DbHelpers.Add(command, "status", (short)span.Status);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Oznitelik sozlugunu JSON metnine cevirir.
    /// </summary>
    /// <remarks>
    /// Sozluk <c>string → string</c> oldugu icin <see cref="Utf8JsonWriter"/> ile
    /// elle yazilir. Yansimaya dayanan serilestirme <c>IL2026</c> uretirdi ve
    /// bu paket AOT uyumlu isaretlidir. <c>jsonb</c> kullanmak burada guvenlidir:
    /// duz bir metin sozlugunde polimorfik <c>$type</c> ayraci yoktur, dolayisiyla
    /// anahtar siralamasi sorun cikarmaz (karsit ornek: karar K-027).
    /// </remarks>
    private static string WriteAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        var buffer = new MemoryStream();
        var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        foreach (var pair in attributes)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static Dictionary<string, string> ReadAttributes(string? json)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(json))
        {
            return attributes;
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return attributes;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            attributes[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        }

        return attributes;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static RunTrace ReadTrace(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TraceId = reader.GetString(1),
            RunId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            TenantId = reader.GetString(3),
            StartedAt = DbHelpers.GetTimestamp(reader, 4),
            EndedAt = reader.IsDBNull(5) ? null : DbHelpers.GetTimestamp(reader, 5),
        };

    private static TraceSpan ReadSpan(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            ParentId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
            SpanId = DbHelpers.GetNullableString(reader, 2) ?? string.Empty,
            Name = reader.GetString(3),
            Kind = (TraceSpanKind)reader.GetInt16(4),
            StartedAt = DbHelpers.GetTimestamp(reader, 5),
            EndedAt = reader.IsDBNull(6) ? null : DbHelpers.GetTimestamp(reader, 6),
            Attributes = ReadAttributes(DbHelpers.GetNullableString(reader, 7)),
            Status = reader.IsDBNull(8) ? TraceSpanStatus.Unset : (TraceSpanStatus)reader.GetInt16(8),
        };
}
