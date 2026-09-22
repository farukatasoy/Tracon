namespace Tracon;

/// <summary>
/// Resolves the instructions text a run should use from an
/// <see cref="AgentDefinition"/>'s culture dictionary.
/// </summary>
/// <remarks>
/// Resolution order: the requested culture -&gt; its parent subtag (<c>"tr-TR"</c> -&gt;
/// <c>"tr"</c>) -&gt; <see cref="AgentDefinition.Instructions"/>. An unmatched culture
/// never fails compilation; it silently falls back to the default text.
/// Matching is case-insensitive: BCP-47 tags are conventionally lowercase, but a
/// definition authored by hand may not be.
/// </remarks>
internal static class InstructionCultureResolver
{
    /// <summary>Resolves the instructions text for a definition and a requested culture.</summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <param name="culture">The requested culture; <see langword="null"/> or empty uses the default.</param>
    /// <returns>
    /// The matched culture's text, or <see cref="AgentDefinition.Instructions"/> when no
    /// entry matches (or the definition carries no culture dictionary).
    /// </returns>
    public static string? Resolve(AgentDefinition definition, string? culture)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(culture) || definition.InstructionsByCulture is not { Count: > 0 } map)
        {
            return definition.Instructions;
        }

        if (TryFind(map, culture, out var exact))
        {
            return exact;
        }

        var separator = culture.IndexOf('-');

        if (separator > 0 && TryFind(map, culture[..separator], out var parent))
        {
            return parent;
        }

        return definition.Instructions;
    }

    private static bool TryFind(IReadOnlyDictionary<string, string> map, string culture, out string text)
    {
        foreach (var (key, value) in map)
        {
            if (string.Equals(key, culture, StringComparison.OrdinalIgnoreCase))
            {
                text = value;
                return true;
            }
        }

        text = string.Empty;
        return false;
    }
}
