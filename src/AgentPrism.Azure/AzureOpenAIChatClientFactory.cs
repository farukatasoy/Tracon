using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Builds an <see cref="AzureOpenAIClient"/> from options and produces <see
/// cref="IChatClient"/> instances from model bindings.
/// </summary>
/// <remarks>
/// <para>
/// The factory returns a <strong>RAW</strong> client. The common pipeline
/// (<c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, content guard,
/// circuit breaker, extra resolution) is set up inside
/// <c>ModelProviderRegistry.CreateChatClient</c>.
/// When the loop was set up here, none of the wrapping layers could
/// see the tool-call turns.
/// </para>
/// <para>
/// <see cref="AzureOpenAIClient"/> is built once and shared; it manages its own
/// HTTP connection pool. Building a new client on every call fragments the pool.
/// </para>
/// <para>
/// <strong>What Azure calls is a deployment name, not a model name.</strong>
/// The <see cref="ModelBinding.Model"/> field carries the deployment name for
/// this provider and goes into the request path:
/// <c>POST {endpoint}/openai/deployments/{deployment}/chat/completions</c>.
/// The same model may be deployed under one name (e.g. <c>prod-gpt</c>) on one
/// resource and another (e.g. <c>gpt-mini-tr</c>) on another; the person who sets
/// up the resource chooses the name.
/// </para>
/// <para>
/// The provider supports <strong>no keys at all</strong> inside
/// <see cref="ModelBinding.ProviderSettings"/>. Custom fields Azure lets you add
/// to a chat request are written through <c>AzureChatExtensions</c>, and those
/// extensions break at run time against whichever OpenAI SDK version we use.
/// When a defined key arrives, the build stops with an
/// explicit error.
/// </para>
/// </remarks>
public sealed class AzureOpenAIChatClientFactory
{
    private readonly AzureOpenAIClient _client;
    private readonly string? _defaultDeployment;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Builds a new factory from options.</summary>
    /// <param name="options">The provider options.</param>
    /// <param name="loggerFactory">The logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// <see cref="AzureOpenAIProviderOptions.Endpoint"/> is empty, or neither an API key
    /// nor a credential factory is given.
    /// </exception>
    public AzureOpenAIChatClientFactory(AzureOpenAIProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultDeployment, loggerFactory)
    {
    }

    /// <summary>Builds a new factory from a ready-made client.</summary>
    /// <param name="client">The Azure OpenAI client to use.</param>
    /// <param name="defaultDeployment">The name to use when no deployment name is given.</param>
    /// <param name="loggerFactory">The logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Setups that build the client themselves use <see cref="FromClient"/>, which
    /// calls this constructor: it is the only escape hatch when a custom
    /// <c>AzureOpenAIClientOptions</c>, a sovereign cloud, or a hand-managed HTTP
    /// pipeline is needed.
    /// </remarks>
    private AzureOpenAIChatClientFactory(
        AzureOpenAIClient client,
        string? defaultDeployment,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _defaultDeployment = Trim(defaultDeployment);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Builds a new factory from a ready-made client.</summary>
    /// <param name="client">The Azure OpenAI client to use.</param>
    /// <param name="defaultDeployment">The name to use when no deployment name is given.</param>
    /// <param name="loggerFactory">The logger factory passed to produced clients.</param>
    /// <returns>The new factory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Setups that build the client themselves use this factory method: it is the
    /// only escape hatch when a custom <c>AzureOpenAIClientOptions</c>, a
    /// sovereign cloud, or a hand-managed HTTP pipeline is needed.
    /// </remarks>
    public static AzureOpenAIChatClientFactory FromClient(
        AzureOpenAIClient client,
        string? defaultDeployment = null,
        ILoggerFactory? loggerFactory = null)
        => new(client, defaultDeployment, loggerFactory);

    /// <summary>Produces a chat client for the given binding.</summary>
    /// <param name="binding">The model binding. <see cref="ModelBinding.Model"/> is the deployment name.</param>
    /// <returns>A chat client passed through the pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// The deployment name cannot be resolved, or <see cref="ModelBinding.ProviderSettings"/>
    /// contains any key.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var deployment = Trim(binding.Model) ?? _defaultDeployment
            ?? throw new AgentPrismException(
                "The deployment name is empty and no default deployment is defined. In Azure OpenAI, " +
                $"the {nameof(ModelBinding)}.{nameof(ModelBinding.Model)} field is not a MODEL name — " +
                "it expects a DEPLOYMENT name defined in your Azure resource. Fill in the field or " +
                $"set the '{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.DefaultDeployment)}' " +
                "option.");

        // This provider supports no extra settings; every incoming key is
        // rejected here and the error is wrapped by AgentDefinitionCompiler into
        // AgentPrismCompilationException. Silently ignoring it would leave the
        // user without the behavior they expected (K-208).
        ModelProviderSettings.Validate(
            binding,
            AzureOpenAIProviderNames.SettingsPrefix,
            AzureOpenAIProviderNames.SupportedSettings);

        return _client.GetChatClient(deployment).AsIChatClient();
    }

    /// <summary>Builds an Azure OpenAI client from options.</summary>
    /// <param name="options">The provider options.</param>
    /// <param name="egressGuard">
    /// When given, every connection this client opens passes through the
    /// outbound network guard. Supplied only for a tenant-supplied endpoint
    /// override; a setup-time endpoint is the operator's own decision and is
    /// written in code.
    /// </param>
    /// <returns>The built client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// <see cref="AzureOpenAIProviderOptions.Endpoint"/> is empty, or neither an API key
    /// nor a credential factory is given.
    /// </exception>
    public static AzureOpenAIClient CreateClient(AzureOpenAIProviderOptions options, EgressSocketGuard? egressGuard = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Endpoint is null)
        {
            throw new AgentPrismException(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} is empty. " +
                "Azure OpenAI has no single global address; give your resource's address " +
                "in the `UseAzureOpenAI(endpoint, ...)` call or in the " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.Endpoint)}' " +
                "option.");
        }

        var clientOptions = new AzureOpenAIClientOptions();

        // 🚨 Same family as OpenAIClientOptions: the hook is
        // ClientPipelineOptions.Transport, not an HttpClient property.
        if (egressGuard is not null)
        {
            // Infinite on purpose: these SDKs apply their own per-request
            // network timeout (NetworkTimeout / ClientOptions.Timeout), and a
            // second bound here would race with it.
            clientOptions.Transport = new HttpClientPipelineTransport(egressGuard.CreateHttpClient(Timeout.InfiniteTimeSpan));
        }

        if (options.Timeout is { } timeout)
        {
            clientOptions.NetworkTimeout = timeout;
        }

        if (!string.IsNullOrWhiteSpace(options.Audience))
        {
            clientOptions.Audience = new AzureOpenAIAudience(options.Audience);
        }

        // The credential factory OVERRIDES the key: when both are given, the
        // managed credential is the safer choice, and silently falling back to
        // the key would be wrong.
        if (options.CredentialFactory is { } credentialFactory)
        {
            var credential = credentialFactory()
                ?? throw new AgentPrismException(
                    $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)} " +
                    "returned null. The factory must always produce a credential.");

            return new AzureOpenAIClient(options.Endpoint, credential, clientOptions);
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AgentPrismException(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.ApiKey)} is empty and " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} is not defined. " +
                "Give the key in the `UseAzureOpenAI(endpoint, apiKey)` call, " +
                $"define the '{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.ApiKey)}' " +
                "option, or give a " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} for a managed credential.");
        }

        return new AzureOpenAIClient(options.Endpoint, new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
