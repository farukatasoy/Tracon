using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Responses;

namespace Tracon;

/// <summary>
/// Builds an <see cref="OpenAIClient"/> from options and produces
/// <see cref="IChatClient"/> instances from model bindings.
/// </summary>
/// <remarks>
/// <para>
/// The factory returns a <strong>RAW</strong> client. The shared pipeline
/// (<c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, the content guard,
/// the circuit breaker and attachment resolution) is built inside
/// <c>ModelProviderRegistry.CreateChatClient</c> — it.
/// when the loop was built here, no decorator wrapped by the recorder could observe
/// the tool call turns.
/// </para>
/// <para>
/// The <see cref="OpenAIClient"/> is built once and shared; it manages the HTTP
/// connection pool itself. Building a new client per chat client fragments that pool.
/// </para>
/// </remarks>
internal sealed class OpenAIChatClientFactory
{
    /// <summary>The API base the official client uses when no endpoint is set.</summary>
    private static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/");

    private readonly OpenAIClient _client;
    private readonly Uri? _endpoint;
    private readonly string? _defaultModel;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Initializes a new factory from options.</summary>
    /// <param name="options">Provider options.</param>
    /// <param name="loggerFactory">Logger factory handed to the produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="OpenAIProviderOptions.ApiKey"/> is empty.</exception>
    public OpenAIChatClientFactory(OpenAIProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.Endpoint ?? DefaultEndpoint, options.DefaultModel, loggerFactory)
    {
    }

    /// <summary>Initializes a new factory from an existing client.</summary>
    /// <param name="client">The OpenAI client to use.</param>
    /// <param name="endpoint">The API base the client was built with, or <see langword="null"/> when it is not known.</param>
    /// <param name="defaultModel">The model to use when no model name is given.</param>
    /// <param name="loggerFactory">Logger factory handed to the produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="FromClient"/> calls this constructor.
    /// </remarks>
    private OpenAIChatClientFactory(OpenAIClient client, Uri? endpoint, string? defaultModel, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _endpoint = endpoint;
        _defaultModel = Trim(defaultModel);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Initializes a new factory from an existing client.</summary>
    /// <param name="client">The OpenAI client to use.</param>
    /// <param name="defaultModel">The model to use when no model name is given.</param>
    /// <param name="loggerFactory">Logger factory handed to the produced clients.</param>
    /// <returns>The new factory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The model provider builds a tenant's own-credential factory this way: it
    /// creates the client for that credential (or for a named compatible
    /// endpoint), then wraps it here.
    /// </remarks>
    public static OpenAIChatClientFactory FromClient(OpenAIClient client, string? defaultModel = null, ILoggerFactory? loggerFactory = null)
        => new(client, endpoint: null, defaultModel, loggerFactory);

    /// <summary>Creates a chat client for the given binding.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="apiSurface">The OpenAI API surface to use.</param>
    /// <returns>A chat client that the pipeline wraps.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The model name cannot be resolved, or <see cref="ModelBinding.ProviderSettings"/>
    /// contains a key that is not in <see cref="OpenAIProviderNames.SupportedSettings"/>, or a
    /// supported key with a value of the wrong type.
    /// </exception>
    /// <remarks>
    /// Every provider of this package reaches this method: both surfaces of
    /// <c>UseOpenAI()</c>, every <c>UseOpenAICompatible()</c> registration,
    /// and the per-tenant credential path. The settings check therefore runs
    /// here, once, for all of them.
    /// </remarks>
    public IChatClient CreateChatClient(ModelBinding binding, OpenAIApiSurface apiSurface)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var model = Trim(binding.Model) ?? _defaultModel
            ?? throw new ProviderSettingsValidationException(
                "The model name is empty and no default model is configured. Set the " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Model)} field of the agent definition, or " +
                $"configure '{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.DefaultModel)}'.");

        // Every key that is not in the supported list is rejected here and the
        // error is wrapped by AgentDefinitionCompiler into
        // TraconCompilationException. Silently ignoring it would leave the
        // user without the behavior they expected.
        ModelProviderSettings.Validate(
            binding,
            OpenAIProviderNames.SettingsPrefix,
            OpenAIProviderNames.SupportedSettings);

        var client = CreateInnerChatClient(model, apiSurface);

        return ModelProviderSettings.ReadBoolean(binding, OpenAIProviderNames.StrictJsonSchemaSetting) is true
            ? new OpenAIStrictResponseSchemaChatClient(client)
            : client;
    }

    /// <summary>Creates an image generator that shares this factory's OpenAI client.</summary>
    /// <param name="model">The image model.</param>
    /// <returns>The native Microsoft.Extensions.AI image generator.</returns>
    /// <remarks>
    /// The image surface is marked experimental by MEAI 10.11.0. It remains in
    /// this one factory method so a future MEAI change does not spread through
    /// the provider package.
    /// </remarks>
