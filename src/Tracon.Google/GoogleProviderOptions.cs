namespace Tracon;

/// <summary>Tracon's Google Gemini provider settings.</summary>
/// <remarks>
/// <para>
/// This type is deliberately a <c>class</c>, not a <c>record</c>: a <c>record</c>'s
/// generated <c>ToString</c> would print every property, exposing the API key in a
/// single log line.
/// </para>
/// <para>
/// Validation is done by hand in <see cref="GoogleProviderOptionsValidator"/>;
/// <c>DataAnnotations</c> relies on reflection and breaks AOT compatibility.
/// </para>
/// </remarks>
public sealed class GoogleProviderOptions
{
    /// <summary>Full path of the configuration section settings are read from.</summary>
    public const string SectionName = "Tracon:Providers:Google";

    /// <summary>Gemini Developer API key.</summary>
    /// <remarks>
    /// <strong>This value is a secret and is not written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable, or a secret manager. The
    /// key is never written to the database, never returned by the API, and never
    /// shown in the UI, under any condition. White space at both ends is removed before the key is sent. A key that still holds a
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
    /// the next call; the host does not restart. Chat, image generation and speech all send it. The value is read from
    /// <c>IConfiguration</c>, never stored by Tracon, and never shown. A value that is missing, blank or unusable does
    /// not fail the call: the last valid key stays in use and one warning names this configuration key.
    /// <see cref="ApiKey"/> is the key the host starts with when the name has no value yet.
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
    /// Model name used when <see cref="ModelBinding.Model"/> is left empty.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Request address. When <see langword="null"/>, Google's own address
    /// (<c>https://generativelanguage.googleapis.com</c>) is used.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// API version to use. When <see langword="null"/>, the SDK default applies.
    /// Example: <c>v1beta</c>.
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>Upper time limit for a single request. When <see langword="null"/>, the library default is used.</summary>
    /// <remarks>Must be from one millisecond to about 24.8 days (<see cref="int.MaxValue"/> milliseconds) when set; any other value fails at startup.</remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Model catalog shown in the UI.
    /// </summary>
    /// <remarks>
    /// Tracon carries no built-in model list; the catalog comes entirely from
    /// here. This list <em>is not a validation list</em>: a model name absent from
    /// it can still be used.
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];
}
