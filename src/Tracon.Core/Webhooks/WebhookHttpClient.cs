using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Provides the <see cref="HttpClient"/> used for webhook delivery, with built-in SSRF protection.
/// </summary>
/// <remarks>
/// <para>
/// Protection is built into <strong>the client itself</strong>, not caller
/// code. A single path that accidentally used an unprotected <see cref="HttpClient"/>
/// would defeat the entire defense. This type does not accept an external
/// <see cref="HttpMessageHandler"/>; an <c>internal</c> constructor exists only for tests.
/// </para>
/// <para>
/// <c>IHttpClientFactory</c> is not used because it would add
/// <c>Microsoft.Extensions.Http</c> to the dependency graph, and a
/// consumer could reconfigure the factory and remove the protection.
/// </para>
/// </remarks>
public sealed class WebhookHttpClient : IDisposable
{
    private readonly HttpClient _client;

    /// <summary>Initializes the production client.</summary>
    /// <param name="optionsMonitor">The webhook options.</param>
    /// <param name="egressOptions">The shared egress options.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="optionsMonitor"/> or <paramref name="egressOptions"/> is <see langword="null"/>.
    /// </exception>
    public WebhookHttpClient(
        IOptionsMonitor<TraconWebhookOptions> optionsMonitor,
        IOptionsMonitor<TraconEgressOptions> egressOptions)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(egressOptions);

        // The connection callback resolves and validates the address for each
        // connection. The validated address is the socket destination, so no TOCTOU gap exists.
        var guard = new EgressSocketGuard(
            () => WebhookUrlValidator.ToPolicy(optionsMonitor.CurrentValue, egressOptions.CurrentValue));

        var handler = guard.CreateHandler();

        // 🚨 Redirects are not followed. A redirect escapes a validated
        // address to a private network: 302 -> http://169.254.169.254.
        // The guard would catch the redirect target too, but refusing the hop
        // outright keeps the recipient from steering delivery at all.
        handler.AllowAutoRedirect = false;
        handler.AutomaticDecompression = System.Net.DecompressionMethods.None;

        _client = new HttpClient(handler, disposeHandler: true)
        {
            // A per-request CancellationTokenSource enforces timeouts. Changing
            // Timeout on a shared client would create a race between threads.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tracon", "1.0"));
        _client.DefaultRequestHeaders.ExpectContinue = false;
    }

    /// <summary>For testing, uses the supplied handler without network validation.</summary>
    /// <param name="handler">The fake transport handler.</param>
    internal WebhookHttpClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>Sends a request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response, with its body not yet read.</returns>
    /// <remarks>
    /// <para>
    /// This returns as soon as the response <strong>headers</strong> arrive;
    /// the body is still on the wire. The per-attempt deadline therefore
    /// belongs to the caller, which is the only party that knows when the
    /// attempt is finished. Passing the deadline in here instead would end it
    /// when the headers landed, and the body read that follows would run with
    /// no deadline at all.
    /// </para>
    /// <para>
    /// The shared <see cref="HttpClient"/> carries an infinite timeout on
    /// purpose: a timeout on a shared client would make one request's deadline
    /// cancel another's.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
