namespace Tracon;

/// <summary>
/// Resolves the instructions text a run should use from an
/// <see cref="AgentDefinition"/>'s culture dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Resolution order: the requested culture -&gt; its parent subtag (<c>"tr-TR"</c> -&gt;
/// <c>"tr"</c>) -&gt; <see cref="AgentDefinition.Instructions"/>. An unmatched culture
/// never fails compilation; it silently falls back to the default text.
/// Matching is case-insensitive: BCP-47 tags are conventionally lowercase, but a
/// definition authored by hand may not be.
/// </para>
/// <para>
/// A blank requested culture is the same as none, and a blank parent subtag (<c>" -x"</c>) matches nothing, so a
/// dictionary key made of white space is never selected.
/// </para>
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

        return TryMatch(definition, culture, out _, out var text) ? text : definition.Instructions;
    }

    /// <summary>
    /// Names the dictionary entry <see cref="Resolve"/> selects for a requested culture: the single value that
    /// stands for every request resolving to that entry.
    /// </summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <param name="culture">The requested culture; <see langword="null"/> or empty uses the default.</param>
    /// <returns>
    /// The key of the entry that matches, exactly as the dictionary spells it, or <see cref="string.Empty"/> when
    /// the default text is used. <c>Resolve(definition, Canonical(definition, culture))</c> is the same text as
    /// <c>Resolve(definition, culture)</c>, so a caller that keys a cache on the instructions text keys it on this
    /// value instead of on a string the requester chose.
    /// </returns>
    /// <remarks>
    /// A request body carries the culture as free text. Kept as written, every distinct string would be a distinct
    /// cache slot for the same compiled text; this bounds the slots of a definition to the entries of its
    /// dictionary plus the default.
    /// </remarks>
    public static string Canonical(AgentDefinition definition, string? culture)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return TryMatch(definition, culture, out var key, out _) ? key : string.Empty;
    }

    private static bool TryMatch(AgentDefinition definition, string? culture, out string key, out string text)
    {
        key = string.Empty;
        text = string.Empty;

        if (string.IsNullOrWhiteSpace(culture) || definition.InstructionsByCulture is not { Count: > 0 } map)
        {
            return false;
        }

        if (TryFind(map, culture, out key, out text))
        {
            return true;
        }

        var separator = culture.IndexOf('-');

        return separator > 0
            && !culture.AsSpan(0, separator).IsWhiteSpace()
            && TryFind(map, culture[..separator], out key, out text);
    }

    private static bool TryFind(IReadOnlyDictionary<string, string> map, string culture, out string key, out string text)
    {
        foreach (var (candidate, value) in map)
        {
            if (string.Equals(candidate, culture, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                text = value;
                return true;
            }
        }

        key = string.Empty;
        text = string.Empty;
        return false;
    }
}
