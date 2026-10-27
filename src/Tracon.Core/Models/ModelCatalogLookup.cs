namespace Tracon;

/// <summary>Looks up a single model's catalog entry across registered providers.</summary>
/// <remarks>
/// Shared by <see cref="AgentDefinitionCompiler"/> (structured-output and
/// context-window checks) and <see cref="ContextWindowEstimator"/>
/// so the two never drift on how a binding resolves to a
/// <see cref="ModelDescriptor"/>.
/// </remarks>
internal static class ModelCatalogLookup
{
    /// <summary>Finds the descriptor for <paramref name="provider"/>/<paramref name="model"/>.</summary>
    /// <returns>
    /// The descriptor, or <see langword="null"/> when the provider or the
    /// model is not in the catalog — a missing entry is not an error (
    /// the catalog is not a validation list).
    /// </returns>
    public static ModelDescriptor? Find(IModelProviderRegistry registry, string provider, string model)
        => Find(registry.List(), provider, model);

    /// <summary>Finds the descriptor in a provider list the caller already holds.</summary>
    /// <param name="providers">The listed providers.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <returns>The descriptor, or <see langword="null"/> when the provider or the model is not in the catalog.</returns>
    public static ModelDescriptor? Find(IReadOnlyList<ModelProviderDescriptor> providers, string provider, string model)
    {
        foreach (var providerDescriptor in providers)
        {
            if (!string.Equals(providerDescriptor.Name, provider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var descriptor in providerDescriptor.Models)
            {
                if (string.Equals(descriptor.Name, model, StringComparison.OrdinalIgnoreCase))
                {
                    return descriptor;
                }
            }
        }

        return null;
    }
}
