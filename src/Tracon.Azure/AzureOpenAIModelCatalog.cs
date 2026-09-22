namespace Tracon;

/// <summary>Builds the catalog shown to the UI from the deployment definitions in the provider options.</summary>
/// <remarks>
/// <para>
/// <strong>Tracon carries no built-in model list.</strong> On Azure this rule
/// is even more binding: the name written in the catalog is not a <em>model</em>
/// name — it is a <em>deployment</em> name defined on that resource, and the
/// person who sets up the resource chooses deployment names. Two Tracon
/// consumers' catalogs need not resemble each other.
/// </para>
/// <para>
/// The catalog is <em>not a validation list</em>: a deployment name absent from
/// it can still be used. The catalog only feeds the UI's model selection screen
/// and cost calculation.
/// </para>
/// <example>
/// <code language="json">
/// "Tracon": { "Providers": { "AzureOpenAI": {
///   "Models": [
///     { "Name": "prod-gpt", "DisplayName": "Production (gpt-5.4-mini)", "ContextWindowTokens": 128000 }
///   ]
/// }}}
/// </code>
/// </example>
/// </remarks>
internal static class AzureOpenAIModelCatalog
{
    /// <summary>Builds the catalog from the deployment definitions in the options.</summary>
    /// <param name="options">The provider options.</param>
    /// <returns>A list sorted by name. An empty list when no definitions exist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the same name is defined more than once, the last definition wins.
    /// The comparison is case-insensitive. Nameless entries are ignored.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(AzureOpenAIProviderOptions options)
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
