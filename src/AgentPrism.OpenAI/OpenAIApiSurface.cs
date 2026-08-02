namespace AgentPrism;

/// <summary>
/// OpenAI'in iki sohbet API'sinden hangisinin kullanilacagini belirler.
/// </summary>
/// <remarks>
/// Secim <see cref="ModelBinding.Provider"/> uzerinden yapilir:
/// <see cref="OpenAIProviderNames.ChatCompletions"/> veya
/// <see cref="OpenAIProviderNames.Responses"/>. <c>UseOpenAI()</c> her ikisini de kaydeder.
/// </remarks>
public enum OpenAIApiSurface
{
    /// <summary>
    /// Chat Completions API. Konusma gecmisi istemcide tutulur ve AgentPrism'in
    /// kalicilik katmanina yazilir. Varsayilan yol budur.
    /// </summary>
    ChatCompletions = 0,

    /// <summary>
    /// Responses API. Konusma durumunu OpenAI servisi yonetir.
    /// </summary>
    /// <remarks>
    /// OpenAI kitapligi bu yuzeyi hala "evaluation purposes only" olarak isaretler
    /// (<c>OPENAI001</c>). Kullanim tek bir dosyada toplanmistir.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-031.
    /// </remarks>
    Responses = 1,
}
