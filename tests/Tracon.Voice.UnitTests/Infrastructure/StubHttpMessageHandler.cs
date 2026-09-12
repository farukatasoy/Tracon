using System.Net;

namespace Tracon.Voice.UnitTests.Infrastructure;

/// <summary>
/// Fake HTTP handler that records incoming requests and returns a canned response.
/// </summary>
/// <remarks>
/// No test reaches a real voice provider (decision valid since Phase 3).
/// The request shape — path, header, body — is verified here.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Requests seen, in arrival order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The body is read BEFORE the request is disposed: once the call finishes,
        // the content is discarded and can no longer be verified.
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

    /// <summary>A single recorded request.</summary>
    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        Dictionary<string, string> Headers,
        string? Body,
        string? ContentType);

    /// <summary>Produces a successful response with a binary body.</summary>
    public static HttpResponseMessage Binary(byte[] data, string mediaType = "audio/mpeg")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data),
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

        return response;
    }

    /// <summary>Produces a successful response with a JSON body.</summary>
    public static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
}
