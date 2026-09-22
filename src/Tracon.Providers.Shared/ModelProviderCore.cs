using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

// Record: K-646 (the tenant chat client cache fixed four times), K-531
// (tenant endpoint vs setup-time endpoint), K-059 (keys never in a diagnostic
// value), phase 181 (why this body left the four packages).

/// <summary>
/// The per-instance state every provider's <see cref="IModelProvider"/> shares:
/// the known-model set, the per-tenant (BYOK) factory and chat client caches,
/// and the tenant endpoint guard rule.
/// </summary>
/// <typeparam name="TFactory">The provider's chat client factory type.</typeparam>
/// <remarks>
/// <para>
/// Composition, not a base class: each provider keeps its own type, its own
/// constructor and its own <c>BuildCredentialFactory</c> (the options each SDK
/// needs differ). A cache fix once had to be written four times because this
/// body was copied four times; here it is written once.
/// </para>
/// <para>
/// The stateless rules live in the non-generic <see cref="ModelProviderCore"/>.
/// Shared source: see <c>src/Tracon.Providers.Shared/README.md</c>.
/// </para>
/// </remarks>
internal sealed class ModelProviderCore<TFactory>
    where TFactory : class
{
    private readonly HashSet<string> _knownModels;
    private readonly EgressSocketGuard? _egressGuard;

    // Phase 65 (BYOK). Measured: each SDK client is built once at setup and
    // shared, so a tenant credential cannot reuse it - a second client is built
    // and cached per distinct credential (the same pattern
    // OpenAINamedChatClientFactoryCache uses for named OpenAI-compatible
    // providers).
    private readonly ProviderCredentialClientCache<TFactory> _credentialFactories = new();

    // The chat client produced for a tenant credential must stay stable across
    // calls, the same as the setup-time client (see TenantChatClientCacheKey).
    private readonly ConcurrentDictionary<string, IChatClient> _tenantChatClients = new(StringComparer.Ordinal);

    /// <summary>Creates the shared state for one provider instance.</summary>
    /// <param name="models">The provider's catalog.</param>
    /// <param name="egressGuard">The outbound network guard for tenant-supplied endpoints, or <see langword="null"/>.</param>
    internal ModelProviderCore(IReadOnlyList<ModelDescriptor> models, EgressSocketGuard? egressGuard)
    {
        ArgumentNullException.ThrowIfNull(models);

        _knownModels = new HashSet<string>(models.Select(static model => model.Name), StringComparer.OrdinalIgnoreCase);
        _egressGuard = egressGuard;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the catalog is non-empty and does not
    /// list <paramref name="model"/>.
    /// </summary>
    /// <param name="model">The model (Azure: deployment) name of the binding.</param>
    /// <returns>Whether the caller should write its out-of-catalog log entry.</returns>
    /// <remarks>
    /// A model outside the catalog is <strong>not</strong> rejected — a provider
    /// ships new models without a Tracon release — it is only logged. An empty
    /// catalog is the normal case (Tracon carries no built-in model list);
    /// there is nothing to compare against, so it never returns <see langword="true"/>.
    /// </remarks>
    internal bool IsOutsideCatalog([NotNullWhen(true)] string? model)
        => _knownModels.Count > 0
            && !string.IsNullOrWhiteSpace(model)
            && !_knownModels.Contains(model);

    /// <summary>
    /// Returns the stable chat client for a tenant credential (BYOK): one factory
    /// per credential, one client per credential, model and settings.
    /// </summary>
    /// <param name="credential">The tenant's resolved credential.</param>
    /// <param name="binding">The model binding.</param>
    /// <param name="buildFactory">Builds the provider factory for a credential not seen before.</param>
    /// <param name="createChatClient">Creates the chat client from the factory.</param>
    /// <returns>The same client instance for the same credential and binding.</returns>
    internal IChatClient GetTenantChatClient(
        ModelProviderCredential credential,
        ModelBinding binding,
        Func<ModelProviderCredential, TFactory> buildFactory,
        Func<TFactory, ModelBinding, IChatClient> createChatClient)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(buildFactory);
        ArgumentNullException.ThrowIfNull(createChatClient);

        var factory = _credentialFactories.GetOrAdd(credential, buildFactory);
        var cacheKey = TenantChatClientCacheKey.For(credential, binding);

        return _tenantChatClients.GetOrAdd(cacheKey, _ => createChatClient(factory, binding));
    }

    /// <summary>
    /// Returns the guard to attach to a per-tenant client, or
    /// <see langword="null"/> when none is needed.
    /// </summary>
    /// <param name="tenantSuppliedEndpoint">The value of <see cref="ModelProviderCore.TenantEndpoint"/>.</param>
    /// <returns>The guard, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The guard is attached <strong>only</strong> when the endpoint came from the
    /// tenant's binding. A setup-time endpoint is the operator's own decision and
    /// is written in code — guarding it would break sovereign cloud and
    /// internal-proxy setups that are deliberately private. A tenant-supplied
    /// override is outside input and is guarded.
    /// </remarks>
    internal EgressSocketGuard? GuardFor(Uri? tenantSuppliedEndpoint)
        => tenantSuppliedEndpoint is null ? null : _egressGuard;
}

