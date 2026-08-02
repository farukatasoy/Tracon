using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Script argumanlarini bildirilen JSON Schema'ya karsi <em>yuzeysel</em> denetler.
/// </summary>
/// <remarks>
/// <para>
/// Denetim bilerek sinirlidir: kok nesnenin varligi, <c>required</c> listesindeki
/// alanlarin bulunmasi ve <c>properties</c> altindaki alanlarin ust duzey tipi.
/// Ic ice sema, <c>oneOf</c>, <c>pattern</c> ve benzeri kurallar
/// <strong>denetlenmez</strong>.
/// </para>
/// <para>
/// Tam bir JSON Schema dogrulayicisi ek bir NuGet bagimliligi gerektirirdi.
/// AgentPrism bir kutuphanedir; tuketicinin bagimlilik grafigine yalnizca
/// argumanlari sinirlamak icin yeni bir paket eklemek dogru degildir. Asil
/// guvenlik siniri arguman semasi degil, sureci ve ortami sinirlamaktir.
/// </para>
/// </remarks>
internal static class SkillScriptArgumentValidator
{
    /// <summary>Argumanlari denetler; uygun degilse sebebi doner.</summary>
    /// <param name="schema">JSON Schema nesnesi. <see langword="null"/> ise denetim yapilmaz.</param>
    /// <param name="arguments">Modelin urettigi argumanlar.</param>
    /// <param name="error">Uygun degilse hata sebebi.</param>
    /// <returns>Argumanlar uygunsa <see langword="true"/>.</returns>
    public static bool TryValidate(JsonElement? schema, JsonElement? arguments, out string? error)
    {
        error = null;

        if (schema is not { ValueKind: JsonValueKind.Object } schemaElement)
        {
            return true;
        }

        if (arguments is not { } argumentsElement || argumentsElement.ValueKind == JsonValueKind.Null)
        {
            argumentsElement = default;
        }

        if (argumentsElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
        {
            error = "Script argumanlari bir JSON nesnesi olmalidir.";
            return false;
        }

        if (schemaElement.TryGetProperty("required", out var required) &&
            required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = item.GetString()!;

                if (argumentsElement.ValueKind == JsonValueKind.Undefined ||
                    !argumentsElement.TryGetProperty(name, out _))
                {
                    error = $"Zorunlu '{name}' argumani eksik.";
                    return false;
                }
            }
        }

        if (argumentsElement.ValueKind == JsonValueKind.Undefined ||
            !schemaElement.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        foreach (var property in argumentsElement.EnumerateObject())
        {
            if (!properties.TryGetProperty(property.Name, out var propertySchema) ||
                propertySchema.ValueKind != JsonValueKind.Object ||
                !propertySchema.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!Matches(type.GetString(), property.Value.ValueKind))
            {
                error = $"'{property.Name}' argumani '{type.GetString()}' tipinde olmalidir.";
                return false;
            }
        }

        return true;
    }

    private static bool Matches(string? type, JsonValueKind kind) => type switch
    {
        "string" => kind == JsonValueKind.String,
        "number" or "integer" => kind == JsonValueKind.Number,
        "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
        "object" => kind == JsonValueKind.Object,
        "array" => kind == JsonValueKind.Array,
        "null" => kind == JsonValueKind.Null,
        _ => true,
    };
}
