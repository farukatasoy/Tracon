namespace Tracon;

/// <summary>A model's capabilities and limits.</summary>
public sealed record ModelDescriptor
{
    /// <summary>The model name. <c>ModelBinding.Model</c> matches this value.</summary>
    public required string Name { get; init; }

    /// <summary>The name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The context window's token capacity.</summary>
    public int? ContextWindowTokens { get; init; }

    /// <summary>The maximum number of tokens producible in a single response.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Whether streaming responses are supported.</summary>
    public bool SupportsStreaming { get; init; } = true;

    /// <summary>Whether tool calling is supported.</summary>
    public bool SupportsTools { get; init; } = true;

    /// <summary>Whether the reasoning-effort setting is supported.</summary>
    public bool SupportsReasoning { get; init; }

    /// <summary>Whether output conforming to a JSON schema can be produced.</summary>
    public bool SupportsStructuredOutput { get; init; }

    /// <summary>
    /// Whether the model accepts the sampling parameters <c>temperature</c> and
    /// <c>topP</c>; <see langword="null"/> when the catalog does not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameters are <see cref="ModelBinding.Temperature"/> and
    /// <see cref="ModelBinding.TopP"/>. <see langword="null"/> (the default)
    /// means unknown and checks nothing. When
    /// the catalog states <see langword="false"/>, an agent definition that sets
    /// either parameter does not compile, and the validation report names the
    /// field: the provider would reject the request with <c>400</c> on the first
    /// run. The check covers the binding's <see cref="ModelBinding.Fallbacks"/>
    /// links too, because they receive the same parameters.
    /// </para>
    /// <para>
    /// The value comes from the catalog, never from a model-name list in code:
    /// many current models reject sampling, and which ones changes with every
    /// release.
    /// </para>
    /// </remarks>
    public bool? SupportsSampling { get; init; }

    /// <summary>
    /// The request fields this model rejects although its provider's API surface sends them;
    /// <see cref="SamplingFields.None"/> when the catalog names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A model decides what it accepts, not the API surface: <c>openai</c> sends
    /// <see cref="ModelBinding.StopSequences"/>, and some reasoning models answer a request that
    /// carries them with <c>400 unsupported_parameter</c>. <see cref="SamplingFields.None"/> (the
    /// default) means the catalog does not say and checks nothing. When the catalog names a field, an
    /// agent definition that sets it does not compile, the validation report names the field as
    /// <c>sampling_field_rejected_by_model</c>, and saving the definition returns <c>400</c>: the
    /// provider would reject the first run. The check covers the binding's
    /// <see cref="ModelBinding.Fallbacks"/> links too, because they receive the same fields.
    /// </para>
    /// <para>
    /// Only a field the provider's surface sends can be rejected here; a field the surface drops is
    /// reported as <c>sampling_field_unsupported</c> already. In configuration the value is a name or a
    /// comma-separated list of names, such as <c>"StopSequences, Seed"</c>, and a name that does not
    /// exist stops the host at start. The value comes from the catalog, never from a model-name list
    /// in code: which models reject a field changes with every release.
    /// </para>
    /// </remarks>
    public SamplingFields RejectedSamplingFields { get; init; }

    /// <summary>The cost per million input tokens. For reporting only.</summary>
    public decimal? InputCostPerMillionTokens { get; init; }

    /// <summary>The cost per million output tokens. For reporting only.</summary>
    public decimal? OutputCostPerMillionTokens { get; init; }

    /// <summary>
    /// The cost per million input tokens that were served from the provider's
    /// prompt cache. For reporting only.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="null"/> the cached tokens are priced at
    /// <see cref="InputCostPerMillionTokens"/>, exactly as they were before the
    /// rate existed — an undefined cache rate does NOT make the run's price
    /// unknown. Set it to <c>0</c> only to state that cache reads are free.
    /// </remarks>
    public decimal? CachedInputCostPerMillionTokens { get; init; }
}

/// <summary>A provider's definition as shown in the UI.</summary>
public sealed record ModelProviderDescriptor
{
    /// <summary>The provider name.</summary>
    public required string Name { get; init; }

    /// <summary>The name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The models this provider offers.</summary>
    public IReadOnlyList<ModelDescriptor> Models { get; init; } = [];

    /// <summary>
    /// The provider's last known health status.
    /// </summary>
    /// <remarks>
    /// This field is filled from a <strong>cache</strong>; the <c>/api/models</c>
    /// endpoint makes no network call to the provider for this field (the
    /// default status is <see cref="ModelProviderHealthStatus.Unknown"/>). Use
    /// <c>/api/models/health</c> for a live check.
    /// </remarks>
    public ModelProviderHealthStatus Status { get; init; }

    /// <summary>
    /// Gets the fields the provider's API surface sends to the model, as the provider stated
    /// them through <see cref="ISamplingSupport"/>.
    /// </summary>
    /// <remarks>
    /// Not part of the wire shape. The compiler and the validator read it to reject a
    /// definition that sets a field the provider would drop.
    /// </remarks>
    internal SamplingFields SupportedSamplingFields { get; init; }

    /// <summary>
    /// Gets the value ranges the provider's API surface accepts, as the provider stated them through
    /// <see cref="ISamplingSupport"/>.
    /// </summary>
    /// <remarks>
    /// Not part of the wire shape. The compiler and the validator read it to reject a definition whose
    /// value the provider would refuse or change.
    /// </remarks>
    internal SamplingLimits SamplingLimits { get; init; } = SamplingLimits.None;

    /// <summary>
    /// Gets the hosted tool kinds the provider's API surface runs, as the provider stated them
    /// through <see cref="IHostedToolSupport"/>.
    /// </summary>
    /// <remarks>
    /// Not part of the wire shape. The compiler and the validator read it to reject a
    /// definition that selects a hosted tool the provider would drop.
    /// </remarks>
    internal HostedToolKinds SupportedHostedTools { get; init; }

    /// <summary>
    /// Gets the provider's answer to <see cref="IResponseSchemaSupport"/>, or <see langword="null"/> when
    /// the provider does not implement it.
    /// </summary>
    /// <remarks>
    /// Not part of the wire shape. The validator reads it to report the response schema the provider's
    /// client sends. The provider is a singleton, so the reference stays valid for the registry's life.
    /// </remarks>
    internal IResponseSchemaSupport? ResponseSchemaSupport { get; init; }
}
