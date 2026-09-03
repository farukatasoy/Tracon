using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Bounds and normalizes a provider's raw voice metadata into
/// <see cref="VoiceDescriptor.Attributes"/>.
/// </summary>
/// <remarks>
/// Pure and stateless: safe to call concurrently. Only scalar string values
/// survive — a numeric, object, array or null label is skipped rather than
/// carried, and neither <c>preview_url</c> nor any provider secret ever
/// reaches this mapper's inputs (<c>VoiceEndpoints</c>'s caller never passes them).
/// </remarks>
internal static class VoiceAttributeMapper
{
    /// <summary>The most attributes a single voice can carry.</summary>
    internal const int MaxAttributes = 32;

    /// <summary>The longest allowed attribute key.</summary>
    internal const int MaxKeyLength = 64;

    /// <summary>The longest allowed attribute value.</summary>
    internal const int MaxValueLength = 256;

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    /// <summary>
    /// Maps ElevenLabs' <c>labels</c> object and <c>verified_languages</c> array
    /// into a bounded, safe attribute set.
    /// </summary>
    /// <param name="labels">
    /// The provider's raw <c>labels</c> dictionary. Values are inspected by
    /// <see cref="JsonElement.ValueKind"/> rather than trusted as strings: the
    /// provider's schema promises scalars, but a malformed or future response
    /// must not crash deserialization of the whole voice list.
    /// </param>
    /// <param name="verifiedLanguages">
    /// The provider's <c>verified_languages</c> array. <c>labels</c> does NOT
    /// carry a language key — measured against ElevenLabs' published OpenAPI
    /// document (<c>VoiceResponseModel</c>/<c>VerifiedVoiceLanguageResponseModel</c>,
    /// 2026-09-03): language lives in this separate, per-model-verified array
    /// instead. A voice can be verified for more than one language; the
    /// distinct set is joined into one comma-separated scalar since
    /// <see cref="VoiceDescriptor.Attributes"/> holds one string per key.
    /// </param>
    public static IReadOnlyDictionary<string, string> Map(
        Dictionary<string, JsonElement>? labels,
        List<ElevenLabsVerifiedLanguage>? verifiedLanguages)
    {
        Dictionary<string, string>? result = null;

        if (labels is { Count: > 0 })
        {
            foreach (var (key, value) in labels)
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    TryAdd(ref result, key, value.GetString());
                }
            }
        }

        if (verifiedLanguages is { Count: > 0 })
        {
            var languages = verifiedLanguages
                .Select(static entry => entry.Language)
                .Where(static language => !string.IsNullOrEmpty(language))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.Ordinal)
                .ToArray();

            if (languages.Length > 0)
            {
                TryAdd(ref result, VoiceAttributeNames.Language, string.Join(",", languages));
            }
        }

        return result is not { Count: > 0 } ? Empty : result;
    }

    private static void TryAdd(ref Dictionary<string, string>? result, string? key, string? value)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength ||
            string.IsNullOrEmpty(value) || value.Length > MaxValueLength)
        {
            return;
        }

        var canonicalKey = key.ToLowerInvariant().Replace('_', '-');

        result ??= new Dictionary<string, string>(StringComparer.Ordinal);

        if (!result.ContainsKey(canonicalKey) && result.Count >= MaxAttributes)
        {
            return;
        }

        result.TryAdd(canonicalKey, value);
    }
}
