using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Creates and attaches to OpenAI live voice sessions.</summary>
/// <remarks>
/// <para>
/// The shape of both calls was measured against the real API on 2026-09-11 rather
/// than taken from documentation. Two findings shaped the design:
/// </para>
/// <list type="bullet">
/// <item>
/// Only the <c>webrtc</c> transport is accepted, so Tracon cannot bridge the
/// media itself. It relays the media peer's SDP offer instead, which is what keeps
/// the API key out of the browser.
/// </item>
/// <item>
/// There is no ephemeral-token endpoint for this model
/// (<c>POST /v1/live/client_secrets</c> answers <c>404</c>), and none is needed:
/// Tracon creates the session with its own key and attaches with the same one.
/// </item>
/// </list>
/// <para>
/// A tenant that has its own OpenAI binding gets its own key on both calls
/// (<see cref="SupportsTenantCredentials"/>); the configured key is never used for
/// that tenant. The binding's endpoint override is not: it names a chat API base,
/// not a live session surface, so a binding that carries one is refused rather than
/// sent somewhere the tenant did not choose.
/// </para>
/// </remarks>
internal sealed class OpenAILiveProvider : ILiveVoiceProvider, ILiveSetupVoiceProvider, IDisposable
{
    private static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/");

    private readonly IOptionsMonitor<OpenAILiveOptions> _liveOptions;
    private readonly IOptionsMonitor<OpenAIProviderOptions> _providerOptions;
    private readonly EgressSocketGuard _egress;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OpenAILiveProvider> _logger;
    private readonly HttpClient _http;
    private readonly OpenAINamedChatClientFactoryCache? _setups;
    private readonly SetupCredentialSource? _endpointSource;

    /// <summary>Creates a provider.</summary>
    /// <param name="liveOptions">The live options.</param>
    /// <param name="providerOptions">The OpenAI provider options, which carry the API key.</param>
    /// <param name="egress">The outbound address guard.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="setups">The setups of the OpenAI provider; when its key follows a configuration key, so do the sessions.</param>
    /// <param name="configuration">The configuration the live address is read from when the options name a configuration key for it.</param>
    public OpenAILiveProvider(
        IOptionsMonitor<OpenAILiveOptions> liveOptions,
        IOptionsMonitor<OpenAIProviderOptions> providerOptions,
        EgressSocketGuard egress,
        ILoggerFactory loggerFactory,
        OpenAINamedChatClientFactoryCache? setups = null,
        IConfiguration? configuration = null)
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

        _setups = setups;

        if (!string.IsNullOrWhiteSpace(liveOptions.CurrentValue.EndpointConfigurationName))
        {
            _endpointSource = new SetupCredentialSource(
                "openai-live",
                OpenAIChatClientFactory.ApiKeyOptionName,
                startupApiKey: null,
                liveOptions.CurrentValue.Endpoint,
                apiKeyName: null,
                liveOptions.CurrentValue.EndpointConfigurationName,
                configuration,
                _logger,
                keyless: true);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The key of the OpenAI provider and the live address, read together once for a session. <see langword="null"/> when
    /// neither follows a configuration key: the session then reads the options as it always did.
    /// </remarks>
    public ModelProviderCredential? SetupCredential()
    {
        var keySetup = _setups?.Setup(Options.DefaultName).Live;

        if (keySetup is null && _endpointSource is null)
        {
            return null;
        }

        var key = keySetup is not null
            ? keySetup.Snapshot().ApiKey
            : OutboundApiKey.Normalize(OpenAIChatClientFactory.ApiKeyOptionName, _providerOptions.CurrentValue.ApiKey)
                ?? throw new TraconException("The OpenAI live voice provider needs an API key. Call UseOpenAI(...) before UseOpenAILive(...).");

        return TenantCredentialScope.AttachSetup(
            new ModelProviderCredential { ApiKey = key, Endpoint = _endpointSource?.Current().Endpoint },
            "openai-live");
    }

    /// <inheritdoc />
    public string ProviderName => OpenAIProviderNames.ChatCompletions;

    /// <inheritdoc />
    public string ModelId => _liveOptions.CurrentValue.Model;

    /// <inheritdoc />
    public int MaxAppendCharacters => _liveOptions.CurrentValue.MaxAppendCharacters;

    /// <inheritdoc />
    public bool SupportsTenantCredentials => true;

    /// <inheritdoc />
    public async ValueTask<LiveVoiceSessionHandle> CreateSessionAsync(
        LiveVoiceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _liveOptions.CurrentValue;
        var target = new Uri(RootFor(request.Credential, options), "live/sessions");

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
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(request),
                            request.Options.DelegationMode,
                            "The live voice delegation mode is not one this provider knows."),
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

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKeyFor(request.Credential));

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // 🚨 The provider's body is NOT forwarded to the caller: it can echo the
            // SDP offer back, and an SDP carries the caller's network addresses.
            _logger.LogWarning(
                "OpenAI refused a live session with status {Status}.",
                (int)response.StatusCode);

