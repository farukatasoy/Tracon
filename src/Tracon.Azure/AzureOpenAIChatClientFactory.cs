using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Tracon;

/// <summary>
/// Builds an <see cref="OpenAIClient"/> that talks to an Azure OpenAI resource's
/// <c>/openai/v1/</c> endpoint and produces <see cref="IChatClient"/> instances
/// from model bindings.
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
/// <see cref="OpenAIClient"/> is built once and shared; it manages its own
/// HTTP connection pool. Building a new client on every call fragments the pool.
/// </para>
/// <para>
/// <strong>What Azure calls is a deployment name, not a model name.</strong>
/// The <see cref="ModelBinding.Model"/> field carries the deployment name for
/// this provider and goes into the <c>model</c> field of the request body:
/// <c>POST {endpoint}/openai/v1/chat/completions</c>.
/// The same model may be deployed under one name (e.g. <c>prod-gpt</c>) on one
/// resource and another (e.g. <c>gpt-mini-tr</c>) on another; the person who sets
/// up the resource chooses the name.
/// </para>
/// <para>
/// The provider supports <strong>one key</strong> inside
/// <see cref="ModelBinding.ProviderSettings"/>:
/// <see cref="AzureOpenAIProviderNames.StrictJsonSchemaSetting"/>. The Azure-only request fields
/// (data sources and the like) have no counterpart on the plain OpenAI client
/// this provider uses. When any other key arrives, the build stops with
/// an explicit error.
/// </para>
/// </remarks>
internal sealed class AzureOpenAIChatClientFactory
{
    private readonly OpenAIClient _client;
    private readonly Uri? _endpoint;
    private readonly string? _defaultDeployment;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Builds a new factory from options.</summary>
    /// <param name="options">The provider options.</param>
    /// <param name="loggerFactory">The logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <see cref="AzureOpenAIProviderOptions.Endpoint"/> is empty, or neither an API key
    /// nor a credential factory is given.
    /// </exception>
    public AzureOpenAIChatClientFactory(AzureOpenAIProviderOptions options, ILoggerFactory? loggerFactory = null)
        : this(CreateClient(options), options.DefaultDeployment, loggerFactory)
    {
        // CreateClient has already refused an empty endpoint.
        _endpoint = V1Endpoint(options.Endpoint!);
    }

    /// <summary>Builds a new factory from a ready-made client.</summary>
    /// <param name="client">The client, already pointed at the resource's v1 endpoint.</param>
    /// <param name="defaultDeployment">The name to use when no deployment name is given.</param>
    /// <param name="loggerFactory">The logger factory passed to produced clients.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="FromClient"/> calls this constructor.
    /// </remarks>
    private AzureOpenAIChatClientFactory(
        OpenAIClient client,
        string? defaultDeployment,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _defaultDeployment = Trim(defaultDeployment);
        _loggerFactory = loggerFactory;
    }

    /// <summary>Builds a new factory from a ready-made client.</summary>
    /// <param name="client">The client, already pointed at the resource's v1 endpoint.</param>
    /// <param name="defaultDeployment">The name to use when no deployment name is given.</param>
    /// <param name="loggerFactory">The logger factory passed to produced clients.</param>
    /// <returns>The new factory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The model provider builds a tenant's own-credential factory this way: it
    /// creates the client for that credential, then wraps it here.
    /// </remarks>
    public static AzureOpenAIChatClientFactory FromClient(
        OpenAIClient client,
        string? defaultDeployment = null,
        ILoggerFactory? loggerFactory = null)
        => new(client, defaultDeployment, loggerFactory);

    /// <summary>Produces a chat client for the given binding.</summary>
    /// <param name="binding">The model binding. <see cref="ModelBinding.Model"/> is the deployment name.</param>
    /// <returns>A chat client passed through the pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The deployment name cannot be resolved, or <see cref="ModelBinding.ProviderSettings"/>
    /// contains a key that is not in <see cref="AzureOpenAIProviderNames.SupportedSettings"/>, or a
    /// supported key with a value of the wrong type.
    /// </exception>
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var deployment = Trim(binding.Model) ?? _defaultDeployment
            ?? throw new ProviderSettingsValidationException(
                "The deployment name is empty and no default deployment is defined. In Azure OpenAI, " +
                $"the {nameof(ModelBinding)}.{nameof(ModelBinding.Model)} field is not a MODEL name — " +
                "it expects a DEPLOYMENT name defined in your Azure resource. Fill in the field or " +
                $"set the '{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.DefaultDeployment)}' " +
                "option.");

        // Every key that is not in the supported list is rejected here and the
        // error is wrapped by AgentDefinitionCompiler into
        // TraconCompilationException. Silently ignoring it would leave the
        // user without the behavior they expected.
        ModelProviderSettings.Validate(
            binding,
            AzureOpenAIProviderNames.SettingsPrefix,
            AzureOpenAIProviderNames.SupportedSettings);

        var client = _client.GetChatClient(deployment).AsIChatClient();

