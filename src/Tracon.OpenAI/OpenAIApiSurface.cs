namespace Tracon;

/// <summary>
/// Selects which of the two OpenAI chat APIs is used.
/// </summary>
/// <remarks>
/// The choice comes from <see cref="ModelBinding.Provider"/>:
/// <see cref="OpenAIProviderNames.ChatCompletions"/> or
/// <see cref="OpenAIProviderNames.Responses"/>. <c>UseOpenAI()</c> registers both.
/// </remarks>
internal enum OpenAIApiSurface
{
    /// <summary>
    /// The Chat Completions API. The conversation history stays on the client and is
    /// written to the Tracon persistence layer. This is the default path.
    /// </summary>
    ChatCompletions = 0,

    /// <summary>
    /// The Responses API. The OpenAI service owns the conversation state.
    /// </summary>
    /// <remarks>
    /// The OpenAI library still marks this surface as "evaluation purposes only"
    /// (<c>OPENAI001</c>). Its usage is confined to a single file.
    /// </remarks>
    Responses = 1,
}