/// <summary>
/// The stateless rules every provider's <see cref="IModelProvider"/> applies the
/// same way.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ModelProviderCore{TFactory}"/> so a caller writes
/// <c>ModelProviderCore.TenantEndpoint(...)</c> and does not name its factory type
/// for a static call.
/// </remarks>
internal static class ModelProviderCore
{
    /// <summary>
    /// Returns the endpoint the <strong>tenant</strong> supplied, or
    /// <see langword="null"/> when the credential carries none or an unusable one.
    /// </summary>
    /// <param name="credential">The tenant's resolved credential.</param>
    /// <returns>The tenant-supplied absolute endpoint, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Kept apart from the setup-time endpoint on purpose: the caller
    /// falls back to its setup-time endpoint for the client, but passes only
    /// this value to <see cref="ModelProviderCore{TFactory}.GuardFor"/>.
    /// </remarks>
    internal static Uri? TenantEndpoint(ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.Endpoint is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                ? endpointUri
                : null;
    }

    /// <summary>Returns the health result of a provider set up without health check options.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <returns>An <see cref="ModelProviderHealthStatus.Unknown"/> result.</returns>
    internal static ModelProviderHealth UnknownHealth(string providerName)
        => new()
        {
            ProviderName = providerName,
            Status = ModelProviderHealthStatus.Unknown,
            CheckedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Builds the diagnostic for the provider's API key setting. It names the
    /// configuration <strong>key</strong>, never its value.
    /// </summary>
    /// <param name="sectionKey">The fixed configuration section of the provider.</param>
    /// <param name="resolved">Whether a usable credential is configured.</param>
    /// <param name="alternative">A second way to satisfy the setting (Azure: a credential factory), or <see langword="null"/>.</param>
    /// <returns>The diagnostic.</returns>
    internal static ConfigurationDiagnostic ConfigurationDiagnosticFor(
        string sectionKey,
        bool resolved,
        string? alternative = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

        var key = $"{sectionKey}:ApiKey";
        var command = $"dotnet user-secrets set \"{key}\" \"<key>\"";

        return new ConfigurationDiagnostic
        {
            Key = key,
            Resolved = resolved,
            Hint = resolved ? null : alternative is null ? command : $"{command} or {alternative}",
        };
    }

    /// <summary>Writes the informational entry for a model outside the catalog.</summary>
    /// <param name="logger">The provider's logger, or <see langword="null"/>.</param>
    /// <param name="model">The model name.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="modelsSetting">Where the operator edits the catalog.</param>
    internal static void LogOutsideCatalog(ILogger? logger, string model, string providerName, string modelsSetting)
    {
        if (logger is not null && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Model '{Model}' is not in the '{Provider}' catalog; the request is sent anyway. " +
                "Use the {ModelsSetting} setting to add the model to the catalog.",
                model,
                providerName,
                modelsSetting);
        }
    }
}
