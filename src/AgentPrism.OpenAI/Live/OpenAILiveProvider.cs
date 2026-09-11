using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Creates and attaches to OpenAI live voice sessions.</summary>
/// <remarks>
/// <para>
/// The shape of both calls was measured against the real API on 2026-09-11 rather
/// than taken from documentation. Two findings shaped the design:
/// </para>
/// <list type="bullet">
/// <item>
/// Only the <c>webrtc</c> transport is accepted, so AgentPrism cannot bridge the
/// media itself. It relays the media peer's SDP offer instead, which is what keeps
/// the API key out of the browser.
/// </item>
/// <item>
/// There is no ephemeral-token endpoint for this model
/// (<c>POST /v1/live/client_secrets</c> answers <c>404</c>), and none is needed:
/// AgentPrism creates the session with its own key and attaches with the same one.
/// </item>
/// </list>
/// </remarks>
internal sealed class OpenAILiveProvider : ILiveVoiceProvider, IDisposable
{
    private static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/");

    private readonly IOptionsMonitor<OpenAILiveOptions> _liveOptions;
    private readonly IOptionsMonitor<OpenAIProviderOptions> _providerOptions;
    private readonly EgressSocketGuard _egress;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OpenAILiveProvider> _logger;
    private readonly HttpClient _http;

    /// <summary>Creates a provider.</summary>
    /// <param name="liveOptions">The live options.</param>
    /// <param name="providerOptions">The OpenAI provider options, which carry the API key.</param>
    /// <param name="egress">The outbound address guard.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public OpenAILiveProvider(
        IOptionsMonitor<OpenAILiveOptions> liveOptions,
        IOptionsMonitor<OpenAIProviderOptions> providerOptions,
        EgressSocketGuard egress,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(liveOptions);
        ArgumentNullException.ThrowIfNull(providerOptions);
        ArgumentNullException.ThrowIfNull(egress);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _liveOptions = liveOptions;
        _providerOptions = providerOptions;
        _egress = egress;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<OpenAILiveProvider>();

        // 🚨 Built from the guard, never with `new HttpClient()`: a path that built
        // its own handler would be outside the egress policy without saying so.
        _http = egress.CreateHttpClient(liveOptions.CurrentValue.Timeout);
    }

    /// <inheritdoc />
    public string ProviderName => OpenAIProviderNames.ChatCompletions;

    /// <inheritdoc />
    public string ModelId => _liveOptions.CurrentValue.Model;

    /// <inheritdoc />
    public int MaxAppendCharacters => _liveOptions.CurrentValue.MaxAppendCharacters;

    /// <inheritdoc />
    public async ValueTask<LiveVoiceSessionHandle> CreateSessionAsync(
        LiveVoiceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _liveOptions.CurrentValue;
        var target = new Uri(options.Endpoint ?? DefaultEndpoint, "live/sessions");

        var body = new OpenAILiveCreateRequest
        {
            Transport = new OpenAILiveTransport { Type = "webrtc", Sdp = request.SdpOffer },
            Session = new OpenAILiveSessionBody
            {
                Model = request.Options.Model ?? options.Model,
                Instructions = request.Options.Instructions,
                Audio = (request.Options.Voice ?? options.Voice) is { Length: > 0 } voice
                    ? new OpenAILiveAudio { Output = new OpenAILiveAudioOutput { Voice = voice } }
                    : null,
                Delegation = new OpenAILiveDelegationConfig
                {
                    Type = request.Options.DelegationMode switch
                    {
                        LiveVoiceDelegationMode.Client => "client",
                        LiveVoiceDelegationMode.Responses => throw new NotSupportedException(
                            "The 'Responses' delegation mode is part of the contract but is not implemented yet; " +
                            "use 'Client'."),
                        _ => "client",
                    },
                },
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, OpenAILiveJsonContext.Default.OpenAILiveCreateRequest),
                Encoding.UTF8,
                "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireApiKey());

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // 🚨 The provider's body is NOT forwarded to the caller: it can echo the
            // SDP offer back, and an SDP carries the caller's network addresses.
            _logger.LogWarning(
                "OpenAI refused a live session with status {Status}.",
                (int)response.StatusCode);

            throw new AgentPrismException(
                $"The live voice session could not be created (provider status {(int)response.StatusCode}).");
        }

        var parsed = JsonSerializer.Deserialize(payload, OpenAILiveJsonContext.Default.OpenAILiveCreateResponse);

        if (parsed?.Session?.Id is not { Length: > 0 } sessionId
            || parsed.Transport?.Sdp is not { Length: > 0 } answer)
        {
            throw new AgentPrismException("The live voice provider returned no session identifier or SDP answer.");
        }

        return new LiveVoiceSessionHandle
        {
            ProviderSessionId = sessionId,
            SdpAnswer = answer,
            Model = parsed.Session.Model ?? options.Model,
        };
    }

    /// <inheritdoc />
    public async ValueTask<ILiveVoiceSideband> AttachAsync(
        string providerSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);

        var options = _liveOptions.CurrentValue;
        var root = options.Endpoint ?? DefaultEndpoint;
        var https = new Uri(root, $"live/sessions/{Uri.EscapeDataString(providerSessionId)}/attach");

        // 🚨 ClientWebSocket has no ConnectCallback, so the egress policy cannot be
        // enforced by the transport. It is enforced HERE, before the connection, by
        // resolving and judging the target address.
        await _egress.ValidateAsync(https, cancellationToken).ConfigureAwait(false);

        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + RequireApiKey());

        var wss = new UriBuilder(https)
        {
            Scheme = string.Equals(https.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ? "ws" : "wss",
        }.Uri;

        try
        {
            await socket.ConnectAsync(wss, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();

            throw;
        }

        return new OpenAILiveSideband(socket, _loggerFactory.CreateLogger<OpenAILiveSideband>());
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private string RequireApiKey()
        => _providerOptions.CurrentValue.ApiKey is { Length: > 0 } key
            ? key
            : throw new AgentPrismException(
                "The OpenAI live voice provider needs an API key. Call UseOpenAI(...) before UseOpenAILive(...).");
}
