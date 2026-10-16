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
    private readonly IAnthropicClient _client;
    private readonly string? _defaultModel;
    private readonly int _defaultMaxOutputTokens;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Builds a new factory from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="AnthropicProviderOptions.ApiKey"/> is empty.</exception>
    public AnthropicChatClientFactory(AnthropicProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultModel, options.DefaultMaxOutputTokens, loggerFactory)
    {
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
        // selected with `anthropic.thinking.mode = enabled` and a budget (K-1017).
        //
        // The decorator is always added: it also tells the validation report
        // which output limit this client sends when the binding sets none
        // (ModelOutputLimit). With no setting that changes the body, it passes
        // the caller's options through without a copy or a raw body.
        // The stop-reason reader sits on the adapter itself, inside the settings:
        // a paused turn's continuation goes out with the same settings (F-426).
        return new AnthropicProviderSettingsChatClient(
            new AnthropicStopReasonChatClient(_client.AsIChatClient(model, _defaultMaxOutputTokens)),
            model,
            _defaultMaxOutputTokens,
            settings);
    }

    /// <summary>Builds an Anthropic client from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="egressGuard">
    /// When given, every connection this client opens passes through the
    /// outbound network guard. Supplied only for a tenant-supplied endpoint
    /// override; a setup-time endpoint is the operator's own decision and is
    /// written in code.
    /// </param>
    /// <returns>The constructed client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="AnthropicProviderOptions.ApiKey"/> is empty.</exception>
    public static AnthropicClient CreateClient(AnthropicProviderOptions options, EgressSocketGuard? egressGuard = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new TraconException(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.ApiKey)} is empty. " +
                "Provide the key through the `UseAnthropic(apiKey)` call, or set " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKey)}'.");
        }

        var clientOptions = new Anthropic.Core.ClientOptions
        {
            ApiKey = options.ApiKey,
            Timeout = options.Timeout,
            MaxRetries = options.MaxRetries,
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
