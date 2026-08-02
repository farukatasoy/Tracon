namespace AgentPrism;

/// <summary>
/// <c>UseOpenAICompatible()</c> ile kaydedilen saglayicilarin yapilandirma yolu icin
/// sabitler.
/// </summary>
/// <remarks>
/// Ayar sekli <see cref="OpenAIProviderOptions"/> ile aynidir; bu tip yalnizca alt
/// bolum yolunu tasir. Her adlandirilmis saglayici kendi alt bolumunde yasar:
/// <c>AgentPrism:Providers:OpenAICompatible:{ad}:*</c>. Gerekce:
/// <c>docs/KARARLAR.md</c>, karar K-028 (saglayici ayarlari kendi alt bolumunde).
/// </remarks>
public static class OpenAICompatibleProviderOptions
{
    /// <summary>Adlandirilmis saglayicilarin yapilandirma bolumunun taban yolu.</summary>
    public const string SectionName = "AgentPrism:Providers:OpenAICompatible";
}
