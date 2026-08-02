namespace AgentPrism;

/// <summary>
/// <c>UseOpenAI()</c> cagrisinin kaydettigi saglayici adlari.
/// </summary>
/// <remarks>
/// Bu adlar <strong>kararlidir</strong>. Agent tanimlari veritabaninda bu adlarla
/// saklanir; degistirmek kayitli tanimlari bozar.
/// </remarks>
public static class OpenAIProviderNames
{
    /// <summary>Chat Completions API kullanan saglayici: <c>openai</c>.</summary>
    public const string ChatCompletions = "openai";

    /// <summary>Responses API kullanan saglayici: <c>openai-responses</c>.</summary>
    public const string Responses = "openai-responses";
}
