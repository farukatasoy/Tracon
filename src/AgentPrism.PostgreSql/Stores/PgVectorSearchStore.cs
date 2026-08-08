using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// <see cref="IVectorSearchStore"/>'un <c>pgvector</c> destekli, tek somut
/// uygulamasi (Faz 51).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Diger 20 depodan farkli olarak <c>AgentPrism.Sql.Shared</c>'in saglayicidan
/// bagimsiz katmanindan GECMEZ: bu depo <strong>yalniz</strong> PostgreSQL'de var
/// olacagi icin bir <see cref="SqlDialect"/> soyutlamasina gerek yoktur; dogrudan
/// Npgsql kullanir. Gerekce: <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>, 51.3.
/// </para>
/// <para>
/// Gomu <strong>metin</strong> olarak gonderilir (<c>@embedding::vector</c> cast).
/// Hicbir vektor paketi alinmadi (K-007/K-211'in ikinci uygulamasi, karar Faz
/// 51 kapanisinda K-numarasi alir); AOT duruşu bu yuzden bozulmaz.
/// </para>
/// </remarks>
internal sealed class PgVectorSearchStore : IVectorSearchStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schema;

    /// <summary>Yeni bir vektor arama deposu olusturur.</summary>
    /// <param name="dataSource">PostgreSQL veri kaynagi.</param>
    /// <param name="postgresOptions">Sema adini tasiyan PostgreSQL ayarlari.</param>
    /// <param name="knowledgeOptions">Gomu boyutunu tasiyan bilgi tabani ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PgVectorSearchStore(
        NpgsqlDataSource dataSource,
        AgentPrismPostgreSqlOptions postgresOptions,
        AgentPrismKnowledgeOptions knowledgeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(postgresOptions);
        ArgumentNullException.ThrowIfNull(knowledgeOptions);

        _dataSource = dataSource;
        _schema = SqlIdentifier.RequireSchemaName(postgresOptions.SchemaName);
        Dimensions = knowledgeOptions.Dimensions;
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public async ValueTask UpsertAsync(
        string tenantId,
        string collection,
        string sourceId,
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(chunks);

        foreach (var chunk in chunks)
        {
            if (chunk.Embedding.Length != Dimensions)
            {
                throw new ArgumentException(
                    $"Parca {chunk.Index} gomu uzunlugu ({chunk.Embedding.Length}) depo boyutuyla " +
                    $"({Dimensions}) eslesmiyor.",
                    nameof(chunks));
            }
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await DeleteSourceAsync(connection, transaction, tenantId, collection, sourceId, cancellationToken)
                    .ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;

                foreach (var chunk in chunks)
                {
                    var insert = new NpgsqlCommand(
                        $"""
                         INSERT INTO {_schema}.document_embeddings
                             (id, tenant_id, collection, source_id, chunk_index, content, metadata, embedding, created_at)
                         VALUES
                             (@id, @tenant_id, @collection, @source_id, @chunk_index, @content, @metadata::jsonb, @embedding::vector, @now);
                         """,
                        connection,
                        (NpgsqlTransaction)transaction);

                    await using (insert.ConfigureAwait(false))
                    {
                        insert.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = AgentPrismId.NewId(now) });
                        insert.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text) { Value = tenantId });
                        insert.Parameters.Add(new NpgsqlParameter("collection", NpgsqlDbType.Text) { Value = collection });
                        insert.Parameters.Add(new NpgsqlParameter("source_id", NpgsqlDbType.Text) { Value = sourceId });
                        insert.Parameters.Add(new NpgsqlParameter("chunk_index", NpgsqlDbType.Integer) { Value = chunk.Index });
                        insert.Parameters.Add(new NpgsqlParameter("content", NpgsqlDbType.Text) { Value = chunk.Content });
                        insert.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Text) { Value = SerializeMetadata(chunk.Metadata) });
                        insert.Parameters.Add(new NpgsqlParameter("embedding", NpgsqlDbType.Text) { Value = FormatVector(chunk.Embedding) });
                        insert.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });

                        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Collection, nameof(request));

        var command = _dataSource.CreateCommand(
            $"""
             SELECT source_id, chunk_index, content, metadata, embedding <=> @query::vector AS distance
             FROM {_schema}.document_embeddings
             WHERE tenant_id = @tenant_id AND collection = @collection
               AND (@max_distance::double precision IS NULL OR embedding <=> @query::vector <= @max_distance)
             ORDER BY embedding <=> @query::vector
             LIMIT @top;
             """);

        command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text) { Value = request.TenantId });
        command.Parameters.Add(new NpgsqlParameter("collection", NpgsqlDbType.Text) { Value = request.Collection });
        command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = FormatVector(request.QueryEmbedding) });
        command.Parameters.Add(new NpgsqlParameter("top", NpgsqlDbType.Integer) { Value = request.Top });
        command.Parameters.Add(new NpgsqlParameter("max_distance", NpgsqlDbType.Double)
        {
            Value = request.MaxDistance is { } maxDistance ? maxDistance : DBNull.Value,
        });

        var results = new List<VectorSearchHit>();

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new VectorSearchHit
                {
                    SourceId = reader.GetString(0),
                    ChunkIndex = reader.GetInt32(1),
                    Content = reader.GetString(2),
                    Metadata = DeserializeMetadata(reader.GetString(3)),
                    Distance = reader.GetDouble(4),
                });
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteSourceAsync(
        string tenantId,
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            return await DeleteSourceAsync(connection, transaction: null, tenantId, collection, sourceId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> ListSourcesAsync(
        string tenantId,
        string collection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        var command = _dataSource.CreateCommand(
            $"""
             SELECT DISTINCT source_id
             FROM {_schema}.document_embeddings
             WHERE tenant_id = @tenant_id AND collection = @collection
             ORDER BY source_id;
             """);

        command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("collection", NpgsqlDbType.Text) { Value = collection });

        var results = new List<string>();

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private async ValueTask<int> DeleteSourceAsync(
        NpgsqlConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string tenantId,
        string collection,
        string sourceId,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(
            $"""
             DELETE FROM {_schema}.document_embeddings
             WHERE tenant_id = @tenant_id AND collection = @collection AND source_id = @source_id;
             """,
            connection,
            (NpgsqlTransaction?)transaction);

        await using (command.ConfigureAwait(false))
        {
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("collection", NpgsqlDbType.Text) { Value = collection });
            command.Parameters.Add(new NpgsqlParameter("source_id", NpgsqlDbType.Text) { Value = sourceId });

            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Gomuyu <c>pgvector</c>'un metin bicimine cevirir: <c>[0.1,0.2,...]</c>.</summary>
    private static string FormatVector(ReadOnlyMemory<float> vector)
    {
        var span = vector.Span;
        var builder = new StringBuilder(span.Length * 10 + 2);
        builder.Append('[');

        for (var i = 0; i < span.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(span[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>
    /// Duz bir string->string sozlugu <c>jsonb</c> metnine cevirir. Yansima
    /// KULLANMAZ (<see cref="Utf8JsonWriter"/> DOM tabanlidir) — AOT guvenli.
    /// </summary>
    private static string SerializeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return "{}";
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var (key, value) in metadata)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// <see cref="SerializeMetadata"/>'nin tersi. <see cref="JsonDocument"/> DOM
    /// tabanlidir — yansima KULLANMAZ, AOT guvenli.
    /// </summary>
    private static Dictionary<string, string>? DeserializeMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = document.RootElement.EnumerateObject();
        Dictionary<string, string>? result = null;

        foreach (var property in properties)
        {
            (result ??= new Dictionary<string, string>(StringComparer.Ordinal))[property.Name] =
                property.Value.GetString() ?? string.Empty;
        }

        return result;
    }
}
