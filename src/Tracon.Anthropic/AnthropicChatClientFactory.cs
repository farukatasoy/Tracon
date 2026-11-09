using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Builds an <see cref="AnthropicClient"/> from settings and produces
/// <see cref="IChatClient"/> instances from model bindings.
/// </summary>
/// <remarks>
/// <para>
/// The factory returns a <strong>RAW</strong> client. The shared pipeline
/// (<c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, the content guard,
/// the circuit breaker, usage resolution) is built inside
/// <c>ModelProviderRegistry.CreateChatClient</c>.
/// When the loop was built here, none of the layers the registry
/// wrapped around it could see the tool-call turns.
/// </para>
/// <para>
/// <see cref="AnthropicClient"/> is built once and shared; it manages its own HTTP
/// connection pool. Building a new client per call would fragment the pool.
/// </para>
/// <para>
/// The Anthropic Messages API treats <c>max_tokens</c> as
/// <strong>required</strong>. When <see cref="ModelBinding.MaxOutputTokens"/> is
/// left empty, <see cref="AnthropicProviderOptions.DefaultMaxOutputTokens"/> is
/// used instead.
/// </para>
/// </remarks>
internal sealed class AnthropicChatClientFactory
{
    /// <summary>The option the setup-time key is read from, as it is written in a failure.</summary>
    internal const string ApiKeyOptionName = nameof(AnthropicProviderOptions) + "." + nameof(AnthropicProviderOptions.ApiKey);

    private readonly IAnthropicClient _client;
    private readonly string? _defaultModel;
    private readonly int _defaultMaxOutputTokens;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Builds a new factory from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <param name="timeProvider">The clock a date in a <c>Retry-After</c> header is read against; <see cref="TimeProvider.System"/> when omitted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <see cref="AnthropicProviderOptions.ApiKey"/> is empty, or contains a character an HTTP header cannot carry.
    /// </exception>
    /// <remarks>
    /// This is the setup-time key, so it is trimmed and checked before a header is built from it
    /// (<c>OutboundApiKey</c>).
    /// </remarks>
    public AnthropicChatClientFactory(AnthropicProviderOptions options, ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null)
        : this(options, SetupKey(options), loggerFactory, timeProvider)
    {
    }

