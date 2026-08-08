using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir calistirma icindeki <c>ToolInvoking</c> / <c>ToolInvoked</c> olay
/// ciftlerini eslestirir ve <see cref="ToolInvocationRecord"/> uretir.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework tool cagrisi icin ayri bir kanca sunmaz; cagri ve
/// sonuc <see cref="FunctionCallContent"/> ve <see cref="FunctionResultContent"/>
/// icerikleri olarak gelir. Ikisini birlestiren tek anahtar
/// <c>CallId</c> degeridir (ikisinin de tabani <see cref="ToolCallContent"/>).
/// </para>
/// <para>
/// <strong>Sure yalnizca akisli calistirmada olculur.</strong> Akissiz
/// calistirmada butun mesajlar tek seferde, cagri bittikten sonra gorulur;
/// iki icerik arasindaki gercek sure oradan okunamaz. Sifira yakin bir sure
/// yazmak yanlis veri uretirdi, bu yuzden alan bos birakilir.
/// </para>
/// <para>
/// Bu sinif <strong>is parcacigi guvenli degildir</strong>; her calistirmanin
/// kendi ornegi vardir ve tek bir okuma dongusunden kullanilir.
/// </para>
/// </remarks>
internal sealed class ToolInvocationTracker
{
    private readonly Dictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private readonly Guid _runId;
    private readonly bool _measureDuration;
    private readonly TimeProvider _timeProvider;
    private readonly ToolUsageAccumulator? _usage;
    private readonly string? _tenantId;

    /// <summary>Yeni bir izleyici olusturur.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="measureDuration">Sure olculsun mu. Yalnizca akisli calistirmada anlamlidir.</param>
    /// <param name="timeProvider">Zaman kaynagi.</param>
    /// <param name="usage">
    /// Tool'larin bildirdigi token disi olcumler. <see langword="null"/> ise
    /// olcum toplanmaz.
    /// </param>
    /// <param name="tenantId">
    /// Calistirmanin BEKLENEN kiracisi. Uretilen her kayda damgalanir;
    /// <see langword="null"/> ise depo kiraci denetimi yapmaz. Gerekce: K-355.
    /// </param>
    public ToolInvocationTracker(
        Guid runId,
        bool measureDuration,
        TimeProvider timeProvider,
        ToolUsageAccumulator? usage = null,
        string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _runId = runId;
        _measureDuration = measureDuration;
        _timeProvider = timeProvider;
        _usage = usage;
        _tenantId = tenantId;
    }

    /// <summary>Bir tool cagrisinin basladigini kaydeder.</summary>
    /// <param name="call">Cagri icerigi.</param>
    /// <param name="source">Tool'un kaynagi; kodda tanimliysa <see langword="null"/>.</param>
    /// <param name="arguments">Bicimlendirilmis argumanlar.</param>
    public void OnCall(FunctionCallContent call, string? source, string? arguments)
    {
        ArgumentNullException.ThrowIfNull(call);

        // Ayni CallId ikinci kez gelirse (yeniden deneme) ilk kaydin uzerine
        // yazilir: sonuc her zaman son cagriyla eslesir.
        _pending[call.CallId] = new PendingCall(
            call.Name,
            source,
            arguments,
            _timeProvider.GetTimestamp());
    }

    /// <summary>
    /// Bir tool cagrisinin sonuclandigini kaydeder ve kalici kaydi uretir.
    /// </summary>
    /// <param name="result">Sonuc icerigi.</param>
    /// <returns>
    /// Kalici kayit. Eslesen bir cagri bulunamazsa yine bir kayit uretilir;
    /// tool adi bilinmiyorsa <c>unknown</c> yazilir.
    /// </returns>
    public ToolInvocationRecord OnResult(FunctionResultContent result)
    {
        ArgumentNullException.ThrowIfNull(result);

        TimeSpan? duration = null;
        string toolName = "unknown";
        string? source = null;
        string? arguments = null;

        if (_pending.Remove(result.CallId, out var call))
        {
            toolName = call.ToolName;
            source = call.Source;
            arguments = call.Arguments;

            if (_measureDuration)
            {
                duration = _timeProvider.GetElapsedTime(call.StartedAt);
            }
        }

        return new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
            RunId = _runId,
            ToolName = toolName,
            ToolCallId = result.CallId,
            Source = source,
            Arguments = arguments,
            Result = result.Exception is null ? result.Result?.ToString() : null,
            Duration = duration,
            Error = result.Exception?.Message,
            CreatedAt = _timeProvider.GetUtcNow(),

            // Tool kendi olcumunu cagri kimligiyle bildirmis olabilir. Cagrilarin
            // buyuk cogunlugu olcum tasimaz ve alan bos kalir.
            Usage = _usage?.Take(result.CallId),

            // Beklenen kiraci damgasi (K-355).
            TenantId = _tenantId,
        };
    }

    /// <summary>
    /// Sonuclanmamis cagrilar icin kayit uretir ve izleyiciyi bosaltir.
    /// </summary>
    /// <remarks>
    /// Calistirma tool sonucu gelmeden biterse (iptal, hata, onay bekleme)
    /// cagri kaydi hic yazilmazdi. Bu, arayuzde "cagri basladi ama ne oldugu
    /// belli degil" durumu uretir; onun yerine acik bir hata mesajiyla kapatilir.
    /// </remarks>
    /// <param name="reason">Kaydin neden yarim kaldigi.</param>
    /// <returns>Yarim kalan cagrilarin kayitlari.</returns>
    public IReadOnlyList<ToolInvocationRecord> DrainUnfinished(string reason)
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        var now = _timeProvider.GetUtcNow();

        var records = _pending
            .Select(pair => new ToolInvocationRecord
            {
                Id = AgentPrismId.NewId(),
                RunId = _runId,
                ToolName = pair.Value.ToolName,
                ToolCallId = pair.Key,
                Source = pair.Value.Source,
                Arguments = pair.Value.Arguments,
                Duration = _measureDuration ? _timeProvider.GetElapsedTime(pair.Value.StartedAt) : null,
                Error = reason,
                CreatedAt = now,

                // Beklenen kiraci damgasi (K-355).
                TenantId = _tenantId,
            })
            .ToList();

        _pending.Clear();

        return records;
    }

    private readonly record struct PendingCall(
        string ToolName,
        string? Source,
        string? Arguments,
        long StartedAt);
}
