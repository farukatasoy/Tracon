namespace Tracon;

/// <summary>
/// Finds the models of a binding whose catalog entry states that they reject
/// the sampling parameters (<see cref="ModelDescriptor.SupportsSampling"/> is
/// <see langword="false"/>), when the binding sets one of them.
/// </summary>
/// <remarks>
/// <para>
/// One rule for two readers: <see cref="AgentDefinitionCompiler"/> stops the
/// compilation, and <see cref="AgentDefinitionValidator"/> reports the same
/// finding with the offending field's path. Two copies of the rule could
/// disagree about a fallback link.
/// </para>
/// <para>
/// The binding's <see cref="ModelBinding.Fallbacks"/> links are checked too:
/// <c>FallbackChatClient</c> sends a link the same <c>ChatOptions</c>, only with
/// its own model name, so a link that rejects sampling fails with the same
/// <c>400</c>. A model absent from the catalog, or with the flag left
/// <see langword="null"/>, is not checked — the catalog is metadata, not a
/// validation list.
/// </para>
/// </remarks>
internal static class SamplingCapabilityCheck
{
    /// <summary>Lists the <c>provider/model</c> names that reject sampling, primary first.</summary>
    /// <param name="registry">The registry whose catalog is read.</param>
    /// <param name="binding">The binding.</param>
    /// <returns>An empty list when the binding sets no sampling parameter or every model accepts it.</returns>
    public static IReadOnlyList<string> ModelsRejectingSampling(IModelProviderRegistry registry, ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Temperature is null && binding.TopP is null)
        {
            return [];
        }

        List<string>? rejecting = null;

        if (RejectsSampling(registry, binding.Provider, binding.Model))
        {
            (rejecting ??= []).Add($"{binding.Provider}/{binding.Model}");
        }

        foreach (var link in binding.Fallbacks)
        {
            if (RejectsSampling(registry, link.Provider, link.Model))
            {
                (rejecting ??= []).Add($"{link.Provider}/{link.Model}");
            }
        }

        return rejecting ?? [];
    }

    private static bool RejectsSampling(IModelProviderRegistry registry, string provider, string model)
        => ModelCatalogLookup.Find(registry, provider, model) is { SupportsSampling: false };
}
