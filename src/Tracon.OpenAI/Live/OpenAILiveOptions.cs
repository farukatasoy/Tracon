namespace Tracon;

/// <summary>Options of the OpenAI live voice provider.</summary>
/// <remarks>
/// The type is <strong>not</strong> a <c>record</c>: option classes must not
/// produce a <c>ToString</c> that can be written to a log, and this one sits next to
/// an API key.
/// </remarks>
public sealed class OpenAILiveOptions
{
    /// <summary>The default name of the configuration section.</summary>
    public const string SectionName = "Tracon:Providers:OpenAI:Live";

    /// <summary>
    /// The per-append token limit the provider enforces, as measured against the
    /// live API.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-11: an oversized append answers
    /// <c>"Context append text must not exceed 500 tokens."</c>
    /// </remarks>
    public const int ProviderAppendTokenLimit = 500;

    /// <summary>
    /// The characters-per-token ratio used to turn the provider's token limit into a
    /// character ceiling.
    /// </summary>
    /// <remarks>
    /// This is the <strong>single</strong> place the conversion is stated.
    /// Tracon ships no tokenizer and must not take one on (AOT, and no new
    /// package), so the limit is converted once and deliberately conservatively: a
    /// Turkish sentence spends far fewer characters per token than an English one, and
    /// a ratio tuned for English would have appends rejected in Turkish.
    /// </remarks>
    public const int ConservativeCharactersPerToken = 2;

    /// <summary>Gets or sets the live model. The default is <c>gpt-live-1</c>.</summary>
    public string Model { get; set; } = "gpt-live-1";

    /// <summary>Gets or sets the output voice; <see langword="null"/> leaves the provider's default.</summary>
    public string? Voice { get; set; }

    /// <summary>Gets or sets the API root. The default is <c>https://api.openai.com/v1/</c>.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the model the provider uses in <c>Responses</c> delegation mode.
    /// </summary>
    public string? BackendModel { get; set; }

    /// <summary>
    /// Gets or sets the per-append character ceiling. The default is derived from
    /// <see cref="ProviderAppendTokenLimit"/> and <see cref="ConservativeCharactersPerToken"/>.
    /// </summary>
    public int MaxAppendCharacters { get; set; } = ProviderAppendTokenLimit * ConservativeCharactersPerToken;

    /// <summary>Gets or sets the timeout of the session-creation call. The default is 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
