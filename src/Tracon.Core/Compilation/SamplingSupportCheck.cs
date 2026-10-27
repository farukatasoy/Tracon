using System.Globalization;

namespace Tracon;

/// <summary>
/// Finds what a binding sets that its providers or models would drop, refuse or change.
/// </summary>
/// <remarks>
/// <para>
/// The model adapters send only some of the <see cref="SamplingFields"/> and drop the rest without an error:
/// a definition that sets <see cref="ModelBinding.Seed"/> for the Responses API runs, and the seed never
/// reaches the model. A provider states what its surface sends through <see cref="ISamplingSupport"/>; a
/// provider that states nothing supports none of the fields.
/// </para>
/// <para>
/// A field the surface sends can still fail at the model or in its value, so the check has three findings:
/// <see cref="Unsupported"/> (the surface drops the field), <see cref="RejectedByModel"/> (the surface sends it
/// and the catalog says the model rejects it), and <see cref="OutOfRange"/> (the value is outside the range the
/// surface accepts, <see cref="ISamplingSupport.SamplingLimits"/>). Each field gets the most basic finding only:
/// a field that is dropped is not also reported as rejected, and a field that is rejected is not also reported
/// for its value, because removing it is the one remedy.
/// </para>
/// <para>
/// One rule for two readers, as <see cref="SamplingCapabilityCheck"/>: the compiler stops
/// the compilation and the validator reports the same finding with the field's path. The
/// binding's <see cref="ModelBinding.Fallbacks"/> links are checked too, because a link
/// receives the primary call's request options. A provider that is not registered is not
/// checked here; the model check reports it. A model absent from the catalog, or whose entry names
/// nothing, is not checked either: the catalog is metadata, not a validation list.
/// </para>
/// </remarks>
internal static class SamplingSupportCheck
{
    /// <summary>A request field that a provider or a model would drop, refuse or change.</summary>
    /// <param name="Field">The field.</param>
    /// <param name="Provider">The provider name.</param>
    internal abstract record Finding(SamplingFields Field, string Provider)
    {
        /// <summary>Gets the <see cref="ModelBinding"/> member name of the field.</summary>
        public string Member => MemberOf(Field);

        /// <summary>Gets the JSON path of the field in the definition.</summary>
        public string Path => PathOf(Field);

        /// <summary>Gets the machine-readable code of the validation finding (<see cref="ValidationCodes"/>).</summary>
        public abstract string Code { get; }

        /// <summary>Gets the sentence that names the field and the cause, and says what to do.</summary>
        public abstract string Describe();
    }

    /// <summary>A field the binding sets and the provider's API surface would drop.</summary>
    /// <param name="Field">The field.</param>
    /// <param name="Provider">The provider name.</param>
    /// <param name="SupportedBy">The registered providers that send the field.</param>
    internal sealed record Unsupported(SamplingFields Field, string Provider, IReadOnlyList<string> SupportedBy) : Finding(Field, Provider)
    {
        /// <inheritdoc />
        public override string Code => ValidationCodes.SamplingFieldUnsupported;

        /// <inheritdoc />
        public override string Describe()
        {
            var alternative = SupportedBy.Count == 0
                ? "No registered provider sends it."
                : $"Providers that send it: {string.Join(", ", SupportedBy)}.";

            return $"{nameof(ModelBinding)}.{Member} is set, but the '{Provider}' provider does not send it to the model " +
                   $"(its API surface drops it, so the request would run without it). Remove it from the definition " +
                   $"or use a provider that sends it. {alternative}";
        }
    }

    /// <summary>A field the binding sets, the provider's surface sends, and the catalog says the model rejects.</summary>
    /// <param name="Field">The field.</param>
    /// <param name="Provider">The provider name.</param>
    /// <param name="Model">The model name, as the binding writes it.</param>
    internal sealed record RejectedByModel(SamplingFields Field, string Provider, string Model) : Finding(Field, Provider)
    {
        /// <inheritdoc />
        public override string Code => ValidationCodes.SamplingFieldRejectedByModel;

        /// <inheritdoc />
        public override string Describe()
            => $"{nameof(ModelBinding)}.{Member} is set, but the model catalog states that model '{Model}' of the '{Provider}' " +
               $"provider rejects it (the provider would answer the request with 400). Remove it from the definition " +
               $"or use another model.";
    }

