namespace Tracon;

/// <summary>
/// The provider names that the <c>UseOpenAI()</c> call registers, and the
/// <see cref="ModelBinding.ProviderSettings"/> keys.
/// </summary>
/// <remarks>
/// These names are <strong>stable</strong>. Agent definitions are stored in the database
/// with these names; changing them breaks the stored definitions.
/// </remarks>
public static class OpenAIProviderNames
{
    /// <summary>Gets the provider that uses the Chat Completions API: <c>openai</c>.</summary>
    public const string ChatCompletions = "openai";

    /// <summary>Gets the provider that uses the Responses API: <c>openai-responses</c>.</summary>
    public const string Responses = "openai-responses";

    /// <summary>The prefix for provider-specific settings: <c>openai</c>.</summary>
    /// <remarks>
    /// One prefix covers both surfaces (<see cref="ChatCompletions"/> and
    /// <see cref="Responses"/>) and every provider that
    /// <c>UseOpenAICompatible()</c> registers, whatever name it is given: they
    /// all build their clients through the same OpenAI client library.
    /// </remarks>
    public const string SettingsPrefix = "openai";

    /// <summary>
    /// The setting keys this provider supports. <strong>Empty today.</strong>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is deliberately empty. The settings that OpenAI and the
    /// compatible servers share (temperature, output limit, reasoning effort,
    /// response format) are fixed fields of <see cref="ModelBinding"/>; no
    /// vendor-only setting is offered yet.
    /// </para>
    /// <para>
    /// Because the list is empty, <see cref="ModelProviderSettings.Validate"/>
    /// rejects any key and says "supports no extra settings" in its message.
    /// A key is never ignored silently: a setting that does nothing would leave
    /// the author without the behavior they configured.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } = [];
}
