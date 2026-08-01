namespace AgentPrism;

/// <summary>
/// Bir agent'in hangi saglayici ve model ile calisacagini belirler.
/// Kimlik bilgisi <em>icermez</em>; API anahtari yapilandirmadan cozulur.
/// </summary>
public sealed record ModelBinding
{
    /// <summary>Saglayici adi. Ornek: <c>openai</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Model adi. Ornek: <c>gpt-5.4-mini</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Ornekleme sicakligi. <see langword="null"/> ise saglayici varsayilani kullanilir.</summary>
    public float? Temperature { get; init; }

    /// <summary>Ureteceklerin ust token siniri. <see langword="null"/> ise saglayici varsayilani kullanilir.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Nucleus ornekleme esigi. <see langword="null"/> ise saglayici varsayilani kullanilir.</summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Akil yurutme cabasi seviyesi. Destekleyen modellerde kullanilir; digerlerinde yok sayilir.
    /// </summary>
    public string? ReasoningEffort { get; init; }
}
