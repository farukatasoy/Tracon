using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Webhook teslimlerinin kullandigi, SSRF korumasi gomulu <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Koruma <strong>istemcinin kendisine</strong> gomulüdur, cagiran koda
/// degil. Sebep: yanlislikla korumasiz bir <see cref="HttpClient"/> kullanan
/// tek bir kod yolu tum savunmayi bosa cikarirdi. Bu tip disaridan bir
/// <see cref="HttpMessageHandler"/> kabul etmez — yalnizca testler icin
/// <c>internal</c> bir kurucu vardir.
/// </para>
/// <para>
/// <c>IHttpClientFactory</c> kullanilmaz: <c>Microsoft.Extensions.Http</c>
/// paketi bagimlilik grafigine eklenirdi (K-007) ve tuketicinin fabrikayi
/// yeniden yapilandirmasi korumayi kaldirabilirdi (K-164).
/// </para>
/// </remarks>
public sealed class WebhookHttpClient : IDisposable
{
    private readonly HttpClient _client;

    /// <summary>Uretim istemcisini kurar.</summary>
    /// <param name="optionsMonitor">Webhook ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> <see langword="null"/> ise.</exception>
    public WebhookHttpClient(IOptionsMonitor<AgentPrismWebhookOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        var handler = new SocketsHttpHandler
        {
            // 🚨 Yonlendirme IZLENMEZ. Bir yonlendirme, denetimden gecmis bir
            // adresten ozel bir aga kacis yoludur: 302 -> http://169.254.169.254.
            AllowAutoRedirect = false,

            // Baglanti geri cagrisi her baglantida adresi cozer ve denetler;
            // dogrulanan adres soketin baglandigi adrestir (TOCTOU yok).
            ConnectCallback = WebhookSocketGuard.Create(() => optionsMonitor.CurrentValue),

            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            // Zaman asimi istek basina CancellationTokenSource ile uygulanir;
            // paylasilan bir istemcide Timeout alanini degistirmek is parcaciklari
            // arasinda yaris kosulu yaratirdi.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AgentPrism", "1.0"));
        _client.DefaultRequestHeaders.ExpectContinue = false;
    }

    /// <summary>Test icin: verilen isleyiciyi kullanir, ag denetimi yapmaz.</summary>
    /// <param name="handler">Sahte aktarim isleyicisi.</param>
    internal WebhookHttpClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>Bir istek gonderir.</summary>
    /// <param name="request">Istek.</param>
    /// <param name="timeout">Bu istege ozgu zaman asimi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yanit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> <see langword="null"/> ise.</exception>
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (timeout > TimeSpan.Zero)
        {
            timeoutSource.CancelAfter(timeout);
        }

        return await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
