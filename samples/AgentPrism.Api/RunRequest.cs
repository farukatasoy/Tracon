namespace AgentPrism.Api;

/// <summary>Ornek calistirma istegi.</summary>
/// <param name="Message">Agent'a gonderilecek mesaj.</param>
/// <param name="SessionId">
/// Devam edilecek oturumun kimligi. Verilirse konusma gecmisi veritabanindan
/// yuklenir ve calistirma sonunda geri yazilir. Bos birakilirsa oturumsuz calisir.
/// </param>
internal sealed record RunRequest(string Message, string? SessionId = null);
