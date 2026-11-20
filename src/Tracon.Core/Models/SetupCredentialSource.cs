using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Reads the key and the address a provider was set up with from the configuration keys the host named, on every
/// call, and hands out the pair as one snapshot.
/// </summary>
/// <remarks>
/// <para>
/// A provider's key and address were read once, when the host started. A host that names the configuration keys they
/// live under (<c>ApiKeyConfigurationName</c>, <c>EndpointConfigurationName</c>) gets the current value on the next
/// call: a rotated key or a moved gateway needs no restart. The pair is read in one step, so a call never sends the
/// old key to the new address or the new key to the old one.
/// </para>
/// <para>
/// A value that is missing, blank or unusable (an address that is not absolute, a key an HTTP header cannot carry)
/// does not fail the call. The last valid value stays in use and one warning names the configuration key, never its
/// value; the next valid value is picked up without a restart. The first valid value is the one the host was set up
/// with, so a host that starts with a literal key and a name that has no value yet keeps the literal. A provider that
/// can run without a key (<c>keyless</c>) does not warn for a name that has no value while no key is in use.
/// </para>
/// <para>
/// The names are the operator's own settings, written in code or in the host's configuration. They are not fenced
/// by the key prefix a tenant binding must stay under, because no tenant writes them. The value is never stored or
/// shown: the snapshot's <see cref="object.ToString"/> redacts it.
/// </para>
/// </remarks>
internal sealed class SetupCredentialSource
{
    /// <summary>The key a provider that needs none carries, so the snapshot can still be a credential.</summary>
    internal const string KeylessPlaceholder = "no-key-required";

    // The keys a call that started before a rotation can still be holding when it fails. Older ones are not kept: a
    // call that outlives three rotations is not one the text of a failure needs to be masked for.
    private const int KeysKept = 4;

    private readonly string _providerName;
    private readonly string _optionName;
    private readonly string? _apiKeyName;
    private readonly string? _endpointName;
    private readonly IConfiguration? _configuration;
    private readonly ILogger? _logger;
    private readonly Lock _gate = new();

    private State _state;

    /// <summary>Initializes a new source.</summary>
    /// <param name="providerName">The provider name; the scope of the snapshots and the name in a warning.</param>
    /// <param name="optionName">The option the key is written under, as a refusal names it.</param>
    /// <param name="startupApiKey">The key the host was set up with in code or in its options; may be <see langword="null"/>.</param>
    /// <param name="startupEndpoint">The address the host was set up with; may be <see langword="null"/>.</param>
    /// <param name="apiKeyName">The configuration key the key is read from, or <see langword="null"/> to keep the startup key.</param>
    /// <param name="endpointName">The configuration key the address is read from, or <see langword="null"/> to keep the startup address.</param>
    /// <param name="configuration">The configuration the names are read from.</param>
    /// <param name="logger">The logger the warning is written to.</param>
    /// <param name="keyless">Whether the provider can run without a key (an OpenAI compatible server that asks for none).</param>
    internal SetupCredentialSource(
        string providerName,
        string optionName,
        string? startupApiKey,
        Uri? startupEndpoint,
        string? apiKeyName,
        string? endpointName,
        IConfiguration? configuration,
        ILogger? logger,
        bool keyless = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);

        _providerName = providerName;
        _optionName = optionName;
        _apiKeyName = string.IsNullOrWhiteSpace(apiKeyName) ? null : apiKeyName;
        _endpointName = string.IsNullOrWhiteSpace(endpointName) ? null : endpointName;
        _configuration = configuration;
        _logger = logger;
        Keyless = keyless;

        var key = UsableKey(startupApiKey);
        var endpoint = startupEndpoint is { IsAbsoluteUri: true } ? startupEndpoint.ToString() : null;

