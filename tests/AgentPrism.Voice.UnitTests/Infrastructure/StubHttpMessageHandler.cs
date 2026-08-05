using System.Net;

namespace AgentPrism.Voice.UnitTests.Infrastructure;

/// <summary>
/// Gelen istekleri kaydeden ve hazir yanit donduren sahte HTTP isleyicisi.
/// </summary>
/// <remarks>
/// Gercek bir ses saglayicisina HICBIR test cikmaz (Faz 3'ten beri gecerli
/// karar). Istek bicimi — yol, baslik, govde — burada dogrulanir.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Gorulen istekler, gelis sirasiyla.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Govde istek elden cikmadan ONCE okunur: cagri bittikten sonra icerik
        // atilmis olur ve dogrulanamaz.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers
                .ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase),
            body,
            request.Content?.Headers.ContentType?.MediaType));

        return _responder(request);
    }

    /// <summary>Kaydedilmis tek bir istek.</summary>
    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        Dictionary<string, string> Headers,
        string? Body,
        string? ContentType);

    /// <summary>Ikili govdeli basarili bir yanit uretir.</summary>
    public static HttpResponseMessage Binary(byte[] data, string mediaType = "audio/mpeg")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data),
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

        return response;
    }

    /// <summary>JSON govdeli basarili bir yanit uretir.</summary>
    public static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
}
