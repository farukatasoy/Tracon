namespace AgentPrism;

/// <summary>AgentPrism's Anthropic (Claude) provider settings.</summary>
/// <remarks>
/// <para>
/// This type is deliberately a <c>class</c>, not a <c>record</c>: a <c>record</c>'s
/// generated <c>ToString</c> would print every property and a single log line
/// would expose the API key. Rationale: <c>docs/KARARLAR.md</c>, decision K-035.
/// </para>
/// <para>
/// Validation is done by hand in <see cref="AnthropicProviderOptionsValidator"/>;
/// <c>DataAnnotations</c> relies on reflection and breaks AOT compatibility
/// (decision K-006).
/// </para>
/// </remarks>
public sealed class AnthropicProviderOptions
{
    /// <summary>The full path of the configuration section settings are read from.</summary>
    public const string SectionName = "AgentPrism:Providers:Anthropic";

    /// <summary>Anthropic API key.</summary>
    /// <remarks>
    /// <strong>This value is a secret and is never written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable, or a secret manager.
    /// The key is never written to the database, never returned from the API, and
    /// never shown in the UI, under any condition.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model name used when <see cref="ModelBinding.Model"/> is left empty.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// The request address. When <see langword="null"/>, Anthropic's own address is
    /// used. Filled in for setups behind a proxy or gateway.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The upper bound used when <see cref="ModelBinding.MaxOutputTokens"/> is not given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 The Anthropic Messages API requires <c>max_tokens</c>; it is not an
    /// optional field like in OpenAI. Because of this, AgentPrism carries a
    /// default and the default cannot be <see langword="null"/>.
    /// </para>
    /// <para>
    /// The value bounds output generation, not tokens actually spent. Even so, a
    /// very high limit is treated by some providers as a "worst case" cost (see
    /// the <c>HTTP 402</c> encountered with OpenRouter in Phase 8).
    /// </para>
    /// </remarks>
    public int DefaultMaxOutputTokens { get; set; } = 4096;

    /// <summary>The upper time limit for a single request. When <see langword="null"/>, the library default is used.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// How many times the SDK automatically retries a request. When
    /// <see langword="null"/>, the library default is used.
    /// </summary>
    /// <remarks>
    /// The circuit breaker looks at failure of the <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// level, not the raw request count; this setting does not affect it. However,
    /// if a test measures raw request count, this value must be set to <c>0</c>.
    /// </remarks>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// The model catalog shown to the UI.
    /// </summary>
    /// <remarks>
    /// AgentPrism carries no built-in model list; the catalog comes entirely from
    /// here. This list is <em>not a validation list</em>: a model name absent from
    /// it can still be used. Rationale: <c>docs/KARARLAR.md</c>, decision K-032.
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];
}
