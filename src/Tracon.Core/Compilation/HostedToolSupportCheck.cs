namespace Tracon;

/// <summary>
/// Finds the hosted tools a definition selects that the provider's API surface would drop.
/// </summary>
/// <remarks>
/// <para>
/// The model adapters map only some hosted tools to the wire and drop the rest without an error: a
/// definition that selects a code interpreter for the Chat Completions surface runs, and the model
/// answers as if it had no interpreter. A provider states what its surface runs through
/// <see cref="IHostedToolSupport"/>; a provider that states nothing runs none.
/// </para>
/// <para>
/// The sibling of <see cref="SamplingSupportCheck"/>, with the same shape: one rule for two readers (the
/// compiler stops the compilation, the validator reports each finding with the path of the tool name),
/// the binding's <see cref="ModelBinding.Fallbacks"/> links are checked because a link receives the
/// primary call's tools, and a provider that is not registered is not checked here (the model check
/// reports it).
/// </para>
/// </remarks>
internal static class HostedToolSupportCheck
{
    /// <summary>A hosted tool the definition selects and a provider would drop.</summary>
    /// <param name="Index">The index of the tool name in <see cref="AgentDefinition.ToolNames"/>.</param>
    /// <param name="ToolName">The registered name.</param>
    /// <param name="Kind">The kind of the tool.</param>
    /// <param name="Provider">The provider that would drop it.</param>
    /// <param name="SupportedBy">The registered providers that run the kind.</param>
    internal sealed record Finding(
        int Index,
        string ToolName,
        HostedToolKind Kind,
        string Provider,
        IReadOnlyList<string> SupportedBy)
    {
        /// <summary>Gets the JSON path of the tool name in the definition.</summary>
        public string Path => $"toolNames[{Index}]";

        /// <summary>Gets the sentence that names the tool, its kind, the provider and the providers that run the kind.</summary>
        public string Describe()
        {
            var alternative = SupportedBy.Count == 0
                ? "No registered provider runs it."
                : $"Providers that run it: {string.Join(", ", SupportedBy)}.";

            return $"The hosted tool '{ToolName}' ({Kind}) is selected, but the '{Provider}' provider does not run it " +
                   "(its API surface drops the tool or the provider rejects it, so the model would answer without it " +
                   $"or the call would fail). Remove the tool or use a provider that runs it. {alternative}";
        }
    }

    /// <summary>Lists the selected tools the primary provider or a fallback link would drop.</summary>
    /// <param name="registry">The registry whose providers are read.</param>
    /// <param name="binding">The binding.</param>
    /// <param name="selected">The hosted tools the definition selects.</param>
    /// <returns>An empty list when no tool is selected or every provider runs it.</returns>
    public static IReadOnlyList<Finding> Unsupported(
        IModelProviderRegistry registry,
        ModelBinding binding,
        IReadOnlyList<(int Index, HostedToolRegistration Registration)> selected)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(selected);

        if (selected.Count == 0)
        {
            return [];
        }

        var providers = registry.List();
        var findings = new List<Finding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Collect(providers, binding.Provider, selected, seen, findings);

        foreach (var link in binding.Fallbacks)
        {
            Collect(providers, link.Provider, selected, seen, findings);
        }

        return findings;
    }

    /// <summary>Gets the flag that stands for a kind in a provider's set.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The flag.</returns>
    public static HostedToolKinds FlagOf(HostedToolKind kind) => kind switch
    {
        HostedToolKind.WebSearch => HostedToolKinds.WebSearch,
        HostedToolKind.CodeInterpreter => HostedToolKinds.CodeInterpreter,
        HostedToolKind.FileSearch => HostedToolKinds.FileSearch,
        _ => HostedToolKinds.None,
    };

    private static void Collect(
        IReadOnlyList<ModelProviderDescriptor> providers,
        string provider,
        IReadOnlyList<(int Index, HostedToolRegistration Registration)> selected,
        HashSet<string> seen,
        List<Finding> findings)
    {
        var descriptor = providers.FirstOrDefault(
            candidate => string.Equals(candidate.Name, provider, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            return;
        }

        foreach (var (index, registration) in selected)
        {
            var flag = FlagOf(registration.Kind);

            if ((descriptor.SupportedHostedTools & flag) != HostedToolKinds.None || !seen.Add($"{descriptor.Name}\n{index}"))
            {
                continue;
            }

            var supportedBy = providers
                .Where(candidate => (candidate.SupportedHostedTools & flag) != HostedToolKinds.None)
                .Select(static candidate => candidate.Name)
                .ToList();

            findings.Add(new Finding(index, registration.Name, registration.Kind, descriptor.Name, supportedBy));
        }
    }
}
