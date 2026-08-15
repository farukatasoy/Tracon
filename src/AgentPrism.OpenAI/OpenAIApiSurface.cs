namespace AgentPrism;

/// <summary>
/// Selects which of the two OpenAI chat APIs is used.
/// </summary>
/// <remarks>
/// The choice comes from <see cref="ModelBinding.Provider"/>:
/// <see cref="OpenAIProviderNames.ChatCompletions"/> or
/// <see cref="OpenAIProviderNames.Responses"/>. <c>UseOpenAI()</c> registers both.
/// </remarks>
public enum OpenAIApiSurface
{
    /// <summary>
    /// The Chat Completions API. The conversation history stays on the client and is
    /// written to the AgentPrism persistence layer. This is the default path.
    /// </summary>
    ChatCompletions = 0,

    /// <summary>
    /// The Responses API. The OpenAI service owns the conversation state.
    /// </summary>
    /// <remarks>
    /// The OpenAI library still marks this surface as "evaluation purposes only"
    /// (<c>OPENAI001</c>). Its usage is confined to a single file.
    /// Reason: <c>docs/KARARLAR.md</c>, decision K-031.
    /// </remarks>
    Responses = 1,
}
