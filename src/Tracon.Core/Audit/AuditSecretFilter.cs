using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Redacts potential secrets from <c>before</c> and <c>after</c> payloads before
/// they are written to the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// The filter operates on property <em>names</em>. A case-insensitive property
/// containing <c>apiKey</c>, <c>authorization</c>, <c>token</c>, <c>password</c>,
/// or <c>secret</c> has its value replaced with <c>"***"</c>, regardless of type.
/// </para>
/// <para>
/// The types audited today, such as <c>McpServerDefinition</c>, do not contain
/// secrets, but the filter still applies because a future type might.
/// </para>
/// <para>Does not use reflection; it operates manually with <c>Utf8JsonWriter</c> and <c>JsonDocument</c>.</para>
/// </remarks>
public static class AuditSecretFilter
{
    private static readonly string[] SecretKeyFragments =
    [
        "apikey",
        "authorization",
        "token",
        "password",
        "secret",
    ];

    /// <summary>Redacts secret fields in the supplied JSON text.</summary>
    /// <param name="json">The raw JSON text. Returns it unchanged when <see langword="null"/>.</param>
    /// <returns>The redacted JSON text.</returns>
    public static string? Redact(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Invalid JSON, such as manually formatted run_events.payload text, is
            // left unchanged because the filter can only traverse JSON objects.
            return json;
        }

        using (document)
        {
            var buffer = new MemoryStream();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteRedacted(document.RootElement, writer);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);

                    if (IsSecretKey(property.Name))
                    {
                        writer.WriteStringValue("***");
                    }
                    else
                    {
                        WriteRedacted(property.Value, writer);
                    }
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteRedacted(item, writer);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSecretKey(string propertyName)
    {
        // 🚨 Separators are stripped before matching. The fragments are written
        // without them, so "apiKey" matched but "x-api-key", "xi-api-key" and
        // "api_key" did not - the separator broke the "apikey" run apart and a
        // live provider key reached audit_log.after in clear text. Those exact
        // spellings are real in this code base (Anthropic and ElevenLabs both
        // use them), and free-form surfaces such as MCP server headers, agent
        // metadata and skill script arguments carry caller-chosen key names.
        var normalized = Normalize(propertyName);

        foreach (var fragment in SecretKeyFragments)
        {
            if (!normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A singular "token" is an authentication value, such as authToken
            // or accessToken, while the plural is a count, such as
            // maxOutputTokens, maxContextWindowTokens, or totalTokens. Redacting
            // the latter would make every agent audit record needlessly empty.
            // This was observed in a real agent.create record from the /tracon sample.
            if (string.Equals(fragment, "token", StringComparison.Ordinal) &&
                normalized.Contains("tokens", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Removes the characters that only separate words in a key name.</summary>
    /// <remarks>
    /// Keeps letters and digits, drops everything else, so <c>x-api-key</c>,
    /// <c>api_key</c> and <c>API.KEY</c> all reduce to the same run of letters
    /// the fragments are written in.
    /// </remarks>
    private static string Normalize(string propertyName)
    {
        var needsWork = false;

        foreach (var character in propertyName)
        {
            if (!char.IsLetterOrDigit(character))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return propertyName;
        }

        return string.Create(propertyName.Length, propertyName, static (span, source) =>
        {
            var length = 0;

            foreach (var character in source)
            {
                if (char.IsLetterOrDigit(character))
                {
                    span[length++] = character;
                }
            }

            span[length..].Fill(' ');
        }).TrimEnd();
    }
}
