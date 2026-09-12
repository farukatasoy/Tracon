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
    {
        foreach (var providerDescriptor in registry.List())
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