        return ModelProviderSettings.ReadBoolean(binding, AzureOpenAIProviderNames.StrictJsonSchemaSetting) is true
            ? new OpenAIStrictResponseSchemaChatClient(client)
            : client;
    }

    /// <summary>Builds a client for an Azure OpenAI resource's v1 endpoint from options.</summary>
    /// <param name="options">The provider options.</param>
    /// <param name="egressGuard">
    /// When given, every connection this client opens passes through the
    /// outbound network guard. Supplied only for a tenant-supplied endpoint
    /// override; a setup-time endpoint is the operator's own decision and is
    /// written in code.
    /// </param>
    /// <param name="transport">
    /// Replaces the HTTP transport. Tests only: the Entra path refuses a
    /// non-HTTPS address, so a loopback server cannot carry it. Wins over
    /// <paramref name="egressGuard"/>.
    /// </param>
    /// <returns>The built client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <see cref="AzureOpenAIProviderOptions.Endpoint"/> is empty, or neither an API key
    /// nor a credential factory is given.
    /// </exception>
    /// <remarks>
    /// Azure's v1 API is the OpenAI wire protocol under
    /// <c>{endpoint}/openai/v1/</c>, so the plain <see cref="OpenAIClient"/> is
    /// enough: no <c>api-version</c> query string, and the deployment name is
    /// the request's <c>model</c> field. An API key travels as
    /// <c>Authorization: Bearer</c>; an Entra credential is wrapped in a
    /// <see cref="BearerTokenPolicy"/> for the <see cref="AzureOpenAIProviderOptions.Audience"/> scope.
    /// </remarks>
    public static OpenAIClient CreateClient(
        AzureOpenAIProviderOptions options,
        EgressSocketGuard? egressGuard = null,
        PipelineTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Endpoint is null)
        {
            throw new TraconException(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} is empty. " +
                "Azure OpenAI has no single global address; give your resource's address " +
                "in the `UseAzureOpenAI(endpoint, ...)` call or in the " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.Endpoint)}' " +
                "option.");
        }

        var clientOptions = new OpenAIClientOptions { Endpoint = V1Endpoint(options.Endpoint) };

        // 🚨 Same family as OpenAIClientOptions: the hook is
        // ClientPipelineOptions.Transport, not an HttpClient property.
        if (egressGuard is not null)
        {
            // Infinite on purpose: these SDKs apply their own per-request
            // network timeout (NetworkTimeout / ClientOptions.Timeout), and a
            // second bound here would race with it.
            clientOptions.Transport = new HttpClientPipelineTransport(egressGuard.CreateHttpClient(Timeout.InfiniteTimeSpan));
        }

        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }

        if (options.Timeout is { } timeout)
        {
            clientOptions.NetworkTimeout = timeout;
        }

        // The credential factory OVERRIDES the key: when both are given, the
        // managed credential is the safer choice, and silently falling back to
        // the key would be wrong.
        if (options.CredentialFactory is { } credentialFactory)
        {
            var credential = credentialFactory()
                ?? throw new TraconException(
                    $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)} " +
                    "returned null. The factory must always produce a credential.");

            var scope = string.IsNullOrWhiteSpace(options.Audience)
                ? AzureOpenAIProviderHealthCheck.DefaultAudience
                : options.Audience;

            // 🚨 OPENAI001: the OpenAI library marks this constructor experimental.
            // It is the only way to give an OpenAI client an Entra credential, and
            // Microsoft's own Azure OpenAI v1 guidance uses it. The suppression is
            // confined to this one call (the key path below uses the stable
            // constructor). If an OpenAI upgrade renames or removes it, the build
            // breaks right here; a consumer who floats OpenAI above the tested
            // version still sees the failure only at run time, on this path.
#pragma warning disable OPENAI001
            return new OpenAIClient(new BearerTokenPolicy(credential, scope), clientOptions);
#pragma warning restore OPENAI001
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new TraconException(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.ApiKey)} is empty and " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} is not defined. " +
                "Give the key in the `UseAzureOpenAI(endpoint, apiKey)` call, " +
                $"define the '{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.ApiKey)}' " +
                "option, or give a " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} for a managed credential.");
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    /// <summary>Points a resource address at its v1 data-plane endpoint.</summary>
    /// <param name="resourceEndpoint">The resource address, with or without the <c>openai/v1</c> suffix.</param>
    /// <returns>The address ending in <c>/openai/v1/</c>; the suffix is never added twice.</returns>
    /// <remarks>
    /// <c>internal</c>: unit tests verify the combination without a network call.
    /// A path prefix in front of the resource (a gateway) is kept.
    /// </remarks>
    internal static Uri V1Endpoint(Uri resourceEndpoint)
    {
        const string V1Suffix = "openai/v1/";

        // The path only: a query string or fragment on the address must not end up
        // in front of the suffix.
        var text = resourceEndpoint.GetLeftPart(UriPartial.Path);

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return text.EndsWith("/" + V1Suffix, StringComparison.OrdinalIgnoreCase)
            ? new Uri(text, UriKind.Absolute)
            : new Uri(text + V1Suffix, UriKind.Absolute);
    }

    /// <summary>Creates an image generator that shares this factory's Azure OpenAI client.</summary>
    /// <param name="deploymentName">The Azure image deployment name.</param>
    /// <returns>The native Microsoft.Extensions.AI image generator.</returns>
#pragma warning disable MEAI001
    internal IImageGenerator CreateImageGenerator(string deploymentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        // Not GetImageClient(deploymentName).AsIImageGenerator(): a host that constructs an
        // ImageClient cannot be published with Native AOT (see OpenAIPipelineImageGenerator).
        return new OpenAIPipelineImageGenerator(
            _client,
            _endpoint ?? throw new TraconException(
                "Azure OpenAI images need the setup-time client, whose address is known. " +
                "A client built from a tenant's own credential carries no image registration."),
            deploymentName,
            AzureOpenAIProviderNames.AzureOpenAI);
    }
#pragma warning restore MEAI001

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
