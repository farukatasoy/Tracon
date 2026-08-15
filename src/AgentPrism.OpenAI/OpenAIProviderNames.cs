namespace AgentPrism;

/// <summary>
/// The provider names that the <c>UseOpenAI()</c> call registers.
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
}
