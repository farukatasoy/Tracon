using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Resolves the configured image provider without conflating it with another provider.</summary>
/// <remarks>
/// <para>
/// Provider packages register keyed generators using their stable provider names.
/// The unkeyed fallback preserves the public MEAI extension point for a consumer
/// that registers one custom generator directly.
/// </para>
/// <para>
/// Every path that sends a prompt to the image provider goes through
/// <see cref="ResolveForTenantAsync"/>, which applies the tenant's provider
/// checks first — the same rule the model registry and live voice apply.
/// <see cref="Resolve"/> stays for the startup checks that only ask whether a
/// generator is registered.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
internal sealed class ImageGeneratorResolver
{
    private readonly IServiceProvider _services;
    private readonly IOptions<TraconImageOptions> _options;
    private readonly ITenantEgressPolicyStore? _egressPolicies;
    private readonly ITenantProviderBindingStore? _providerBindings;
    private readonly TenantProviderCredentialResolver? _credentialResolver;

    public ImageGeneratorResolver(
        IServiceProvider services,
        IOptions<TraconImageOptions> options,
        ITenantEgressPolicyStore? egressPolicies = null,
        ITenantProviderBindingStore? providerBindings = null,
        TenantProviderCredentialResolver? credentialResolver = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _options = options;
        _egressPolicies = egressPolicies;
        _providerBindings = providerBindings;
        _credentialResolver = credentialResolver;
    }

    /// <summary>Returns the registered generator without a tenant check.</summary>
    /// <returns>The generator.</returns>
    /// <exception cref="TraconException">No generator is registered for the configured provider.</exception>
    /// <remarks>Only for the startup checks. A call that generates uses <see cref="ResolveForTenantAsync"/>.</remarks>
    public IImageGenerator Resolve()
    {
        var providerName = _options.Value.Provider;
        var generator = _services.GetKeyedService<IImageGenerator>(providerName)
            ?? _services.GetService<IImageGenerator>();

        return generator ?? throw new TraconException(
            $"{nameof(TraconImageOptions)} is enabled but no image generator is registered for provider " +
            $"'{providerName}'. Install a supported provider package and call UseOpenAIImages(), " +
            "UseAzureOpenAIImages(), or UseGoogleImages().");
    }

    /// <summary>
    /// Applies the tenant's provider checks, then returns the generator.
    /// </summary>
    /// <param name="tenantId">The tenant the images are generated for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generator.</returns>
    /// <exception cref="TenantProviderRefusedException">
    /// The tenant's egress policy does not allow the image provider, or the tenant
    /// has its own credential for it. The generator was built with the setup
    /// credential and cannot use the tenant's; generating anyway would bill the
    /// host's account, so the call is refused rather than sent (the live-voice rule).
    /// </exception>
    /// <remarks>
    /// The order is the one <see cref="TenantProviderAccess"/> fixes: the policy
    /// before any credential lookup. The provider is named by
    /// <see cref="TraconImageOptions.Provider"/>, which the options validator
    /// requires while images are enabled.
    /// </remarks>
    public async ValueTask<IImageGenerator> ResolveForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var providerName = _options.Value.Provider;

        if (providerName is { Length: > 0 })
        {
            if (_egressPolicies is not null)
            {
                var policy = await _egressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

                if (!TenantProviderAccess.IsAllowed(policy, providerName))
                {
                    throw new TenantProviderRefusedException(
                        TenantProviderAccess.DescribeDenial(policy!, tenantId, providerName));
                }
            }

            ModelProviderCredential? credential;

            try
            {
                credential = await TenantProviderAccess
                    .ResolveCredentialAsync(_providerBindings, _credentialResolver, tenantId, providerName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TraconException exception)
            {
                // A binding whose key has no value: the call does not fall back
                // to the setup key either.
                throw new TenantProviderRefusedException(exception.Message);
            }

            if (credential is not null)
            {
                throw new TenantProviderRefusedException(
                    $"Tenant '{tenantId}' has its own credential for '{providerName}', and the image generator " +
                    "cannot use it. The image is not generated on the setup credential.");
            }
        }

        return Resolve();
    }
}
#pragma warning restore MEAI001
