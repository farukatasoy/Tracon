using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Builds a Google GenAI <see cref="Client"/> from settings and produces
/// <see cref="IChatClient"/> instances from model bindings.
/// </summary>
/// <remarks>
/// <para>
/// The factory returns a <strong>RAW</strong> client. The shared pipeline
/// (<c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, the content guard,
/// the circuit breaker, cost resolution) is built inside
/// <c>ModelProviderRegistry.CreateChatClient</c>.
/// When the loop was built here, no ring wrapped by the ledger could
/// see the tool call rounds.
/// </para>
/// <para>
/// The <see cref="Client"/> is built once and shared; it manages its own HTTP
/// connection pool. The factory implements <see cref="IDisposable"/>, so the
/// client closes when the container shuts down.
/// </para>
/// </remarks>
internal sealed class GoogleChatClientFactory : IDisposable
{
    private readonly Client _client;
    private readonly bool _ownsClient;
    private readonly string? _defaultModel;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Builds a new factory from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="GoogleProviderOptions.ApiKey"/> is empty.</exception>
    public GoogleChatClientFactory(GoogleProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultModel, loggerFactory, ownsClient: true)
    {
    }

    /// <summary>Builds a new factory from a ready-made client.</summary>
    /// <param name="client">Google GenAI client to use.</param>
    /// <param name="defaultModel">Model to use when no model name is given.</param>
    /// <param name="loggerFactory">Logger factory passed to produced clients.</param>
    /// <returns>The new factory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The model provider builds a tenant's own-credential factory this way: it
    /// creates the client for that credential, then wraps it here. The lifetime
    /// of a client passed this way belongs to the <em>caller</em>; the factory
    /// does not close it.
    /// </remarks>
    public static GoogleChatClientFactory FromClient(Client client, string? defaultModel = null, ILoggerFactory? loggerFactory = null)
        => new(client, defaultModel, loggerFactory, ownsClient: false);

    private GoogleChatClientFactory(Client client, string? defaultModel, ILoggerFactory? loggerFactory, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _ownsClient = ownsClient;
        _defaultModel = Trim(defaultModel);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Produces a chat client for the given binding.</summary>
    /// <param name="binding">Model binding.</param>
    /// <returns>A chat client that has gone through the pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The model name cannot be resolved, or <see cref="ModelBinding.ProviderSettings"/>
    /// carries an unknown key or threshold value.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new ProviderSettingsValidationException(
                $"Model name is empty and no default model is defined. Fill in the " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} field in the agent definition, or set " +
                $"'{GoogleProviderOptions.SectionName}:{nameof(GoogleProviderOptions.DefaultModel)}'.");

        // An unknown key is rejected here; the error is wrapped in
        // TraconCompilationException by AgentDefinitionCompiler and stops compilation.
        ModelProviderSettings.Validate(
            binding,
            GoogleProviderNames.SettingsPrefix,
            GoogleProviderNames.SupportedSettings);

        var safetySettings = GoogleSafetySettings.Read(binding);

        var thinkingBudget = ModelProviderSettings.ReadInt32(
            binding,
            GoogleProviderNames.ThinkingBudgetTokensSetting);

        var includeThoughts = ModelProviderSettings.ReadBoolean(
            binding,
            GoogleProviderNames.ThinkingIncludeThoughtsSetting);

        // Gemini requires the budget in the [-1, 65535] range; -1 means "leave it to
        // the model", 0 means "off". An out-of-range value would be rejected at run
        // time by the request, so it is caught at compile time instead.
        if (thinkingBudget is { } budget && budget is < -1 or > 65535)
        {
            throw new ProviderSettingsValidationException(
                $"'{GoogleProviderNames.ThinkingBudgetTokensSetting}' must be in the [-1, 65535] range " +
                $"(-1 leaves it to the model, 0 turns thinking off). Actual value: {budget}.");
        }

        IChatClient inner = _client.AsIChatClient(model);

        // The decorator is skipped entirely when there is no setting: the plain path
        // must not produce a ChatOptions copy and a settings object on every request.
        if (GoogleProviderSettingsChatClient.HasSettings(safetySettings, thinkingBudget, includeThoughts))
        {
            inner = new GoogleProviderSettingsChatClient(inner, safetySettings, thinkingBudget, includeThoughts);
        }

        return inner;
    }

    /// <summary>Creates an image generator that shares this factory's Google client.</summary>
    /// <returns>The native Microsoft.Extensions.AI image generator.</returns>
#pragma warning disable MEAI001
    internal IImageGenerator CreateImageGenerator() => new GoogleImageGenerator(_client);
#pragma warning restore MEAI001

    /// <summary>Builds a Google GenAI client from settings.</summary>
    /// <param name="options">Provider settings.</param>
    /// <param name="egressGuard">
    /// When given, every connection this client opens passes through the
    /// outbound network guard. Supplied only for a tenant-supplied endpoint
    /// override; a setup-time endpoint is the operator's own decision and is
    /// written in code.
    /// </param>
    /// <returns>The built client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="GoogleProviderOptions.ApiKey"/> is empty.</exception>
    /// <remarks>
    /// The base address is passed via <see cref="HttpOptions"/>. The SDK's static
    /// <c>Client.setDefaultBaseUrl</c> method is deliberately not used: it mutates
    /// process-wide state, which would make it impossible to use two different
    /// Gemini endpoints in the same application.
    /// </remarks>
    public static Client CreateClient(GoogleProviderOptions options, EgressSocketGuard? egressGuard = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new TraconException(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.ApiKey)} is empty. " +
                "Pass the key in the `UseGoogle(apiKey)` call, or " +
                $"set '{GoogleProviderOptions.SectionName}:{nameof(GoogleProviderOptions.ApiKey)}'.");
        }

        HttpOptions? httpOptions = null;

        if (options.Endpoint is not null || options.ApiVersion is not null || options.Timeout is not null)
        {
            httpOptions = new HttpOptions
            {
                BaseUrl = options.Endpoint?.ToString(),
                ApiVersion = options.ApiVersion,
                Timeout = options.Timeout is { } timeout ? (int)timeout.TotalMilliseconds : null,
            };
        }

        // 🚨 Measured, not guessed: the Google SDK's hook is a factory
        // (Google.GenAI.Types.ClientOptions.HttpClientFactory), not a client
        // instance. The SDK calls it when it needs a client.
        var clientOptions = egressGuard is null
            ? null
            : new Google.GenAI.Types.ClientOptions
            {
                // Infinite on purpose: the request bound is HttpOptions.Timeout.
                HttpClientFactory = () => egressGuard.CreateHttpClient(Timeout.InfiniteTimeSpan),
            };

        return new Client(apiKey: options.ApiKey, httpOptions: httpOptions, clientOptions: clientOptions);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