    /// <summary>A value the binding sets outside the range the provider's surface accepts.</summary>
    /// <param name="Field">The field.</param>
    /// <param name="Provider">The provider name.</param>
    /// <param name="Value">What the binding sets, such as <c>is 3000000000</c> or <c>has 5 entries</c>.</param>
    /// <param name="Accepted">What the provider accepts, such as <c>only values from 0 to 9</c> or <c>at most 4</c>.</param>
    internal sealed record OutOfRange(SamplingFields Field, string Provider, string Value, string Accepted) : Finding(Field, Provider)
    {
        /// <inheritdoc />
        public override string Code => ValidationCodes.SamplingValueOutOfRange;

        /// <inheritdoc />
        public override string Describe()
            => $"{nameof(ModelBinding)}.{Member} {Value}, but the '{Provider}' provider accepts {Accepted}. " +
               $"Use a value the provider accepts.";
    }

    /// <summary>
    /// Lists what the binding sets that the primary provider or model, or a fallback link, would drop,
    /// refuse or change.
    /// </summary>
    /// <param name="registry">The registry whose providers and catalog are read.</param>
    /// <param name="binding">The binding.</param>
    /// <returns>An empty list when the binding sets no such field or every provider and model takes it.</returns>
    public static IReadOnlyList<Finding> Find(IModelProviderRegistry registry, ModelBinding binding)
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

        Collect(providers, binding, binding.Provider, binding.Model, requested, seen, findings);

        foreach (var link in binding.Fallbacks)
        {
            Collect(providers, binding, link.Provider, link.Model, requested, seen, findings);
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
        ModelBinding binding,
        string provider,
        string model,
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

            findings.Add(new Unsupported(field, descriptor.Name, supportedBy));
        }

        var sent = requested & descriptor.SupportedSamplingFields;
        var rejected = sent & (ModelCatalogLookup.Find(providers, provider, model)?.RejectedSamplingFields ?? SamplingFields.None);

        foreach (var field in Enum.GetValues<SamplingFields>())
        {
            if (field != SamplingFields.None && (rejected & field) != SamplingFields.None && seen.Add($"rejected\n{provider}\n{model}\n{field}"))
            {
                findings.Add(new RejectedByModel(field, descriptor.Name, model));
            }
        }

        CollectOutOfRange(descriptor, binding, sent & ~rejected, seen, findings);
    }

    private static void CollectOutOfRange(
        ModelProviderDescriptor descriptor,
        ModelBinding binding,
        SamplingFields checkable,
        HashSet<string> seen,
        List<Finding> findings)
    {
        var limits = descriptor.SamplingLimits;

        if ((checkable & SamplingFields.Seed) != SamplingFields.None
            && binding.Seed is { } seed
            && (seed < limits.MinSeed || seed > limits.MaxSeed)
            && seen.Add($"range\n{descriptor.Name}\n{SamplingFields.Seed}"))
        {
            findings.Add(new OutOfRange(
                SamplingFields.Seed,
                descriptor.Name,
                $"is {seed.ToString(CultureInfo.InvariantCulture)}",
                DescribeSeedRange(limits)));
        }

        if ((checkable & SamplingFields.StopSequences) != SamplingFields.None
            && binding.StopSequences is { } stops
            && limits.MaxStopSequences is { } maximum
            && stops.Count > maximum
            && seen.Add($"range\n{descriptor.Name}\n{SamplingFields.StopSequences}"))
        {
            findings.Add(new OutOfRange(
                SamplingFields.StopSequences,
                descriptor.Name,
                $"has {stops.Count.ToString(CultureInfo.InvariantCulture)} {(stops.Count == 1 ? "entry" : "entries")}",
                $"at most {maximum.ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    private static string DescribeSeedRange(SamplingLimits limits) => (limits.MinSeed, limits.MaxSeed) switch
    {
        ({ } minimum, { } maximum) => $"only values from {minimum.ToString(CultureInfo.InvariantCulture)} to {maximum.ToString(CultureInfo.InvariantCulture)}",
        ({ } minimum, null) => $"only values from {minimum.ToString(CultureInfo.InvariantCulture)}",
        (null, { } maximum) => $"only values up to {maximum.ToString(CultureInfo.InvariantCulture)}",
        _ => "no value",
    };

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