            throw new TraconException(
                $"The live voice session could not be created (provider status {(int)response.StatusCode}).");
        }

        var parsed = JsonSerializer.Deserialize(payload, OpenAILiveJsonContext.Default.OpenAILiveCreateResponse);

        if (parsed?.Session?.Id is not { Length: > 0 } sessionId
            || parsed.Transport?.Sdp is not { Length: > 0 } answer)
        {
            throw new TraconException("The live voice provider returned no session identifier or SDP answer.");
        }

        return new LiveVoiceSessionHandle
        {
            ProviderSessionId = sessionId,
            SdpAnswer = answer,
            Model = parsed.Session.Model ?? options.Model,
        };
    }

    /// <inheritdoc />
    public ValueTask<ILiveVoiceSideband> AttachAsync(
        string providerSessionId,
        CancellationToken cancellationToken = default)
        => AttachSessionAsync(new LiveVoiceAttachRequest { ProviderSessionId = providerSessionId }, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ILiveVoiceSideband> AttachSessionAsync(
        LiveVoiceAttachRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerSessionId = request.ProviderSessionId;

        if (string.IsNullOrWhiteSpace(providerSessionId))
        {
            throw new ArgumentException("The provider session identifier is required.", nameof(request));
        }

        var apiKey = ApiKeyFor(request.Credential);

        var options = _liveOptions.CurrentValue;
        var root = RootFor(request.Credential, options);
        var https = new Uri(root, $"live/sessions/{Uri.EscapeDataString(providerSessionId)}/attach");

        // 🚨 The socket opens through the egress guard's own handler (ClientWebSocket takes an invoker), so the
        // guard resolves the name once, judges the answer and connects to that address in its connect callback.
        // A check made before a connection the socket opened by itself resolved the name twice: a name that
        // answered differently the second time was judged on one address and opened on another.
        using var handler = _egress.CreateHandler();
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);

        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);

        var wss = new UriBuilder(https)
        {
            Scheme = string.Equals(https.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ? "ws" : "wss",
        }.Uri;

        try
        {
            await socket.ConnectAsync(wss, invoker, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            socket.Dispose();

            // The guard's refusal travels inside the handshake failure; the caller is told what was refused
            // (the setting to change), not that "the WebSocket could not connect".
            if (FindRefusal(exception) is { } refusal)
            {
                throw refusal;
            }

            throw;
        }

        return new OpenAILiveSideband(socket, _loggerFactory.CreateLogger<OpenAILiveSideband>());
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>Finds the verdict of the egress guard in a failed connection.</summary>
    /// <param name="exception">The failure the socket raised.</param>
    /// <returns>The guard's <see cref="TraconException"/>, or <see langword="null"/> when the failure is the transport's own.</returns>
    private static TraconException? FindRefusal(Exception exception)
        => ProviderFailureGraph.Frames(exception).OfType<TraconException>().FirstOrDefault();

    // The live address of a session. A setup credential carries the address read for this session; a tenant's credential
    // never does (a binding's endpoint names a chat API base and is refused by ApiKeyFor), so the options decide.
    private static Uri RootFor(ModelProviderCredential? credential, OpenAILiveOptions options)
        => credential is not null
            && TenantCredentialScope.IsSetup(credential)
            && Uri.TryCreate(credential.Endpoint, UriKind.Absolute, out var snapshotRoot)
                ? snapshotRoot
                : options.Endpoint ?? DefaultEndpoint;

    /// <summary>Picks the key a call is made with: the tenant's when given, otherwise the configured one.</summary>
    /// <param name="credential">The tenant's credential, or <see langword="null"/>.</param>
    /// <returns>The API key.</returns>
    /// <exception cref="TraconException">
    /// The tenant credential carries an endpoint override, or no credential is given and
    /// none is configured.
    /// </exception>
    private string ApiKeyFor(ModelProviderCredential? credential)
    {
        if (credential is not null && TenantCredentialScope.IsSetup(credential))
        {
            // The key and the address this session was opened with, read once by the launcher.
            return credential.ApiKey;
        }

        if (credential is not null)
        {
            // The tenant's key and nothing else: no fallback to the configured key.
            return credential.Endpoint is { Length: > 0 }
                ? throw new TraconException(
                    "The tenant's OpenAI binding sets an endpoint override. A live voice session uses the " +
                    "configured live endpoint and cannot honour that override, so it is not opened.")
                : credential.ApiKey;
        }

        // The configured key is trimmed and checked like the one the chat client is built with.
        return OutboundApiKey.Normalize(OpenAIChatClientFactory.ApiKeyOptionName, _providerOptions.CurrentValue.ApiKey) is { } key
            ? key
            : throw new TraconException(
                "The OpenAI live voice provider needs an API key. Call UseOpenAI(...) before UseOpenAILive(...).");
    }
}
