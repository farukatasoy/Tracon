namespace Tracon;

/// <summary>Tracon's Anthropic (Claude) provider settings.</summary>
/// <remarks>
/// <para>
/// This type is deliberately a <c>class</c>, not a <c>record</c>: a <c>record</c>'s
/// generated <c>ToString</c> would print every property and a single log line
/// would expose the API key.
/// </para>
/// <para>
/// Validation is done by hand in <see cref="AnthropicProviderOptionsValidator"/>;
/// <c>DataAnnotations</c> relies on reflection and breaks AOT compatibility.
/// </para>
/// </remarks>
public sealed class AnthropicProviderOptions
{
    /// <summary>The full path of the configuration section settings are read from.</summary>
    public const string SectionName = "Tracon:Providers:Anthropic";

    /// <summary>Anthropic API key.</summary>
    /// <remarks>
    /// <strong>This value is a secret and is never written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable, or a secret manager.
    /// The key is never written to the database, never returned from the API, and
    /// never shown in the UI, under any condition. White space at both ends is removed before the key is sent. A key that still holds a
    /// control character, or another character an HTTP header cannot carry, stops the host at start.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the name of the configuration key the API key is read from on every call, or <see langword="null"/>
    /// to use <see cref="ApiKey"/> for the life of the host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a name, a key that changes in the configuration source (a file or a secret store that reloads) is used by
    /// the next call; the host does not restart. The value is read from <c>IConfiguration</c>, never stored by Tracon,
    /// and never shown. A value that is missing, blank or unusable does not fail the call: the last valid key stays in
    /// use and one warning names this configuration key. <see cref="ApiKey"/> is the key the host starts with when the
    /// name has no value yet.
    /// </para>
    /// <para>
    /// An environment variable does not reload, so a rotated key in one is not seen. The name is yours: it is not
    /// limited to the prefix a tenant provider binding must stay under.
    /// </para>
    /// </remarks>
    public string? ApiKeyConfigurationName { get; set; }

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
    /// Gets or sets the name of the configuration key the request address is read from on every call, or
    /// <see langword="null"/> to use <see cref="Endpoint"/> for the life of the host.
    /// </summary>
    /// <remarks>
    /// Read together with <see cref="ApiKeyConfigurationName"/> in one step, so a call never sends the old key to the new
    /// address. A value that is not an absolute address does not fail the call; the last valid address stays in use. An
    /// address you set up this way is your own decision and is not checked by the outbound address guard, the same as
    /// <see cref="Endpoint"/>.
    /// </remarks>
    public string? EndpointConfigurationName { get; set; }

    /// <summary>
    /// The upper bound used when <see cref="ModelBinding.MaxOutputTokens"/> is not given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Anthropic Messages API requires <c>max_tokens</c>; it is not an
    /// optional field like in OpenAI. Because of this, Tracon carries a
    /// default and the default cannot be <see langword="null"/>.
    /// </para>
    /// <para>
    /// The value bounds output generation, not tokens actually spent. Even so, a
    /// very high limit is treated by some providers as a "worst case" cost (see
    /// the <c>HTTP 402</c> encountered with OpenRouter).
    /// </para>
    /// </remarks>
    public int DefaultMaxOutputTokens { get; set; } = 4096;

    /// <summary>The upper time limit for a single request. When <see langword="null"/>, the library default is used.</summary>
    /// <remarks>Must be from one millisecond to about 24.8 days (<see cref="int.MaxValue"/> milliseconds) when set; any other value fails at startup.</remarks>
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
    /// Tracon carries no built-in model list; the catalog comes entirely from
    /// here. This list is <em>not a validation list</em>: a model name absent from
    /// it can still be used.
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];
}
