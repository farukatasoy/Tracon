namespace Tracon;

/// <summary>
/// Builds the catalog shown to the UI from the model definitions in provider settings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tracon carries no built-in model list.</strong> Model names and
/// prices change far faster than a NuGet package's release cadence; a list baked
/// into the code goes stale quickly.
/// </para>
/// <para>
/// The catalog is <em>not a validation list</em>: a model name absent from it can
/// still be used — the provider sends the request to Anthropic as given. The
/// catalog only feeds the UI's model picker and cost calculation.
/// </para>
/// <example>
/// <code language="json">
/// "Tracon": { "Providers": { "Anthropic": {
///   "Models": [
///     { "Name": "claude-sonnet-5", "ContextWindowTokens": 200000, "InputCostPerMillionTokens": 3 }
///   ]
/// }}}
/// </code>
/// </example>
/// </remarks>
internal static class AnthropicModelCatalog
{
    /// <summary>Builds the catalog from the model definitions in settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <returns>The model list, sorted by name. Empty when there are no definitions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the same name is defined more than once, the last definition wins.
    /// Comparison is case-insensitive. Nameless entries are ignored.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Models.Count == 0)
        {
            return [];
        }

        var byName = new Dictionary<string, ModelDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in options.Models)
        {
            if (model is not null && !string.IsNullOrWhiteSpace(model.Name))
            {
                byName[model.Name] = model;
            }
        }

        var result = byName.Values.ToList();
        result.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return result;
    }
}
