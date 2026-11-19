namespace Tracon;

/// <summary>The OpenAI provider options of Tracon.</summary>
/// <remarks>
/// Validation is written by hand inside <see cref="OpenAIProviderOptionsValidator"/>;
/// <c>DataAnnotations</c> is not used.
/// </remarks>
public sealed class OpenAIProviderOptions
{
    /// <summary>Gets the full path of the configuration section the options are read from.</summary>
    public const string SectionName = "Tracon:Providers:OpenAI";

    /// <summary>Gets or sets the OpenAI API key.</summary>
    /// <remarks>
    /// <strong>This value is a secret and is never written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable or a secret manager. The key is
    /// never written to the database, never returned from the API and never shown in the
    /// user interface. White space at both ends is removed before the key is sent. A key that still holds a
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
    /// Gets or sets the model name to use when <see cref="ModelBinding.Model"/> is left empty.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Gets or sets the request address. When <see langword="null"/>, the OpenAI address
    /// is used. Set it for OpenAI compatible proxies.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets the organization id. It is used by multi organization accounts.</summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Gets or sets the upper time limit of a single request. When <see
    /// langword="null"/>, the library default is used.
    /// </summary>
    /// <remarks>Must be from one millisecond to about 24.8 days (<see cref="int.MaxValue"/> milliseconds) when set; any other value fails at startup.</remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets the model catalog shown in the user interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tracon carries no built-in model list; the catalog comes entirely from here.
    /// Model names and prices change much faster than the package is released.
    /// </para>
    /// <para>
    /// This list is <em>not a validation list</em>. A model name that is absent here can
    /// still be used; the catalog only feeds the model picker screen and the cost
    /// calculation of the user interface.
    /// </para>
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the Responses API surface is also enabled
    /// for a provider registered with <c>UseOpenAICompatible()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Only <c>UseOpenAICompatible()</c> reads it.</strong> <c>UseOpenAI()</c>
    /// ignores this field; the official OpenAI provider always registers both surfaces
    /// (<see cref="OpenAIProviderNames.ChatCompletions"/> and
    /// <see cref="OpenAIProviderNames.Responses"/>).
    /// </para>
    /// <para>
    /// The default is <see langword="false"/>: most OpenAI compatible servers do not
    /// implement the <c>/v1/responses</c> endpoint. When it is on, a second provider is
    /// registered under the name <c>{name}-responses</c>.
    /// </para>
    /// </remarks>
    public bool EnableResponsesSurface { get; set; }
}
