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
internal static class AuditSecretFilter
{
    /// <summary>
    /// Name endings that mean "this field does not hold the value" even though
    /// the name carries one of the fragments in <see cref="CredentialHeaderNames"/>.
    /// </summary>
    /// <remarks>
    /// Each entry is measured, not assumed - the same standard the singular
    /// "token" exception was held to. A configuration-key reference holds the
    /// NAME of a configuration entry and never its value, which is the rule
    /// the whole code base follows for secrets, so redacting it removes the
    /// only useful thing in the record; five shipped properties
    /// have this shape (AuthorizationConfigurationKey,
    /// OAuthClientSecretConfigurationKey, SecretConfigurationKey,
    /// ApiKeyConfigurationName, SigningSecretConfigurationName). A name ending
    /// in "mode" classifies a flow: an MCP server record redacted
    /// "oauthAuthorizationMode":"AuthorizationCode" and a reader could no
    /// longer tell which OAuth flow the server used.
    /// </remarks>
    private static readonly string[] NonValueKeySuffixes =
    [
        "configurationkey",
        "configurationname",
        "mode",
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

                    if (IsConfigurationKeyMap(property.Name, property.Value))
                    {
                        WriteConfigurationKeyMap(property.Value, writer);
                    }
                    else if (IsSecretKey(property.Name) && CanHoldSecret(property.Value))
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

    /// <summary>
    /// Whether a value of this kind could carry a secret at all.
    /// </summary>
    /// <remarks>
    /// A null, a true or a false holds nothing to protect, and replacing it
    /// with "***" tells a reader that a secret is present where none is - the
    /// measured complaint was an audit record whose
    /// "authorizationConfigurationKey":null read back as "***". A number stays
    /// in scope on purpose: a one-time code is a number, and a name-based
    /// filter cannot tell one from a counter.
    /// </remarks>
    private static bool CanHoldSecret(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False);

    private static bool IsSecretKey(string propertyName)
    {
        // The fragments and the separator rule live in CredentialHeaderNames,
        // the same list the MCP and webhook header save rules read: two lists
        // would drift, and a name one of them catches would reach the other in
        // clear text. Free-form surfaces such as MCP server headers, agent
        // metadata and skill script arguments carry caller-chosen key names.
        var normalized = CredentialHeaderNames.Normalize(propertyName);

        foreach (var suffix in NonValueKeySuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return CredentialHeaderNames.ContainsSecretFragment(normalized);
    }

    /// <summary>
    /// Whether a property is the <c>headerConfigurationKeys</c> map of an MCP
    /// server or a webhook subscription: a header name to the NAME of the
    /// configuration key its value is read from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each entry is keyed by a credential-looking header name
    /// (<c>Authorization</c>, <c>X-Api-Key</c>) and would be redacted one level
    /// down, although no entry holds a value. Redacting them removes the only
    /// thing the trail can tell a reader: which key a header reads.
    /// </para>
    /// <para>
    /// The exemption is deliberately narrow. The property name must match
    /// exactly — a free-form surface such as agent metadata could otherwise
    /// name any object "...ConfigurationKeys" and carry a live value through —
    /// and only a string that looks like a configuration key path (it
    /// contains <c>:</c>, which every allowed prefix does) is kept as written.
    /// </para>
    /// </remarks>
    private static bool IsConfigurationKeyMap(string propertyName, JsonElement value)
        => value.ValueKind == JsonValueKind.Object
            && string.Equals(propertyName, "headerConfigurationKeys", StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes a configuration key map: key paths as they are, anything else redacted as usual.</summary>
    private static void WriteConfigurationKeyMap(JsonElement map, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        foreach (var entry in map.EnumerateObject())
        {
            writer.WritePropertyName(entry.Name);

            if (entry.Value.ValueKind == JsonValueKind.Null ||
                (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString()!.Contains(':', StringComparison.Ordinal)))
            {
                entry.Value.WriteTo(writer);
            }
            else if (IsSecretKey(entry.Name) && CanHoldSecret(entry.Value))
            {
                writer.WriteStringValue("***");
            }
            else
            {
                WriteRedacted(entry.Value, writer);
            }
        }

        writer.WriteEndObject();
    }
}
