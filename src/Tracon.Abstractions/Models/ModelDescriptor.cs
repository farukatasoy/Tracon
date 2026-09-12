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
}
