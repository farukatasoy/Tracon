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
    /// shown in the UI, under any condition.
    /// </remarks>
    public string? ApiKey { get; set; }

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
