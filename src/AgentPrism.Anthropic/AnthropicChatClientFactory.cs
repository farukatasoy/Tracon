using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Builds an <see cref="AnthropicClient"/> from settings and produces
/// <see cref="IChatClient"/> instances from model bindings.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The factory returns a <strong>RAW</strong> client. The shared pipeline
/// (<c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, the content guard,
/// the circuit breaker, usage resolution) is built inside
/// <c>ModelProviderRegistry.CreateChatClient</c> — moved there in Phase 48.
/// Rationale: when the loop was built here, none of the layers the registry
/// wrapped around it could see the tool-call turns.
/// </para>
/// <para>
/// <see cref="AnthropicClient"/> is built once and shared; it manages its own HTTP
/// connection pool. Building a new client per call would fragment the pool.
/// </para>
/// <para>
/// 🚨 The Anthropic Messages API treats <c>max_tokens</c> as
/// <strong>required</strong>. When <see cref="ModelBinding.MaxOutputTokens"/> is
/// left empty, <see cref="AnthropicProviderOptions.DefaultMaxOutputTokens"/> is
/// used instead.
/// </para>
/// </remarks>
public sealed class AnthropicChatClientFactory
{
    private readonly IAnthropicClient _client;
    private readonly string? _defaultModel;
    private readonly int _defaultMaxOutputTokens;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Builds a new factory from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException"><see cref="AnthropicProviderOptions.ApiKey"/> is empty.</exception>
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
    /// Setups that manage their own authentication (for example Bedrock/Vertex
    /// identity, or a provider that refreshes tokens) use <see cref="FromClient"/>,
    /// which calls this constructor.
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
    /// Setups that manage their own authentication (for example Bedrock/Vertex
    /// identity, or a provider that refreshes tokens) use this factory method.
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
    /// <exception cref="AgentPrismException">
    /// The model name cannot be resolved, or <see cref="ModelBinding.ProviderSettings"/>
    /// contains an unrecognized key.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new AgentPrismException(
                $"Model name is empty and no default model is defined. Fill in the " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} field in the agent definition, or set " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.DefaultModel)}'.");

        // An unrecognized key is rejected here; the error is wrapped by
        // AgentDefinitionCompiler into AgentPrismCompilationException and compilation stops.
        ModelProviderSettings.Validate(
            binding,
            AnthropicProviderNames.SettingsPrefix,
            AnthropicProviderNames.SupportedSettings);

        var promptCaching = ModelProviderSettings.ReadBoolean(
            binding,
            AnthropicProviderNames.PromptCachingSetting) ?? false;

        var thinkingBudget = ModelProviderSettings.ReadInt32(
            binding,
            AnthropicProviderNames.ThinkingBudgetTokensSetting);

        if (thinkingBudget is { } budget && budget <= 0)
        {
            throw new AgentPrismException(
                $"'{AnthropicProviderNames.ThinkingBudgetTokensSetting}' must be greater than zero. " +
                $"Actual value: {budget}.");
        }

        IChatClient inner = _client.AsIChatClient(model, _defaultMaxOutputTokens);

        // No decorator is added when there is no setting: the plain path must not
        // allocate a ChatOptions copy and a raw body on every request.
        if (AnthropicProviderSettingsChatClient.HasSettings(promptCaching, thinkingBudget))
        {
            inner = new AnthropicProviderSettingsChatClient(
                inner,
                model,
                _defaultMaxOutputTokens,
                promptCaching,
                thinkingBudget);
        }

        return inner;
    }

    /// <summary>Builds an Anthropic client from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <returns>The constructed client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException"><see cref="AnthropicProviderOptions.ApiKey"/> is empty.</exception>
    public static AnthropicClient CreateClient(AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AgentPrismException(
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

        return new AnthropicClient(clientOptions);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
