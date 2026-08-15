namespace AgentPrism;

/// <summary>The OpenAI provider options of AgentPrism.</summary>
/// <remarks>
/// Validation is written by hand inside <see cref="OpenAIProviderOptionsValidator"/>;
/// <c>DataAnnotations</c> is not used. Reason: <c>docs/KARARLAR.md</c>, decision K-006.
/// </remarks>
public sealed class OpenAIProviderOptions
{
    /// <summary>Gets the full path of the configuration section the options are read from.</summary>
    public const string SectionName = "AgentPrism:Providers:OpenAI";

    /// <summary>Gets or sets the OpenAI API key.</summary>
    /// <remarks>
    /// <strong>This value is a secret and is never written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable or a secret manager. The key is
    /// never written to the database, never returned from the API and never shown in the
    /// user interface.
    /// </remarks>
    public string? ApiKey { get; set; }

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

    /// <summary>Gets or sets the upper time limit of a single request. When <see langword="null"/>, the library default is used.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets the model catalog shown in the user interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AgentPrism carries no built-in model list; the catalog comes entirely from here.
    /// Model names and prices change much faster than the package is released.
    /// Reason: <c>docs/KARARLAR.md</c>, decision K-032.
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
