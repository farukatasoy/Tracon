using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// Writes a definition as one canonical JSON tree, so two definitions can be compared by content and the
/// paths where they differ can be named.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Both sides of a comparison go through the same writer (K-455). A comparison that read one side from a
/// request and the other from a database payload would call equal content different whenever their number
/// formats differ (<c>100</c> and <c>100.0</c>, <c>0.7</c> and <c>0.70</c>), and an import run twice would
/// write a new version the second time. Properties are sorted and numbers are written the one way, so neither
/// the order a store returns a dictionary in nor the way a number was typed decides the answer.
/// </para>
/// </remarks>
internal static class DefinitionCanonicalJson
{
    private const int MaxChangedPaths = 40;

    private static readonly JsonSerializerOptions WriterOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes a value as a canonical JSON tree.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The canonical tree.</returns>
    public static JsonElement ToCanonical<T>(T value)
    {
        using var document = JsonSerializer.SerializeToDocument(value, WriterOptions);
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, document.RootElement);
        }

        using var canonical = JsonDocument.Parse(buffer.WrittenMemory);

        return canonical.RootElement.Clone();
    }

    /// <summary>Determines whether two canonical trees hold the same content.</summary>
    /// <param name="left">One tree.</param>
    /// <param name="right">The other tree.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    public static bool AreEqual(JsonElement left, JsonElement right)
        => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    /// <summary>Lists the JSON paths where two canonical trees differ.</summary>
    /// <param name="current">The tree the target holds.</param>
    /// <param name="incoming">The tree the import would write.</param>
    /// <returns>
    /// The paths, in property order, at most forty. A path is a member name chain with array indexes, for example
    /// <c>callableAgents[0].version</c>; a whole array that changed length is named by the array's own path.
    /// </returns>
    public static IReadOnlyList<string> ChangedPaths(JsonElement current, JsonElement incoming)
    {
        var paths = new List<string>();

        Compare(string.Empty, current, incoming, paths);

        return paths;
    }

    private static void Compare(string path, JsonElement current, JsonElement incoming, List<string> paths)
    {
        if (paths.Count >= MaxChangedPaths)
        {
            return;
        }

        if (current.ValueKind == JsonValueKind.Object && incoming.ValueKind == JsonValueKind.Object)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var property in current.EnumerateObject())
            {
                names.Add(property.Name);
            }

            foreach (var property in incoming.EnumerateObject())
            {
                names.Add(property.Name);
            }

            foreach (var name in names)
            {
                var child = path.Length == 0 ? name : path + "." + name;
                var hasCurrent = current.TryGetProperty(name, out var left);
                var hasIncoming = incoming.TryGetProperty(name, out var right);

                if (hasCurrent && hasIncoming)
                {
                    Compare(child, left, right, paths);
                }
                else if (paths.Count < MaxChangedPaths)
                {
                    paths.Add(child);
                }
            }

            return;
        }

        if (current.ValueKind == JsonValueKind.Array && incoming.ValueKind == JsonValueKind.Array
            && current.GetArrayLength() == incoming.GetArrayLength())
        {
            var index = 0;

            using var left = current.EnumerateArray();
            using var right = incoming.EnumerateArray();

            while (left.MoveNext() && right.MoveNext())
            {
                Compare($"{path}[{index}]", left.Current, right.Current, paths);
                index++;
            }

            return;
        }

        if (!AreEqual(current, incoming))
        {
            paths.Add(path.Length == 0 ? "$" : path);
        }
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    writer.WriteNumberValue(integer);
                }
                else
                {
                    writer.WriteNumberValue(element.GetDouble());
                }

                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
