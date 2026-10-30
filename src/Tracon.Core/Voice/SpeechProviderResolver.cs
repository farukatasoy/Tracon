using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Resolves a speech provider by name and applies the tenant's provider checks
/// before it is returned.
/// </summary>
/// <remarks>
/// <para>
/// The order is the one <see cref="TenantProviderAccess"/> fixes and live voice
/// follows: name to instance, the tenant's egress policy against the instance's
/// provider name, then the tenant's own credential. A credential the provider
/// cannot use, a binding with an endpoint override, or a binding whose key has no
/// value refuses the call: none of them falls back to the setup credential.
/// </para>
/// <para>
/// Nothing is cached. The policy and the binding are read on every resolution, so
/// a policy saved now stops the next resolution; a store that fails fails the call
/// before the provider is reached. A caller that keeps the returned instance keeps
/// the decision: the conversation socket resolves once per connection.
/// </para>
/// </remarks>
// One gate for every speech path (the rule for text-to-speech and
// speech-to-text). The tenant comes from the run scope first: a queued workflow
// runs on a worker whose ITenantContext is the default tenant.
internal sealed class SpeechProviderResolver : ISpeechProviderResolver
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<SpeechProviderRegistration> _registrations;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantEgressPolicyStore? _egressPolicies;
    private readonly IOptions<TraconTenantProviderOptions>? _tenantProviderOptions;
    private readonly ITenantProviderBindingStore? _providerBindings;
    private readonly TenantProviderCredentialResolver? _credentialResolver;

    public SpeechProviderResolver(
        IServiceProvider services,
        IEnumerable<SpeechProviderRegistration> registrations,
        ITenantContext tenantContext,
        ITenantEgressPolicyStore? egressPolicies,
        ITenantProviderBindingStore? providerBindings,
        TenantProviderCredentialResolver? credentialResolver,
        IOptions<TraconTenantProviderOptions>? tenantProviderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _services = services;
        _registrations = [.. registrations
            .OrderBy(static registration => registration.Kind)
            .ThenBy(static registration => registration.Name, StringComparer.Ordinal)];
        _tenantContext = tenantContext;
        _egressPolicies = egressPolicies;
        _providerBindings = providerBindings;
        _credentialResolver = credentialResolver;
        _tenantProviderOptions = tenantProviderOptions;
    }

    /// <inheritdoc />
    public IReadOnlyList<SpeechProviderDescriptor> Providers
    {
        get
        {
            // Not cached: the instances are container singletons already, and a
            // factory that threw once must be tried again rather than remembered.
            var providers = new List<SpeechProviderDescriptor>(_registrations.Count + 2);

            if (_services.GetService<ISpeechSynthesizer>() is { } defaultSynthesizer)
            {
                providers.Add(Describe(null, SpeechProviderKind.Synthesizer, defaultSynthesizer.ProviderName, defaultSynthesizer.SupportsTenantCredentials, DialogueOf(defaultSynthesizer)));
            }

            foreach (var registration in _registrations.Where(static r => r.Kind == SpeechProviderKind.Synthesizer))
            {
                var synthesizer = _services.GetRequiredKeyedService<ISpeechSynthesizer>(registration);

                providers.Add(Describe(registration.Name, registration.Kind, synthesizer.ProviderName, synthesizer.SupportsTenantCredentials, DialogueOf(synthesizer)));
            }

            if (_services.GetService<ISpeechTranscriber>() is { } defaultTranscriber)
            {
                providers.Add(Describe(null, SpeechProviderKind.Transcriber, defaultTranscriber.ProviderName, defaultTranscriber.SupportsTenantCredentials));
            }

            foreach (var registration in _registrations.Where(static r => r.Kind == SpeechProviderKind.Transcriber))
            {
                var transcriber = _services.GetRequiredKeyedService<ISpeechTranscriber>(registration);

                providers.Add(Describe(registration.Name, registration.Kind, transcriber.ProviderName, transcriber.SupportsTenantCredentials));
            }

            return providers;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ISpeechSynthesizer> GetSynthesizerAsync(
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var synthesizer = Find<ISpeechSynthesizer>(SpeechProviderKind.Synthesizer, name);

        var credential = await AuthorizeAsync(
                SpeechProviderRegistration.Noun(SpeechProviderKind.Synthesizer),
                synthesizer.ProviderName,
                synthesizer.SupportsTenantCredentials,
                cancellationToken)
            .ConfigureAwait(false);

        // A store that does not observe the token returns normally after the caller
        // cancelled; the provider is not handed out then, or the caller calls it.
        cancellationToken.ThrowIfCancellationRequested();

        return credential is null ? synthesizer : new TenantCredentialSpeechSynthesizer(synthesizer, credential);
    }

    /// <inheritdoc />
    public async ValueTask<ISpeechTranscriber> GetTranscriberAsync(
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transcriber = Find<ISpeechTranscriber>(SpeechProviderKind.Transcriber, name);

        var credential = await AuthorizeAsync(
                SpeechProviderRegistration.Noun(SpeechProviderKind.Transcriber),
                transcriber.ProviderName,
                transcriber.SupportsTenantCredentials,
                cancellationToken)
            .ConfigureAwait(false);

        // A store that does not observe the token returns normally after the caller
        // cancelled; the provider is not handed out then, or the caller calls it.
        cancellationToken.ThrowIfCancellationRequested();

        return credential is null ? transcriber : new TenantCredentialSpeechTranscriber(transcriber, credential);
    }

    /// <inheritdoc />
    public async ValueTask<ISpeechDialogueSynthesizer> GetDialogueSynthesizerAsync(
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var synthesizer = Find<ISpeechSynthesizer>(SpeechProviderKind.Synthesizer, name);

        // The capability is read from the registered instance, before any tenant
        // wrapper exists: a wrapper implements only what it wraps, so a type test on
        // it would answer differently for a tenant with its own key (the handover).
        if (synthesizer is not ISpeechDialogueSynthesizer dialogue)
        {
            throw new SpeechCapabilityNotSupportedException(
                $"The speech synthesizer {Describe(name)} (provider '{synthesizer.ProviderName}') cannot voice a " +
                $"dialogue: it does not implement {nameof(ISpeechDialogueSynthesizer)}. Name a synthesizer that does, " +
                "or voice the turns one by one with SynthesizeAsync.");
        }

        var credential = await AuthorizeAsync(
                DialogueNoun,
                dialogue.ProviderName,
                dialogue.SupportsTenantCredentials,
                cancellationToken)
            .ConfigureAwait(false);

        // A store that does not observe the token returns normally after the caller
        // cancelled; the provider is not handed out then, or the caller calls it.
        cancellationToken.ThrowIfCancellationRequested();

        return credential is null ? dialogue : new TenantCredentialSpeechDialogueSynthesizer(dialogue, credential);
    }

    /// <summary>The noun the tenant checks' messages use for a dialogue synthesizer.</summary>
    private const string DialogueNoun = "dialogue synthesizer";

    /// <summary>Names a registration in a message: its name, or "default" for the unnamed one.</summary>
    private static string Describe(string? name)
        => name is not null && SpeechProviderNames.TryNormalize(name, out var normalized) ? $"'{normalized}'" : "default";

    private static SpeechProviderDescriptor Describe(
        string? name,
        SpeechProviderKind kind,
        string providerName,
        bool supportsTenantCredentials,
        SpeechDialogueCapabilities? dialogue = null)
        => new()
        {
            Name = name,
            Kind = kind,
            ProviderName = providerName,
            SupportsTenantCredentials = supportsTenantCredentials,
            Dialogue = dialogue,
        };

    /// <summary>Reads the dialogue limits from the registered instance, as <see cref="GetDialogueSynthesizerAsync"/> does.</summary>
    private static SpeechDialogueCapabilities? DialogueOf(ISpeechSynthesizer synthesizer)
        => (synthesizer as ISpeechDialogueSynthesizer)?.DialogueCapabilities;

    private T Find<T>(SpeechProviderKind kind, string? name)
        where T : class
    {
        var noun = SpeechProviderRegistration.Noun(kind);

        if (name is null)
        {
            return _services.GetService<T>()
                ?? throw new SpeechProviderNotFoundException(
                    $"No default speech {noun} is registered. Call UseVoice(...) or register your own, " +
                    $"or name one of the registered {noun}s: {RegisteredNames(kind)}.");
        }

        if (!SpeechProviderNames.TryNormalize(name, out var normalized))
        {
            throw new SpeechProviderNotFoundException(SpeechProviderNames.InvalidNameMessage);
        }

        var registration = new SpeechProviderRegistration(kind, normalized);

        if (!_registrations.Contains(registration))
        {
            throw new SpeechProviderNotFoundException(
                $"No speech {noun} is registered under the name '{normalized}'. Registered {noun}s: {RegisteredNames(kind)}.");
        }

        return _services.GetRequiredKeyedService<T>(registration);
    }

    private string RegisteredNames(SpeechProviderKind kind)
    {
        var names = _registrations
            .Where(registration => registration.Kind == kind)
            .Select(static registration => registration.Name)
            .ToList();

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>Applies the tenant's egress policy and resolves its own credential.</summary>
    /// <returns>The tenant's credential, or <see langword="null"/> for the provider's configured one.</returns>
    /// <exception cref="TraconTenantProviderRefusedException">The tenant's checks refuse the provider.</exception>
    private async ValueTask<ModelProviderCredential?> AuthorizeAsync(
        string noun,
        string providerName,
        bool supportsTenantCredentials,
        CancellationToken cancellationToken)
    {
        var tenantId = TraconRunContext.Current?.TenantId ?? _tenantContext.TenantId;

        var requirePolicy = TenantProviderAccess.PolicyRequired(_tenantProviderOptions);

        if (_egressPolicies is not null || requirePolicy)
        {
            var policy = _egressPolicies is null
                ? null
                : await _egressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

            if (!TenantProviderAccess.IsAllowed(policy, providerName, requirePolicy))
            {
                throw new TraconTenantProviderRefusedException(
                    TenantProviderAccess.DescribeDenial(policy, tenantId, providerName, TenantProviderCallKind.Speech));
            }
        }

        // A binding whose key has no value is refused there: the call does not fall
        // back to the setup key either.
        var credential = await TenantProviderAccess
            .ResolveCredentialAsync(_providerBindings, _credentialResolver, tenantId, providerName, cancellationToken)
            .ConfigureAwait(false);

        if (credential is null)
        {
            return null;
        }

        if (!supportsTenantCredentials)
        {
            throw new TraconTenantProviderRefusedException(
                $"Tenant '{tenantId}' has its own credential for '{providerName}', and the speech {noun} " +
                "cannot use it. The call is not made on the setup credential.");
        }

        // A binding's endpoint override names a model API base. A speech provider
        // calls the address it was built with, so the override would be ignored and
        // the tenant's key sent to an address the tenant did not choose.
        if (credential.Endpoint is { Length: > 0 })
        {
            throw new TraconTenantProviderRefusedException(
                $"Tenant '{tenantId}' has a '{providerName}' binding with an endpoint override. A speech {noun} " +
                "calls its configured address and cannot honour that override, so the call is not made.");
        }

        return credential;
    }
}

/// <summary>
/// No speech provider is registered under the requested name, or no default one is.
/// </summary>
/// <remarks>
/// A separate type so an HTTP endpoint can answer <c>400</c> for an unknown name and
/// <c>501</c> for a missing default, and keep <c>502</c> for a provider failure.
/// </remarks>
internal sealed class SpeechProviderNotFoundException : TraconException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was not found.</param>
    public SpeechProviderNotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The speech provider registered under the requested name has no such capability: a
/// synthesizer that cannot voice a dialogue.
/// </summary>
/// <remarks>
/// A configuration fact, not a tenant refusal and not a provider failure: no store is
/// read and the provider is not called. Separate from
/// <see cref="SpeechProviderNotFoundException"/> so an HTTP endpoint can tell "no such
/// name" from "this provider cannot do it".
/// </remarks>
internal sealed class SpeechCapabilityNotSupportedException : TraconException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which provider lacks which capability.</param>
    public SpeechCapabilityNotSupportedException(string message)
        : base(message)
    {
    }
}

/// <summary>A dialogue synthesizer that puts one tenant's credential on every request.</summary>
/// <remarks>
/// Returned only when the tenant has its own credential. A credential sent by the caller
/// is replaced, never kept: the resolver chose the tenant.
/// </remarks>
internal sealed class TenantCredentialSpeechDialogueSynthesizer : ISpeechDialogueSynthesizer
{
    private readonly ISpeechDialogueSynthesizer _inner;
    private readonly ModelProviderCredential _credential;

    public TenantCredentialSpeechDialogueSynthesizer(ISpeechDialogueSynthesizer inner, ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(credential);

        _inner = inner;
        _credential = credential;
    }

    /// <inheritdoc />
    public string ProviderName => _inner.ProviderName;

    /// <inheritdoc />
    public bool SupportsTenantCredentials => true;

    /// <inheritdoc />
    public SpeechDialogueCapabilities DialogueCapabilities => _inner.DialogueCapabilities;

    /// <inheritdoc />
    public ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _inner.SynthesizeDialogueAsync(request with { Credential = _credential }, cancellationToken);
    }
}

