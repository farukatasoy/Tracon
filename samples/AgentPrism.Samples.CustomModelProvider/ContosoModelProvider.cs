using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace AgentPrism.Samples.CustomModelProvider;

/// <summary>
/// A model provider for a fictional "Contoso" service, written against the
/// published AgentPrism.Abstractions package alone.
/// </summary>
/// <remarks>
/// <para>
/// This is not a production provider — it talks to no network. It exists to
/// prove that the <see cref="IModelProvider"/> extension point is complete
/// from outside the repository: everything here comes from the NuGet package,
/// and the sample's test project runs the official
/// <c>ModelProviderContract</c> suite against it.
/// </para>
/// <para>
/// It deliberately exercises the parts of the contract a real provider has to
/// get right: it returns a <strong>raw</strong> chat client, it is safe to
/// call concurrently, it honors a per-tenant credential without ever falling
/// back to the setup-time key, it validates its own provider settings, and it
/// never rejects a model the catalog does not list.
/// </para>
/// </remarks>
public sealed class ContosoModelProvider : IModelProvider
{
    /// <summary>The provider name an agent definition binds to.</summary>
    public const string ProviderName = "contoso";

    /// <summary>The prefix this provider's <see cref="ModelBinding.ProviderSettings"/> keys carry.</summary>
    public const string SettingsPrefix = "contoso";

    /// <summary>Turns the reply into upper case. The one setting this provider reads.</summary>
    public const string ShoutSetting = "contoso.shout";

    private static readonly string[] SupportedSettingKeys = [ShoutSetting];

    private readonly string _setupTimeApiKey;
    private readonly Uri? _setupTimeEndpoint;

    // One backing client per distinct credential. A tenant credential cannot
    // reuse the setup-time client, because it carries a different key.
    //
    // ConcurrentDictionary.GetOrAdd may run its factory more than once for the
    // same key when two threads race, discarding the extra results. The
    // factory is therefore side-effect free: building a ContosoBackend twice
    // is harmless. A factory that, say, opened a connection or incremented a
    // counter here would be a bug that only shows up under load.
    private readonly ConcurrentDictionary<string, ContosoBackend> _backends = new(StringComparer.Ordinal);

    private readonly ContosoBackend _setupTimeBackend;

    /// <summary>Creates the provider.</summary>
    /// <param name="apiKey">The setup-time API key, used when no tenant credential is given.</param>
    /// <param name="endpoint">The setup-time endpoint. Optional.</param>
    /// <param name="models">
    /// The model catalog. Metadata only — a binding may name a model that is
    /// absent from it, and this provider does not reject one.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty.</exception>
    public ContosoModelProvider(string apiKey, Uri? endpoint = null, IReadOnlyList<ModelDescriptor>? models = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _setupTimeApiKey = apiKey;
        _setupTimeEndpoint = endpoint;
        _setupTimeBackend = new ContosoBackend(apiKey, endpoint);

        Models = models ??
        [
            new ModelDescriptor
            {
                Name = "contoso-large",
                DisplayName = "Contoso Large",
                ContextWindowTokens = 128_000,
                MaxOutputTokens = 4_096,
                SupportsTools = true,
                SupportsStreaming = true,
            },
        ];
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    /// <remarks>
    /// Built once in the constructor and never mutated: this is read on the
    /// compile path, which runs concurrently.
    /// </remarks>
    public IReadOnlyList<ModelDescriptor> Models { get; }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // An unknown provider setting is not ignored silently: a setting that
        // does nothing makes the host author miss the behavior they configured
        // without ever seeing why.
        ModelProviderSettings.Validate(binding, SettingsPrefix, SupportedSettingKeys);

        // The catalog is metadata, not an allow list. A model absent from it
        // is accepted, so a newly published model needs no new release.
        var shout = ModelProviderSettings.ReadBoolean(binding, ShoutSetting) ?? false;

        var backend = credential is null
            ? _setupTimeBackend
            : _backends.GetOrAdd(CacheKey(credential), _ => BuildBackend(credential));

        // A RAW client. UseFunctionInvocation(), OpenTelemetry, the content
        // guard, the circuit breaker, the fallback chain and the rest are
        // added by ModelProviderRegistry. Building any of them here would nest
        // a second tool-call loop and hide the tool-result turn from the
        // content guard.
        return new ContosoChatClient(backend, binding.Model, shout);
    }

    private static string CacheKey(ModelProviderCredential credential)
        => string.Concat(credential.ApiKey, "|", credential.Endpoint ?? string.Empty);

    /// <remarks>
    /// The endpoint falls back to the setup-time address when the credential
    /// carries none, so a globally configured base address still applies when
    /// a tenant overrides only its key. The key itself never falls back: a
    /// tenant that supplied a credential is billed on it, or the call fails.
    /// </remarks>
    private ContosoBackend BuildBackend(ModelProviderCredential credential)
    {
        var endpoint = credential.Endpoint is { Length: > 0 } value
            && Uri.TryCreate(value, UriKind.Absolute, out var parsed)
                ? parsed
                : _setupTimeEndpoint;

        return new ContosoBackend(credential.ApiKey, endpoint);
    }

    /// <summary>
    /// Stands in for a vendor SDK client: built once per distinct credential
    /// and shared by every chat client that uses it.
    /// </summary>
    /// <remarks>
    /// A real provider's SDK client owns an HTTP connection pool and is meant
    /// to be long-lived. That matters here because AgentPrism never disposes
    /// the client <see cref="CreateChatClient"/> returns — the provider owns
    /// its lifetime, and what it returns must tolerate never being disposed.
    /// </remarks>
    internal sealed class ContosoBackend(string apiKey, Uri? endpoint)
    {
        public string ApiKey { get; } = apiKey;

        public Uri? Endpoint { get; } = endpoint;
    }
}
