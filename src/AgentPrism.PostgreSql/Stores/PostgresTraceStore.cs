using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

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
public sealed class PostgresTraceStore : ITraceStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly ITenantContext _tenantContext;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir span deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresTraceStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _tenantContext = tenantContext;
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Spans.Count == 0)
        {
            return;
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

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
        header.Parameters.AddWithValue("run_id", runId);
        header.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        var trace = await NpgsqlHelpers.ReadSingleAsync(header, ReadTrace, cancellationToken).ConfigureAwait(false);

        if (trace is null)
        {
            return null;
        }

        var spans = CreateCommand(_sql.SelectSpans);
        spans.Parameters.AddWithValue("trace_id", trace.Id);

        return trace with
        {
            Spans = await NpgsqlHelpers.ReadListAsync(spans, ReadSpan, cancellationToken).ConfigureAwait(false),
        };
    }

    private async ValueTask<Guid> UpsertTraceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TraceSpanBatch batch,
        CancellationToken cancellationToken)
    {
        var startedAt = batch.Spans.Min(static span => span.StartedAt);
        var endedAt = batch.Spans.Max(static span => span.EndedAt);

        var command = new NpgsqlCommand(_sql.UpsertTrace, connection, transaction)
        {
            CommandTimeout = _commandTimeout,
        };

        command.Parameters.AddWithValue("id", AgentPrismId.NewId());
        command.Parameters.AddWithValue("tenant_id", batch.TenantId);
        command.Parameters.AddWithValue("trace_id", batch.TraceId);
        command.Parameters.Add(new NpgsqlParameter("run_id", NpgsqlDbType.Uuid)
        {
            Value = batch.RunId is { } runId ? (object)runId : DBNull.Value,
        });
        command.Parameters.AddWithValue("started_at", startedAt.UtcDateTime);
        command.Parameters.Add(new NpgsqlParameter("ended_at", NpgsqlDbType.TimestampTz)
        {
            Value = endedAt is { } ended ? (object)ended.UtcDateTime : DBNull.Value,
        });

        var result = await NpgsqlHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return (Guid)result!;
    }

    private async ValueTask UpsertSpanAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid traceId,
        TraceSpan span,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(_sql.UpsertSpan, connection, transaction)
        {
            CommandTimeout = _commandTimeout,
        };

        command.Parameters.AddWithValue("id", span.Id);
        command.Parameters.AddWithValue("trace_id", traceId);
        command.Parameters.Add(new NpgsqlParameter("parent_span_id", NpgsqlDbType.Uuid)
        {
            Value = span.ParentId is { } parentId ? (object)parentId : DBNull.Value,
        });
        command.Parameters.AddWithValue("span_id", span.SpanId);
        command.Parameters.AddWithValue("name", span.Name);
        command.Parameters.AddWithValue("kind", (short)span.Kind);
        command.Parameters.AddWithValue("started_at", span.StartedAt.UtcDateTime);
        command.Parameters.Add(new NpgsqlParameter("ended_at", NpgsqlDbType.TimestampTz)
        {
            Value = span.EndedAt is { } ended ? (object)ended.UtcDateTime : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("attributes", NpgsqlDbType.Jsonb)
        {
            Value = WriteAttributes(span.Attributes),
        });
        command.Parameters.AddWithValue("status", (short)span.Status);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
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

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static RunTrace ReadTrace(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TraceId = reader.GetString(1),
            RunId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            TenantId = reader.GetString(3),
            StartedAt = NpgsqlHelpers.GetTimestamp(reader, 4),
            EndedAt = reader.IsDBNull(5) ? null : NpgsqlHelpers.GetTimestamp(reader, 5),
        };

    private static TraceSpan ReadSpan(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            ParentId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
            SpanId = NpgsqlHelpers.GetNullableString(reader, 2) ?? string.Empty,
            Name = reader.GetString(3),
            Kind = (TraceSpanKind)reader.GetInt16(4),
            StartedAt = NpgsqlHelpers.GetTimestamp(reader, 5),
            EndedAt = reader.IsDBNull(6) ? null : NpgsqlHelpers.GetTimestamp(reader, 6),
            Attributes = ReadAttributes(NpgsqlHelpers.GetNullableString(reader, 7)),
            Status = reader.IsDBNull(8) ? TraceSpanStatus.Unset : (TraceSpanStatus)reader.GetInt16(8),
        };
}
