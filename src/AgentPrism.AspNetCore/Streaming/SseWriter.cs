using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="HttpResponse"/> uzerine Server-Sent Events cerceveleri yazar.
/// </summary>
/// <remarks>
/// <para>
/// Yanit arabellegi kapatilir ve her cerceveden sonra akis bosaltilir; aksi halde
/// olaylar istemciye ancak yanit kapandiginda ulasirdi.
/// </para>
/// <para>
/// <c>X-Accel-Buffering: no</c> basligi nginx gibi ters vekillerin akisi
/// arabelleklemesini engeller.
/// </para>
/// </remarks>
internal sealed class SseWriter
{
    private readonly HttpResponse _response;

    private SseWriter(HttpResponse response) => _response = response;

    /// <summary>Yanit basliklarini kurar ve yazmaya hazir bir yazici dondurur.</summary>
    /// <param name="response">Yazilacak yanit.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kullanima hazir yazici.</returns>
    public static async Task<SseWriter> StartAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache,no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers.ContentEncoding = "identity";

        // Ters vekil arabelleklemesini kapatir (nginx ve turevleri).
        response.Headers["X-Accel-Buffering"] = "no";

        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        return new SseWriter(response);
    }

    /// <summary>Bir olay cercevesi yazar ve akisi bosaltir.</summary>
    /// <param name="id">
    /// Olay kimligi. Istemci baglanti koptugunda bunu <c>Last-Event-ID</c> basliginda
    /// geri gonderir. Bos birakilirsa <c>id</c> alani yazilmaz.
    /// </param>
    /// <param name="eventName">Olay adi. Bos birakilirsa <c>event</c> alani yazilmaz.</param>
    /// <param name="data">Veri govdesi. Satir sonlari SSE kuralina gore bolunur.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public async Task WriteEventAsync(
        long? id,
        string? eventName,
        string data,
        CancellationToken cancellationToken)
    {
        var frame = new StringBuilder();

        if (id is { } sequence)
        {
            frame.Append("id: ").Append(sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        if (!string.IsNullOrEmpty(eventName))
        {
            frame.Append("event: ").Append(eventName).Append('\n');
        }

        // SSE'de her veri satiri kendi "data: " onekini tasir. Coklu satirli bir
        // yuku tek satir gibi yazmak istemcide bozuk cozumlemeye yol acar.
        foreach (var line in data.AsSpan().EnumerateLines())
        {
            frame.Append("data: ").Append(line).Append('\n');
        }

        frame.Append('\n');

        await WriteRawAsync(frame.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hazir bicimlenmis bir cerceveyi oldugu gibi yazar.</summary>
    /// <param name="frame">
    /// Tam SSE cercevesi. Microsoft Agent Framework'un
    /// <c>OpenAIResponses.WriteResponseStreamAsync</c> ciktisi zaten bu bicimdedir;
    /// yeniden cerceveleme bozulmaya yol acardi.
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public async Task WriteRawAsync(string frame, CancellationToken cancellationToken)
    {
        await _response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await _response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Yorum satiri yazar. Istemci yok sayar; baglantiyi ve ara vekilleri canli tutar.
    /// </summary>
    /// <param name="text">Yorum metni.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public Task WriteKeepAliveAsync(string text, CancellationToken cancellationToken)
        => WriteRawAsync($": {text}\n\n", cancellationToken);

    /// <summary>
    /// <c>Last-Event-ID</c> basligini okur ve bir sonraki sira numarasini dondurur.
    /// </summary>
    /// <param name="request">Gelen istek.</param>
    /// <returns>
    /// Okumaya baslanacak sira numarasi. Baslik yoksa veya cozumlenemezse
    /// bastan (<c>0</c>) baslanir.
    /// </returns>
    /// <remarks>
    /// Baslik istemcinin <em>aldigi son</em> olayin kimligidir; okuma bir sonraki
    /// olaydan devam etmelidir, aksi halde son olay tekrar gonderilir.
    /// </remarks>
    public static long ReadResumeSequence(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = request.Headers["Last-Event-ID"].ToString();

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastId) && lastId >= 0
            ? lastId + 1
            : 0;
    }
}
