using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Denetim izine yazilmadan once <c>before</c>/<c>after</c> yuklerinden olasi
/// sirlari temizler.
/// </summary>
/// <remarks>
/// <para>
/// Suzgec anahtar <em>adina</em> gore calisir: <c>apiKey</c>, <c>authorization</c>,
/// <c>token</c>, <c>password</c>, <c>secret</c> iceren (buyuk/kucuk harfe duyarsiz)
/// bir anahtarin degeri, tipi ne olursa olsun <c>"***"</c> ile degistirilir.
/// </para>
/// <para>
/// Bugun denetlenen tipler (<c>McpServerDefinition</c> gibi) zaten sir tasimaz
/// (karar K-059), ama suzgec yine de uygulanir — sonraki bir tip taşıyabilir.
/// </para>
/// <para>Yansima kullanmaz; <c>Utf8JsonWriter</c>/<c>JsonDocument</c> ile elle calisir.</para>
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

    /// <summary>Verilen JSON metnindeki sir alanlarini temizler.</summary>
    /// <param name="json">Ham JSON metni. <see langword="null"/> ise oldugu gibi doner.</param>
    /// <returns>Temizlenmis JSON metni.</returns>
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
            // Gecerli JSON degilse (ornek: run_events.payload gibi elle bicimlendirilmis
            // metin) oldugu gibi birakilir; suzgec yalnizca JSON nesnelerini gezebilir.
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
        foreach (var fragment in SecretKeyFragments)
        {
            if (!propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // "token" tek basina bir kimlik dogrulama degeridir (authToken,
            // accessToken), ama COGULU bir sayimdir (maxOutputTokens,
            // maxContextWindowTokens, totalTokens). Ikincisini sir sanip
            // gereksiz yere gizlemek, her agent denetim kaydini anlamsizca
            // bosaltirdi. Olculdu: /agentprism ornek uygulamasinda gercek bir
            // agent.create kaydinda "maxOutputTokens":"***" gorundu.
            if (string.Equals(fragment, "token", StringComparison.Ordinal) &&
                propertyName.Contains("tokens", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
