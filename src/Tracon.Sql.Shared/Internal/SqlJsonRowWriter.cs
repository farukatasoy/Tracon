using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Converts a <see cref="DbDataReader"/> row to a single-line JSON object without
/// knowing the column schema up front.
/// </summary>
/// <remarks>
/// Shared by <c>SqlRetentionStore</c> (archive rows) and
/// <c>SqlDataSubjectStore</c> (export documents) — both need to turn an
/// arbitrary row into JSON without reflection, because this code compiles into
/// <c>Tracon.PostgreSql</c> and that package must stay AOT-compatible.
/// </remarks>
internal static class SqlJsonRowWriter
{
    /// <summary>Writes every column of the current row as a JSON object.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="reader">The reader, positioned on a row.</param>
    public static void WriteRow(Utf8JsonWriter writer, DbDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(reader);

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

    /// <summary>Converts the current row to a single-line JSON object (one line of JSONL).</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <returns>The JSON text.</returns>
    public static string RowToJson(DbDataReader reader)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRow(writer, reader);
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
                // A rare provider-specific type (e.g. DateOnly): stays readable
                // as its string representation without loss.
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));

                break;
        }
    }
}
