namespace AgentPrism;

/// <summary>AgentPrism'in calisma zamani ayarlari.</summary>
/// <remarks>Dogrulama <see cref="AgentPrismOptionsValidator"/> icinde elle yapilir.</remarks>
public sealed class AgentPrismOptions
{
    /// <summary>Yapilandirma bolumunun varsayilan adi.</summary>
    public const string SectionName = "AgentPrism";

    /// <summary>
    /// Tek kiracili kurulumda kullanilacak kiraci kimligi.
    /// Cok kiracililik Faz 6'da devreye girer.
    /// </summary>
    public string DefaultTenantId { get; set; } = "default";

    /// <summary>Calistirma kaydi ayarlari.</summary>
    public AgentPrismRunRecordingOptions RunRecording { get; set; } = new();
}

/// <summary>Calistirma kaydinin ne kadar ayrinti tutacagini belirler.</summary>
public sealed class AgentPrismRunRecordingOptions
{
    /// <summary>Calistirma kaydi acik mi. Kapatilirsa hicbir olay yazilmaz.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Akisli calistirmalarda her metin parcasi ayri bir olay olarak yazilsin mi.
    /// Kapatilirsa yalnizca tamamlanan mesajlar kaydedilir; olay hacmi ciddi olcude duser.
    /// </summary>
    public bool RecordMessageDeltas { get; set; } = true;

    /// <summary>Tool argumanlari ve sonuclari kaydedilsin mi.</summary>
    /// <remarks>
    /// Tool argumanlari kisisel veri tasiyabilir. Bu bayrak, veri saklama
    /// politikasi geregi kapatilabilir.
    /// </remarks>
    public bool RecordToolPayloads { get; set; } = true;

    /// <summary>
    /// Tek bir olay yukunun ust karakter siniri. Asan yukler kirpilir ve
    /// sonuna kirpildigini belirten bir isaret eklenir.
    /// </summary>
    public int MaxPayloadLength { get; set; } = 8 * 1024;
}