        _state = Build(rawKey: Unread, rawEndpoint: Unread, key, endpoint, [], keyBad: false, endpointBad: false);
    }

    private static string Unread { get; } = "\0unread";

    /// <summary>Gets a value indicating whether the provider can run without a key.</summary>
    internal bool Keyless { get; }

    /// <summary>
    /// Gets the keys a call can have been sent with: the current one first, then the ones it replaced.
    /// </summary>
    /// <remarks>
    /// The registry reads this when a failure is written (<c>IModelProvider.SetupApiKeys</c>), so a call that started
    /// under the old key still has its text masked.
    /// </remarks>
    internal IReadOnlyList<string> RecentKeys => Volatile.Read(ref _state).Recent;

    /// <summary>Reads the configuration keys and returns the pair a call sends.</summary>
    /// <returns>
    /// The snapshot: the key and the address, with the setup scope. A provider that knows no key carries
    /// <see cref="KeylessPlaceholder"/> in its place; the start-up checks of the provider decide whether it may start
    /// without one.
    /// </returns>
    /// <remarks>Never throws: a configuration source that fails to read keeps the last valid pair.</remarks>
    internal ModelProviderCredential Current()
    {
        string? rawKey = null;
        string? rawEndpoint = null;
        var readFailed = false;

        try
        {
            // The key is read again after the address. A reload that lands between the two reads changes the key, and the
            // pair is read once more: a call is never given the old key with the new address. Three tries bound the work
            // for a source that never settles; the last one is used.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                rawKey = _apiKeyName is null ? null : _configuration?[_apiKeyName];
                rawEndpoint = _endpointName is null ? null : _configuration?[_endpointName];

                if (_apiKeyName is null
                    || _endpointName is null
                    || string.Equals(rawKey, _configuration?[_apiKeyName], StringComparison.Ordinal))
                {
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            // No token governs this read: it is a synchronous lookup in the configuration, and a source that fails (a
            // secret store that cannot be reached) must not fail the model call. The last valid pair stays in use.
            readFailed = true;
            LogReadFailure(exception);
        }

        var seen = Volatile.Read(ref _state);

        if (readFailed
            || (string.Equals(seen.RawKey, rawKey, StringComparison.Ordinal)
                && string.Equals(seen.RawEndpoint, rawEndpoint, StringComparison.Ordinal)))
        {
            return seen.Credential;
        }

        lock (_gate)
        {
            var previous = _state;

            if (string.Equals(previous.RawKey, rawKey, StringComparison.Ordinal)
                && string.Equals(previous.RawEndpoint, rawEndpoint, StringComparison.Ordinal))
            {
                return previous.Credential;
            }

            var key = previous.Key;
            var endpoint = previous.Endpoint;
            var keyBad = previous.KeyBad;
            var endpointBad = previous.EndpointBad;

            if (_apiKeyName is not null)
            {
                if (UsableKey(rawKey) is { } usable)
                {
                    key = usable;
                    keyBad = false;
                }
                else if (!keyBad || !string.Equals(previous.RawKey, rawKey, StringComparison.Ordinal))
                {
                    keyBad = true;

                    // A server that asks for no key may name the configuration key it would be read from before any
                    // value exists. No value, with no key in use yet, is the state that server runs in, not a fault.
                    // A key that was in use and went blank, or a value that is there and unusable, is still worth saying.
                    if (!(Keyless && string.IsNullOrWhiteSpace(rawKey) && key is null))
                    {
                        LogUnusable(_apiKeyName, "key");
                    }
                }
            }

            if (_endpointName is not null)
            {
                if (UsableEndpoint(rawEndpoint) is { } usable)
                {
                    endpoint = usable;
                    endpointBad = false;
                }
                else if (!endpointBad || !string.Equals(previous.RawEndpoint, rawEndpoint, StringComparison.Ordinal))
                {
                    endpointBad = true;
                    LogUnusable(_endpointName, "address");
                }
            }

            var next = Build(rawKey, rawEndpoint, key, endpoint, Remember(previous, key), keyBad, endpointBad);

            Volatile.Write(ref _state, next);

            return next.Credential;
        }
    }

    private static string[] Remember(State previous, string? key)
    {
        if (key is null || (previous.Recent.Length > 0 && string.Equals(previous.Recent[0], key, StringComparison.Ordinal)))
        {
            return previous.Recent;
        }

        return [key, .. previous.Recent.Where(old => !string.Equals(old, key, StringComparison.Ordinal))
            .Take(KeysKept - 1)];
    }

    private State Build(string? rawKey, string? rawEndpoint, string? key, string? endpoint, string[] recent, bool keyBad, bool endpointBad)
    {
        // The first state remembers the key the host started with, so a call that began before the first rotation is
        // masked as well.
        var kept = recent.Length == 0 && key is not null ? [key] : recent;

        var credential = TenantCredentialScope.AttachSetup(
            new ModelProviderCredential { ApiKey = key ?? KeylessPlaceholder, Endpoint = endpoint },
            _providerName);

        return new State(rawKey, rawEndpoint, key, endpoint, kept, keyBad, endpointBad, credential);
    }

    private string? UsableKey(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) || OutboundApiKey.Refusal(_optionName, trimmed) is not null ? null : trimmed;
    }

    private static string? UsableEndpoint(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint) ? endpoint.ToString() : null;

    private void LogUnusable(string configurationName, string kind)
    {
        // The name of the configuration key, never its value: the value is the secret, or an address the operator
        // chose, and a warning is read by more people than the configuration is.
        _logger?.LogWarning(
            "The configuration key '{ConfigurationName}' of the model provider {Provider} has no usable value for the {Kind}; " +
            "the last valid value stays in use until it has one.",
            configurationName,
            _providerName,
            kind);
    }

    private void LogReadFailure(Exception exception)
    {
        // The exception TYPE only: the text was written by the configuration source and may hold the value it failed to read.
        _logger?.LogWarning(
            "The configuration of the model provider {Provider} could not be read ({ExceptionType}); " +
            "the last valid key and address stay in use.",
            _providerName,
            exception.GetType().FullName);
    }

    private sealed record State(
        string? RawKey,
        string? RawEndpoint,
        string? Key,
        string? Endpoint,
        string[] Recent,
        bool KeyBad,
        bool EndpointBad,
        ModelProviderCredential Credential);
}
