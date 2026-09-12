namespace Tracon;

/// <summary>
/// Builds the catalog shown in the UI from the model definitions in provider settings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tracon carries no built-in model list</strong>.
/// This had a concrete cost with Gemini: measured, a call to
/// <c>gemini-2.5-flash</c> returned <em>"This model is no longer available to new
/// users"</em>. A list embedded in code can be wrong the very day it ships.
/// </para>
/// <para>
/// The catalog <em>is not a validation list</em>: a model name absent from it can
/// still be used. The catalog only feeds the UI's model selection screen and cost
/// estimates.
/// </para>
/// </remarks>
public static class GoogleModelCatalog
{
    /// <summary>Builds the catalog from the model definitions in settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <returns>Model list sorted by name. Empty list when there are no definitions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the same name is defined more than once, the last definition wins.
    /// Comparison is case-insensitive. Unnamed entries are ignored.
    /// </remarks>
    public static IReadOnlyList<ModelDescriptor> Build(GoogleProviderOptions options)
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
