namespace AgentPrism;

/// <summary>
/// Builds the catalog shown in the user interface from the model definitions in the
/// provider options.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism carries no built-in model list.</strong> This is a deliberate
/// decision: OpenAI model names and prices change much faster than a NuGet package is
/// released. A list embedded in code becomes misleading in a short time.
/// </para>
/// <para>
/// Measured (2026-08-02): the built-in list written during phase 3 contained none of the
/// models a real account could reach; a call to <c>gpt-4.1-mini</c> from that list
/// returned <c>HTTP 403 model_not_found</c>. Reason:
/// <c>docs/KARARLAR.md</c>, decision K-032.
/// </para>
/// <para>
/// The catalog comes from the <see cref="OpenAIProviderOptions.Models"/> option. The
/// catalog is <em>not a validation list</em>: a model name that is absent here can still
/// be used, and the provider sends the request to OpenAI as it is. The catalog only
/// feeds the model picker screen and the cost calculation of the user interface.
/// </para>
/// <example>
/// <code language="json">
/// "AgentPrism": { "Providers": { "OpenAI": {
///   "Models": [
///     { "Name": "gpt-5.4-mini", "ContextWindowTokens": 400000, "InputCostPerMillionTokens": 0.25 }
///   ]
/// }}}
/// </code>
/// </example>
/// </remarks>
public static class OpenAIModelCatalog
{
    /// <summary>Builds the catalog from the model definitions in the options.</summary>
    /// <param name="options">Provider options.</param>
    /// <returns>The models ordered by name. An empty list when there is no definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the same name is defined more than once, the last definition wins. The
    /// comparison is case insensitive. Entries without a name are ignored.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(OpenAIProviderOptions options)
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
