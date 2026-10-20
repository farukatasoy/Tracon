namespace Tracon;

/// <summary>
/// Finds the request fields a binding sets that the provider's API surface would drop.
/// </summary>
/// <remarks>
/// <para>
/// The model adapters send only some of the <see cref="SamplingFields"/> and drop the
/// rest without an error: a definition that sets <see cref="ModelBinding.Seed"/> for the
/// Responses API runs, and the seed never reaches the model. A provider states what its
/// surface sends through <see cref="ISamplingSupport"/>; a provider that states nothing
/// supports none of the fields.
/// </para>
/// <para>
/// One rule for two readers, as <see cref="SamplingCapabilityCheck"/>: the compiler stops
/// the compilation and the validator reports the same finding with the field's path. The
/// binding's <see cref="ModelBinding.Fallbacks"/> links are checked too, because a link
/// receives the primary call's request options. A provider that is not registered is not
/// checked here; the model check reports it.
/// </para>
/// </remarks>
internal static class SamplingSupportCheck
{
    /// <summary>A field the binding sets and a provider would drop.</summary>
    /// <param name="Field">The field.</param>
    /// <param name="Provider">The provider name.</param>
    /// <param name="SupportedBy">The registered providers that send the field.</param>
    internal sealed record Finding(SamplingFields Field, string Provider, IReadOnlyList<string> SupportedBy)
    {
        /// <summary>Gets the <see cref="ModelBinding"/> member name of the field.</summary>
        public string Member => MemberOf(Field);

        /// <summary>Gets the JSON path of the field in the definition.</summary>
        public string Path => PathOf(Field);

        /// <summary>Gets the sentence that names the field, the provider and the providers that send it.</summary>
        public string Describe()
        {
            var alternative = SupportedBy.Count == 0
                ? "No registered provider sends it."
                : $"Providers that send it: {string.Join(", ", SupportedBy)}.";

            return $"{nameof(ModelBinding)}.{Member} is set, but the '{Provider}' provider does not send it to the model " +
                   $"(its API surface drops it, so the request would run without it). Remove it from the definition " +
                   $"or use a provider that sends it. {alternative}";
        }
    }

    /// <summary>Lists the fields the binding sets that the primary provider or a fallback link would drop.</summary>
    /// <param name="registry">The registry whose providers are read.</param>
    /// <param name="binding">The binding.</param>
    /// <returns>An empty list when the binding sets no such field or every provider sends it.</returns>
    public static IReadOnlyList<Finding> Unsupported(IModelProviderRegistry registry, ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(binding);

        var requested = Requested(binding);

        if (requested == SamplingFields.None)
        {
            return [];
        }

        var providers = registry.List();
        var findings = new List<Finding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Collect(providers, binding.Provider, requested, seen, findings);

        foreach (var link in binding.Fallbacks)
        {
            Collect(providers, link.Provider, requested, seen, findings);
        }

        return findings;
    }

    /// <summary>Gets the fields the binding sets.</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The set fields; <see cref="SamplingFields.None"/> when it sets none.</returns>
    public static SamplingFields Requested(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var requested = SamplingFields.None;

        requested |= binding.Seed is null ? SamplingFields.None : SamplingFields.Seed;
        requested |= binding.TopK is null ? SamplingFields.None : SamplingFields.TopK;
        requested |= binding.StopSequences is { Count: > 0 } ? SamplingFields.StopSequences : SamplingFields.None;
        requested |= binding.FrequencyPenalty is null ? SamplingFields.None : SamplingFields.FrequencyPenalty;
        requested |= binding.PresencePenalty is null ? SamplingFields.None : SamplingFields.PresencePenalty;
        requested |= string.IsNullOrWhiteSpace(binding.ToolMode) ? SamplingFields.None : SamplingFields.ToolMode;
        requested |= binding.AllowMultipleToolCalls is null ? SamplingFields.None : SamplingFields.AllowMultipleToolCalls;
        requested |= string.IsNullOrWhiteSpace(binding.ReasoningOutput) ? SamplingFields.None : SamplingFields.ReasoningOutput;

        return requested;
    }

    private static void Collect(
        IReadOnlyList<ModelProviderDescriptor> providers,
        string provider,
        SamplingFields requested,
        HashSet<string> seen,
        List<Finding> findings)
    {
        var descriptor = providers.FirstOrDefault(
            candidate => string.Equals(candidate.Name, provider, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            return;
        }

        var missing = requested & ~descriptor.SupportedSamplingFields;

        foreach (var field in Enum.GetValues<SamplingFields>())
        {
            if (field == SamplingFields.None || (missing & field) == SamplingFields.None || !seen.Add($"{provider}\n{field}"))
            {
                continue;
            }

            var supportedBy = providers
                .Where(candidate => (candidate.SupportedSamplingFields & field) != SamplingFields.None)
                .Select(static candidate => candidate.Name)
                .ToList();

            findings.Add(new Finding(field, descriptor.Name, supportedBy));
        }
    }

    private static string MemberOf(SamplingFields field) => field switch
    {
        SamplingFields.Seed => nameof(ModelBinding.Seed),
        SamplingFields.TopK => nameof(ModelBinding.TopK),
        SamplingFields.StopSequences => nameof(ModelBinding.StopSequences),
        SamplingFields.FrequencyPenalty => nameof(ModelBinding.FrequencyPenalty),
        SamplingFields.PresencePenalty => nameof(ModelBinding.PresencePenalty),
        SamplingFields.ToolMode => nameof(ModelBinding.ToolMode),
        SamplingFields.AllowMultipleToolCalls => nameof(ModelBinding.AllowMultipleToolCalls),
        SamplingFields.ReasoningOutput => nameof(ModelBinding.ReasoningOutput),
        _ => field.ToString(),
    };

    private static string PathOf(SamplingFields field)
    {
        var member = MemberOf(field);
        return $"model.{char.ToLowerInvariant(member[0])}{member[1..]}";
    }
}
