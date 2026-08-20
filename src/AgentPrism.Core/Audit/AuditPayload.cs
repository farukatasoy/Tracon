using System.Buffers;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

// 🚨 This helper exists because audit payloads were built by string
// interpolation. A value carrying a double quote produced INVALID JSON, and the
// damage was silent and provider-dependent:
//
//   1. AuditSecretFilter.Redact catches JsonException and returns the text
//      UNCHANGED, so redaction was skipped as well.
//   2. On PostgreSQL the `after` column is jsonb; the invalid text failed the
//      cast with 22P02.
//   3. AuditRecorder swallows store failures, so the row was simply dropped
//      while the mutation itself had already been applied.
//
// The result was a tenant-visible change with no audit row, and
// GET /api/audit/verify could not see it: the hash chain only links rows that
// exist, and a row never written leaves no gap. On SQLite and SQL Server the
// column is plain text, so the row was written instead - carrying whatever JSON
// fields the caller's value injected.
//
// Build audit payloads through this type. Do not interpolate JSON by hand.

/// <summary>Builds a small JSON object for the audit trail.</summary>
public static class AuditPayload
{
    /// <summary>Writes a JSON object and returns it as text.</summary>
    /// <param name="writeBody">Writes the object's properties. The braces are supplied.</param>
    /// <returns>The serialized object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writeBody"/> is <see langword="null"/>.</exception>
    public static string Write(Action<Utf8JsonWriter> writeBody)
    {
        ArgumentNullException.ThrowIfNull(writeBody);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Writes a JSON object holding one array of strings.</summary>
    /// <param name="propertyName">The array property's name.</param>
    /// <param name="values">The values; each one is escaped.</param>
    /// <returns>The serialized object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public static string WriteArray(string propertyName, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return Write(writer =>
        {
            writer.WriteStartArray(propertyName);

            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        });
    }
}