/// <summary>A synthesizer that puts one tenant's credential on every request.</summary>
/// <remarks>
/// A credential sent by the caller is replaced, never kept: the resolver chose the
/// tenant, and a caller must not choose another tenant's key.
/// </remarks>
internal sealed class TenantCredentialSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly ISpeechSynthesizer _inner;
    private readonly ModelProviderCredential _credential;

    public TenantCredentialSpeechSynthesizer(ISpeechSynthesizer inner, ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(credential);

        _inner = inner;
        _credential = credential;
    }

    /// <inheritdoc />
    public string ProviderName => _inner.ProviderName;

    /// <inheritdoc />
    public bool SupportsTenantCredentials => true;

    /// <inheritdoc />
    public int MaxCharactersPerRequest => _inner.MaxCharactersPerRequest;

    /// <inheritdoc />
    public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _inner.SynthesizeAsync(request with { Credential = _credential }, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _inner.SynthesizeStreamingAsync(request with { Credential = _credential }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        => _inner.ListVoicesWithCredentialAsync(_credential, cancellationToken);

    /// <inheritdoc />
    /// <remarks>The tenant's credential is used whatever <paramref name="credential"/> holds.</remarks>
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesWithCredentialAsync(
        ModelProviderCredential? credential,
        CancellationToken cancellationToken = default)
        => _inner.ListVoicesWithCredentialAsync(_credential, cancellationToken);
}

/// <summary>A transcriber that puts one tenant's credential on every request.</summary>
internal sealed class TenantCredentialSpeechTranscriber : ISpeechTranscriber
{
    private readonly ISpeechTranscriber _inner;
    private readonly ModelProviderCredential _credential;

    public TenantCredentialSpeechTranscriber(ISpeechTranscriber inner, ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(credential);

        _inner = inner;
        _credential = credential;
    }

    /// <inheritdoc />
    public string ProviderName => _inner.ProviderName;

    /// <inheritdoc />
    public bool SupportsTenantCredentials => true;

    /// <inheritdoc />
    public ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.TranscribeAsync(
            audio,
            mediaType,
            (options ?? new SpeechTranscriptionOptions()) with { Credential = _credential },
            cancellationToken);
}