    /// <summary>Builds a new factory from settings and the setup-time key as it is sent.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="setupApiKey">The key, trimmed and checked; the one value the client is built with and the factory reports.</param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <param name="timeProvider">The clock a date in a <c>Retry-After</c> header is read against.</param>
    private AnthropicChatClientFactory(AnthropicProviderOptions options, string setupApiKey, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
        : this(Build(options, setupApiKey, egressGuard: null, timeProvider), options.DefaultModel, options.DefaultMaxOutputTokens, loggerFactory)
    {
        SetupApiKey = setupApiKey;
    }

    /// <summary>Builds a new factory from an already-constructed client.</summary>
    /// <param name="client">The Anthropic client to use.</param>
    /// <param name="defaultModel">The model to use when no model name is given.</param>
    /// <param name="defaultMaxOutputTokens">
    /// The upper bound used when <see cref="ModelBinding.MaxOutputTokens"/> is not given.
    /// </param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultMaxOutputTokens"/> is not positive.</exception>
    /// <remarks>
    /// <see cref="FromClient"/> calls this constructor.
    /// </remarks>
    private AnthropicChatClientFactory(
        IAnthropicClient client,
        string? defaultModel,
        int defaultMaxOutputTokens,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultMaxOutputTokens);

        _client = client;
        _defaultModel = Trim(defaultModel);
        _defaultMaxOutputTokens = defaultMaxOutputTokens;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Builds a new factory from an already-constructed client.</summary>
    /// <param name="client">The Anthropic client to use.</param>
    /// <param name="defaultModel">The model to use when no model name is given.</param>
    /// <param name="defaultMaxOutputTokens">
    /// The upper bound used when <see cref="ModelBinding.MaxOutputTokens"/> is not given.
    /// </param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <returns>The new factory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultMaxOutputTokens"/> is not positive.</exception>
    /// <remarks>
    /// The model provider builds a tenant's own-credential factory this way: it
    /// creates the client for that credential, then wraps it here.
    /// </remarks>
    public static AnthropicChatClientFactory FromClient(
        IAnthropicClient client,
        string? defaultModel = null,
        int defaultMaxOutputTokens = 4096,
        ILoggerFactory? loggerFactory = null)
        => new(client, defaultModel, defaultMaxOutputTokens, loggerFactory);

    /// <summary>Produces a chat client for the given binding.</summary>
    /// <param name="binding">The model binding.</param>
    /// <returns>A chat client that has passed through the pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The model name cannot be resolved, or <see cref="ModelBinding.ProviderSettings"/>
    /// contains an unrecognized key.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new ProviderSettingsValidationException(
                $"Model name is empty and no default model is defined. Fill in the " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} field in the agent definition, or set " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.DefaultModel)}'.");

        // An unrecognized key, a wrong value or a contradiction is rejected
        // here; the error is wrapped by AgentDefinitionCompiler into
        // TraconCompilationException and compilation stops.
        var settings = AnthropicRequestSettings.Read(binding);

        // 🚨 `ModelBinding.ReasoningEffort` becomes ADAPTIVE thinking
        // (`thinking.type=adaptive` plus `output_config.effort`), the SDK
        // adapter's default. Measured live against every current model (Claude 5
        // family, 2026-10-17): the budget form (`thinking.type=enabled`) is
        // rejected with 400 and the adaptive form is accepted. A model that
        // predates adaptive thinking (claude-haiku-4-5) is the reverse; it is
        // selected with `anthropic.thinking.mode = enabled` and a budget.
        //
        // The decorator is always added: it also tells the validation report
        // which output limit this client sends when the binding sets none
        // (ModelOutputLimit). With no setting that changes the body, it passes
        // the caller's options through without a copy or a raw body.
        // The stop-reason reader sits on the adapter itself, inside the settings:
        // a paused turn's continuation goes out with the same settings.
        // The cache-usage reader sits below it, directly on the adapter, so every API call
        // (a continuation included) reports its own cache writes before the layers above sum them.
        return new AnthropicProviderSettingsChatClient(
            new AnthropicStopReasonChatClient(
                new AnthropicCacheUsageChatClient(
                    _client.AsIChatClient(model, _defaultMaxOutputTokens),
                    settings.CacheTtl,
                    _loggerFactory?.CreateLogger<AnthropicCacheUsageChatClient>())),
            model,
            _defaultMaxOutputTokens,
            settings);
    }

    /// <summary>Gets the key the setup-time client sends, as it is sent; <see langword="null"/> for a factory built from a client.</summary>
    /// <remarks>
    /// The trimmed value, read once: a service that refuses a key quotes what it received, so a second read of the
    /// option could name a different value. The model provider reports it as <see cref="IModelProvider.SetupApiKeys"/>.
    /// </remarks>
    internal string? SetupApiKey { get; }

    /// <summary>Reads the key set up at start, trimmed and checked.</summary>
    /// <param name="options">Provider settings.</param>
    /// <returns>The key to send.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <see cref="AnthropicProviderOptions.ApiKey"/> is empty, or contains a character an HTTP header cannot carry.
    /// </exception>
    /// <remarks>
    /// The key is trimmed and checked first. The client validates the header it builds and quotes the whole
    /// value when it refuses it, so a key that ends in a line break must never reach it as it is.
    /// </remarks>
    private static string SetupKey(AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return OutboundApiKey.Normalize(ApiKeyOptionName, options.ApiKey) ?? throw EmptyKey();
    }

    /// <summary>Builds an Anthropic client from the key of a tenant's own credential.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="egressGuard">
    /// When given, every connection this client opens passes through the
    /// outbound network guard. Supplied only for a tenant-supplied endpoint
    /// override; a setup-time endpoint is the operator's own decision and is
    /// written in code.
    /// </param>
    /// <param name="timeProvider">The clock a date in a <c>Retry-After</c> header is read against; <see cref="TimeProvider.System"/> when omitted.</param>
    /// <returns>The constructed client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="AnthropicProviderOptions.ApiKey"/> is empty.</exception>
    /// <remarks>
    /// The key is used as it was stored: a tenant's key is masked wherever the text of its failure is shown,
    /// and the setup-time key goes through the constructor instead.
    /// </remarks>
    public static AnthropicClient CreateClient(AnthropicProviderOptions options, EgressSocketGuard? egressGuard = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw EmptyKey();
        }

        return Build(options, options.ApiKey, egressGuard, timeProvider);
    }

    private static TraconException EmptyKey()
        => new(
            $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.ApiKey)} is empty. " +
            "Provide the key through the `UseAnthropic(apiKey)` call, or set " +
            $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKey)}'.");

    private static AnthropicClient Build(AnthropicProviderOptions options, string apiKey, EgressSocketGuard? egressGuard, TimeProvider? timeProvider)
    {
        var clientOptions = new Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey,
            Timeout = options.Timeout,
            MaxRetries = options.MaxRetries,

            // The library obeys a Retry-After of any length (a day-long header held a run for a day); the
            // handler drops the ones above a minute so its own backoff applies.
            Handlers = [new AnthropicRetryAfterHandler(timeProvider ?? TimeProvider.System)],
        };

        // BaseUrl does not accept null; when it is not given, the SDK's own default
        // (https://api.anthropic.com) must stay in effect.
        if (options.Endpoint is { } endpoint)
        {
            clientOptions.BaseUrl = endpoint.ToString();
        }

        if (egressGuard is not null)
        {
            // Infinite on purpose: these SDKs apply their own per-request
            // network timeout (NetworkTimeout / ClientOptions.Timeout), and a
            // second bound here would race with it.
            clientOptions.HttpClient = egressGuard.CreateHttpClient(Timeout.InfiniteTimeSpan);
        }

        return new AnthropicClient(clientOptions);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