#pragma warning disable MEAI001
    internal IImageGenerator CreateImageGenerator(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        // Not GetImageClient(model).AsIImageGenerator(): a host that constructs an ImageClient
        // cannot be published with Native AOT (see OpenAIPipelineImageGenerator).
        return new OpenAIPipelineImageGenerator(
            _client,
            _endpoint ?? throw new TraconException(
                "OpenAI images need the setup-time OpenAI client, whose address is known. " +
                "A client built from a tenant's own credential carries no image registration."),
            model,
            OpenAIProviderNames.ChatCompletions);
    }
#pragma warning restore MEAI001

    /// <summary>Creates a text-to-speech client that shares this factory's OpenAI client.</summary>
    /// <param name="model">The speech model. The client is bound to it for its whole life.</param>
    /// <returns>The text-to-speech client.</returns>
    /// <remarks>
    /// The speech surface is marked experimental by MEAI 10.11.0. Like the image surface
    /// above, it stays in this factory so a future MEAI change does not spread through the package.
    /// The client is Tracon's own, written over the shared pipeline: see
    /// <see cref="OpenAITextToSpeechClient"/> and <see cref="OpenAIAudioTransport"/> for the measurements.
    /// </remarks>
#pragma warning disable MEAI001
    internal ITextToSpeechClient CreateTextToSpeechClient(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new OpenAITextToSpeechClient(AudioTransport(), model);
    }

    /// <summary>Creates a speech-to-text client that shares this factory's OpenAI client.</summary>
    /// <param name="model">The transcription model. The client is bound to it for its whole life.</param>
    /// <returns>The speech-to-text client.</returns>
    internal ISpeechToTextClient CreateSpeechToTextClient(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new OpenAISpeechToTextClient(AudioTransport(), model);
    }
#pragma warning restore MEAI001

    private OpenAIAudioTransport AudioTransport()
        => new(
            _client,
            _endpoint ?? throw new TraconException(
                "OpenAI speech needs the setup-time OpenAI client, whose address is known. " +
                "A client built from a tenant's own credential carries no speech registration."));

    /// <summary>Builds an OpenAI client from options.</summary>
    /// <param name="options">Provider options.</param>
    /// <param name="egressGuard">
    /// When given, every connection this client opens passes through the
    /// outbound network guard. Supplied only for a tenant-supplied endpoint
    /// override; a setup-time endpoint is the operator's own decision and is
    /// written in code.
    /// </param>
    /// <returns>The client that was built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException"><see cref="OpenAIProviderOptions.ApiKey"/> is empty.</exception>
    public static OpenAIClient CreateClient(OpenAIProviderOptions options, EgressSocketGuard? egressGuard = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new TraconException(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.ApiKey)} is empty. " +
                $"Pass the key to the `UseOpenAI(apiKey)` call, or define " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.ApiKey)}'.");
        }

        var clientOptions = new OpenAIClientOptions();

        if (options.Endpoint is not null)
        {
            clientOptions.Endpoint = options.Endpoint;
        }

        if (!string.IsNullOrWhiteSpace(options.Organization))
        {
            clientOptions.OrganizationId = options.Organization;
        }

        if (options.Timeout is { } timeout)
        {
            clientOptions.NetworkTimeout = timeout;
        }

        // 🚨 Measured, not guessed: OpenAIClientOptions derives from
        // System.ClientModel's ClientPipelineOptions, which exposes a
        // PipelineTransport rather than an HttpClient. HttpClientPipelineTransport
        // is the adapter that lets a guarded HttpClient carry the pipeline.
        if (egressGuard is not null)
        {
            // Infinite on purpose: these SDKs apply their own per-request
            // network timeout (NetworkTimeout / ClientOptions.Timeout), and a
            // second bound here would race with it.
            clientOptions.Transport = new HttpClientPipelineTransport(egressGuard.CreateHttpClient(Timeout.InfiniteTimeSpan));
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    private IChatClient CreateInnerChatClient(string model, OpenAIApiSurface apiSurface)
    {
        if (apiSurface is OpenAIApiSurface.ChatCompletions)
        {
            return _client.GetChatClient(model).AsIChatClient();
        }

        // Server side storage is DELIBERATELY off. When it stays on, OpenAI returns a
        // conversation id and MAF fails with: "Only ConversationId or ChatHistoryProvider
        // may be used, but not both." The Tracon persistence layer attaches a
        // ChatHistoryProvider to every agent, so the two paths cannot run together.
        // Measured: with storage on, UsePostgreSql() throws InvalidOperationException at run time.
        //
        // OPENAI001 / MAAI001: both the OpenAI library and the Microsoft Agent Framework
        // mark this path as "evaluation purposes only", which breaks the build under
        // TreatWarningsAsErrors. The suppression is a deliberate decision: the Responses
        // usage lives on these two lines only, so a single place changes if the API moves.
#pragma warning disable OPENAI001, MAAI001
        return _client.GetResponsesClient().AsIChatClientWithStoredOutputDisabled(model);
#pragma warning restore OPENAI001, MAAI001
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
