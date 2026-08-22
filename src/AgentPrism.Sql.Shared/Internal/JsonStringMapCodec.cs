using System.Buffers;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Serializes a flat <c>string</c>-to-<c>string</c> map to and from JSON text
/// for a column that is <c>jsonb</c> in PostgreSQL and plain text in SQL
/// Server/SQLite - the same convention <c>runs.labels</c> uses.
/// </summary>
/// <remarks>
/// Hand-written rather than a source-generated <c>JsonSerializerContext</c>:
/// the shape is a single flat map, AOT-safe reflection-free code is a few
/// lines, and a source-generated context would not pay for itself here. This
/// is the same approach <c>PgVectorSearchStore</c> uses for
/// <c>document_embeddings.metadata</c>.
/// </remarks>
internal static class JsonStringMapCodec
{
    /// <summary>Serializes a map to JSON text.</summary>
    /// <param name="map">The map. <see langword="null"/> or empty produces <see langword="null"/>.</param>
    public static string? Serialize(IReadOnlyDictionary<string, string>? map)
    {
        if (map is null or { Count: 0 })
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (var (key, value) in map)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Deserializes a map from JSON text.
    /// </summary>
    /// <returns><see langword="null"/> when the text is empty, malformed, or parses to no entries.</returns>
    /// <remarks>
    /// Malformed JSON returns <see langword="null"/> instead of throwing: this
    /// data is observability/configuration, not the primary record, and a
    /// single unreadable map must not fail the whole read. PostgreSQL
    /// validates the column at write time; SQL Server and SQLite hold plain text.
    /// </remarks>
    public static Dictionary<string, string>? Deserialize(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    map[property.Name] = property.Value.GetString()!;
                }
            }

            return map.Count == 0 ? null : map;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
