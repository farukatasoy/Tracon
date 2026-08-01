namespace AgentPrism.Api;

/// <summary>Ornek calistirma istegi.</summary>
/// <param name="Message">Agent'a gonderilecek mesaj.</param>
internal sealed record RunRequest(string Message);
