using System.Text.Json;
using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="EvalSuite.Checks"/> icindeki bildirimsel denetim tanimlarini
/// <see cref="EvalCheck"/> temsillerine cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Yerlesik alti denetim turu dogrudan <c>Microsoft.Agents.AI.EvalChecks</c>
/// fabrikalarina eslenir: <c>nonEmpty</c>, <c>containsExpected</c>,
/// <c>keywords</c>, <c>toolCalled</c>, <c>toolCallsPresent</c>,
/// <c>hasImageContent</c>. Bunlarin disindaki bir tur adi
/// <c>IAgentPrismBuilder.AddEvalCheck(...)</c> ile kaydedilmis ozel bir
/// denetimde aranir; orada da yoksa <see cref="AgentPrismException"/> firlatilir
/// (K2 — denetimler bildirimseldir, sessizce yok sayilmaz).
/// </para>
/// <para>
/// <c>toolCallArgsMatch</c> kasitli olarak desteklenmez: <see cref="EvalCase"/>
/// yalnizca beklenen tool <em>adlarini</em> tasir, tam arguman eslesmesi icin
/// gereken beklenen argumanlari tasimaz.
/// </para>
/// </remarks>
public sealed class EvalCheckRegistry
{
    private readonly Dictionary<string, EvalCheck> _custom;

    /// <summary>Kayitlardan yeni bir defter olusturur.</summary>
    /// <param name="registrations">Ozel denetim kayitlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Ayni tur adi birden cok kez kaydedilmisse.</exception>
    public EvalCheckRegistry(IEnumerable<AgentPrismEvalCheckRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _custom = new Dictionary<string, EvalCheck>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (!_custom.TryAdd(registration.Kind, registration.Check))
            {
                throw new AgentPrismException(
                    $"'{registration.Kind}' adinda birden cok ozel eval denetimi kaydedilmis. " +
                    "Denetim tur adlari benzersiz olmalidir.");
            }
        }
    }

    /// <summary>
    /// Bir takimin <see cref="EvalSuite.Checks"/> alanini <see cref="EvalCheck"/>
    /// dizisine cevirir.
    /// </summary>
    /// <param name="checks">Denetim tanimlarini tasiyan JSON dizisi.</param>
    /// <returns>Sirali denetim listesi. Yuk tanimsizsa veya bossa bos liste doner.</returns>
    /// <exception cref="AgentPrismException">
    /// Bir tanim <c>kind</c> alani tasimiyorsa veya bilinmeyen bir tur adina isaret ediyorsa.
    /// </exception>
    public IReadOnlyList<EvalCheck> BuildChecks(JsonElement checks)
    {
        if (checks.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        if (checks.ValueKind != JsonValueKind.Array)
        {
            throw new AgentPrismException("'checks' alani bir JSON dizisi olmalidir.");
        }

        var result = new List<EvalCheck>();

        foreach (var spec in checks.EnumerateArray())
        {
            result.Add(BuildCheck(spec));
        }

        return result;
    }

    private EvalCheck BuildCheck(JsonElement spec)
    {
        if (!spec.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
        {
            throw new AgentPrismException("Her denetim tanimi bir 'kind' (metin) alani tasimalidir.");
        }

        var kind = kindElement.GetString()!;

        switch (kind)
        {
            case "nonEmpty":
                return EvalChecks.NonEmpty(GetInt(spec, "minLength", 1));

            case "containsExpected":
                return EvalChecks.ContainsExpected(GetBool(spec, "caseSensitive", false));

            case "keywords":
                var keywords = GetStringArray(spec, "values");
                return spec.TryGetProperty("caseSensitive", out _)
                    ? EvalChecks.KeywordCheck(GetBool(spec, "caseSensitive", false), keywords)
                    : EvalChecks.KeywordCheck(keywords);

            case "toolCalled":
                var tools = GetStringArray(spec, "tools");
                var mode = GetString(spec, "mode", "all") switch
                {
                    "any" => ToolCalledMode.Any,
                    "all" => ToolCalledMode.All,
                    var other => throw new AgentPrismException(
                        $"'{other}' gecerli bir 'toolCalled' modu degil. Gecerli degerler: 'all', 'any'."),
                };
                return EvalChecks.ToolCalledCheck(mode, tools);

            case "toolCallsPresent":
                return EvalChecks.ToolCallsPresent();

            case "hasImageContent":
                return EvalChecks.HasImageContent();

            default:
                if (_custom.TryGetValue(kind, out var custom))
                {
                    return custom;
                }

                throw new AgentPrismException(
                    $"Bilinmeyen denetim turu: '{kind}'. Ozel bir denetimse " +
                    "'IAgentPrismBuilder.AddEvalCheck(\"{kind}\", ...)' ile kaydedilmelidir.");
        }
    }

    private static int GetInt(JsonElement spec, string property, int fallback)
        => spec.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    private static bool GetBool(JsonElement spec, string property, bool fallback)
        => spec.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string GetString(JsonElement spec, string property, string fallback)
        => spec.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : fallback;

    private static string[] GetStringArray(JsonElement spec, string property)
    {
        if (!spec.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new AgentPrismException($"Denetim tanimi bir '{property}' dizisi tasimalidir.");
        }

        return [.. value.EnumerateArray().Select(static element => element.GetString()!)];
    }
}
