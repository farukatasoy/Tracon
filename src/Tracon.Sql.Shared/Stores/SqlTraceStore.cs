using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Stores spans in the SQL database.
/// </summary>
/// <remarks>
/// <para>
/// A trace's spans are written in a <strong>single transaction</strong>: the
/// write happens once after the run finishes, since reading a partial trace
/// would appear as a gap in the waterfall view.
/// </para>
/// <para>
/// Span ids are derived from W3C ids, so the insert uses <c>ON CONFLICT
/// DO UPDATE</c>; writing the same span twice does not create a duplicate.
/// </para>
/// </remarks>
internal sealed class SqlTraceStore : ITraceStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new span store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
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

    /// <summary>The gateway for provider-specific behavior.</summary>
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
        DbHelpers.AddTenant(header, _tenantContext.TenantId);

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

        DbHelpers.Add(command, "id", TraconId.NewId());
        DbHelpers.AddTenant(command, batch.TenantId);
        DbHelpers.Add(command, "trace_id", batch.TraceId);
        Dialect.AddUuid(command, "run_id", batch.RunId);
        Dialect.AddTimestamp(command, "started_at", startedAt);
        Dialect.AddTimestamp(command, "ended_at", endedAt);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return DbHelpers.ToGuid(result!);
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
    /// Converts the attribute dictionary to JSON text.
    /// </summary>
    /// <remarks>
    /// Because the dictionary is <c>string → string</c>, it is written by hand
    /// with <see cref="Utf8JsonWriter"/>. Reflection-based serialization would
    /// produce <c>IL2026</c>, and this package is marked AOT-compatible. Using
    /// <c>jsonb</c> is safe here: a plain string dictionary has no polymorphic
    /// <c>$type</c> discriminator, so key ordering does not matter (contrast:
    /// ).
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
