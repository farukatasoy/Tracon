using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Yeniden oynatmada tool'larin nasil ele alinacagi.</summary>
/// <remarks>
/// 🚨 JSON'da <strong>ad olarak</strong> yazilir ve okunur. Donusturucu
/// olmadan minimal API govdeyi cozemez ve istek <em>bos govdeli</em> bir
/// <c>400</c> ile duser — hata mesaji sebebi soylemez. Ayni isaret
/// <see cref="RunScoreKind"/> uzerinde de vardir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ReplayToolMode>))]
public enum ReplayToolMode
{
    /// <summary>
    /// Tool'lar hic baglanmaz; yalnizca model yaniti uretilir. Skill'ler ve
    /// cagrilabilir alt agent'lar da devre disidir — ucu de modele bir tool
    /// olarak gorunur.
    /// </summary>
    NoTools = 0,

    /// <summary>
    /// Kayitli tool sonuclari geri oynatilir; hicbir tool govdesi calismaz.
    /// Eslesmeyen bir cagri yeniden oynatmayi <strong>durdurur</strong>.
    /// </summary>
    ReplayTools = 1,

    /// <summary>
    /// 🚨 Tool'lar gercekten kosar ve yan etki uretir. Onay gerektiren bir tool
    /// varsa istek reddedilir; uc ayrica <c>Admin</c> rolu ister.
    /// </summary>
    LiveTools = 2,
}

/// <summary>Bir yeniden oynatma istegi.</summary>
/// <remarks>
/// Yeniden oynatma <strong>girdiyi korur, kosullari degistirir</strong>. Girdi
/// mesajlari ve kiraci degistirilemez: farkli bir girdi yeni bir calistirmadir,
/// kiraci sinirini gecmek ise bir guvenlik ihlalidir.
/// </remarks>
public sealed record RunReplayRequest
{
    /// <summary>
    /// Kullanilacak agent tanim surumu. Verilmezse bugunku etkin surum.
    /// </summary>
    /// <remarks>
    /// Kod kaynakli agent'larda surum gecmisi yoktur; deger verilirse istek
    /// reddedilir.
    /// </remarks>
    public int? AgentVersion { get; init; }

    /// <summary>
    /// Bindirilecek model adi. Verilmezse tanimin kendi modeli kullanilir.
    /// </summary>
    /// <remarks>
    /// Yalnizca model <em>adi</em> bindirilir; saglayici ve kimlik bilgileri
    /// tanimdan gelir. Saglayiciyi degistirmek yeni bir tanimdir.
    /// </remarks>
    public string? ModelId { get; init; }

    /// <summary>Tool davranisi. Varsayilan <see cref="ReplayToolMode.ReplayTools"/>.</summary>
    public ReplayToolMode ToolMode { get; init; } = ReplayToolMode.ReplayTools;
}

/// <summary>
/// Yeniden oynatmanin, kayitli bir tool sonucunu bulamamasi.
/// </summary>
/// <remarks>
/// 🚨 Sessizce atlamak veya canli calistirmak <strong>reddedildi</strong>:
/// birincisi modelin goremedigi bir bosluk uretir ve sonucu sessizce yanlis
/// yapar, ikincisi kullanicinin istemedigi bir yan etki uretir. Uc bu istisnayi
/// <c>422</c>'ye cevirir ve hangi tool'un hangi argumanla eslesmedigini yazar.
/// </remarks>
public sealed class ReplayToolMismatchException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string ReplayToolMismatchErrorType = "replay_tool_mismatch";

    /// <summary>Yeni bir eslesmeme hatasi olusturur.</summary>
    public ReplayToolMismatchException()
    {
    }

    /// <summary>Yeni bir eslesmeme hatasi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public ReplayToolMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir eslesmeme hatasi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public ReplayToolMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Eslesmeyen tool'un adi.</summary>
    public string? ToolName { get; init; }

    /// <summary>Eslesmeyen cagrinin argumanlari.</summary>
    public string? Arguments { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ReplayToolMismatchErrorType;

    /// <summary>Eslesmeyen bir cagri icin standart mesajli hata uretir.</summary>
    /// <param name="toolName">Model tarafindan cagrilan tool.</param>
    /// <param name="arguments">Cagrinin argumanlari.</param>
    /// <returns>Hata.</returns>
    public static ReplayToolMismatchException For(string toolName, string? arguments)
        => new(
            $"Yeniden oynatma durdu: '{toolName}' tool'u '{arguments ?? "(argumansiz)"}' argumaniyla " +
            "cagrildi ama kaynak calistirmada bu cagrinin kayitli bir sonucu yok. " +
            "Yeni surum farkli bir tool cagiriyor demektir; bu beklenen bir sonuctur ve " +
            "davranisin gercekten degistigini gosterir. Tool'u gercekten calistirmak icin " +
            "'toolMode' degerini 'LiveTools' yapin.")
        {
            ToolName = toolName,
            Arguments = arguments,
        };
}
