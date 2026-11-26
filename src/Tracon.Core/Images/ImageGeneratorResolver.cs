using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Resolves an image provider's generator without conflating it with another provider.</summary>
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
/// <see cref="Resolve()"/> stays for the startup checks that only ask whether a
/// generator is registered.
/// </para>
/// <para>
/// Both paths return the generator inside Microsoft.Extensions.AI's OpenTelemetry ring, so a generation is
/// a span of the run that asked for it. The ring is built once per registered generator and kept: the resolver is asked
/// on every generation, and a ring owns an <see cref="System.Diagnostics.ActivitySource"/> and a
/// <see cref="System.Diagnostics.Metrics.Meter"/>. It is not disposed here, because disposing it would dispose the
/// consumer's generator. The ring never sets <c>EnableSensitiveData</c>: that switch stays the consumer's, and the
/// trace store filter decides what is kept.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
internal sealed class ImageGeneratorResolver
{
    private readonly IServiceProvider _services;
    private readonly ImageProviderSelection _selection;
    private readonly ITenantEgressPolicyStore? _egressPolicies;
    private readonly ITenantProviderBindingStore? _providerBindings;
    private readonly TenantProviderCredentialResolver? _credentialResolver;
    private readonly IOptions<TraconTenantProviderOptions>? _tenantProviderOptions;
    private readonly ConditionalWeakTable<IImageGenerator, IImageGenerator> _rings = new();

    public ImageGeneratorResolver(
        IServiceProvider services,
        ImageProviderSelection selection,
        ITenantEgressPolicyStore? egressPolicies = null,
        ITenantProviderBindingStore? providerBindings = null,
        TenantProviderCredentialResolver? credentialResolver = null,
        IOptions<TraconTenantProviderOptions>? tenantProviderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(selection);

        _services = services;
        _selection = selection;
        _egressPolicies = egressPolicies;
        _providerBindings = providerBindings;
        _credentialResolver = credentialResolver;
        _tenantProviderOptions = tenantProviderOptions;
    }

    /// <summary>Returns the generator of the default provider without a tenant check.</summary>
    /// <returns>The generator.</returns>
    /// <exception cref="TraconException">No default provider can be told, or no generator is registered for it.</exception>
    /// <remarks>Only for the startup checks. A call that generates uses <see cref="ResolveForTenantAsync"/>.</remarks>
    public IImageGenerator Resolve() => Resolve(_selection.DefaultProvider());

    /// <summary>Returns the registered generator of a provider without a tenant check.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <returns>The generator.</returns>
    /// <exception cref="TraconException">No generator is registered for the provider.</exception>
    /// <remarks>Only for the startup checks. A call that generates uses <see cref="ResolveForTenantAsync"/>.</remarks>
    public IImageGenerator Resolve(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var generator = _services.GetKeyedService<IImageGenerator>(providerName)
            ?? _services.GetService<IImageGenerator>();

        return generator is null
            ? throw new TraconException(
                $"Image generation is enabled but no image generator is registered for provider " +
                $"'{providerName}'. Install a supported provider package and call UseOpenAIImages(), " +
                "UseAzureOpenAIImages(), or UseGoogleImages().")
            : _rings.GetValue(generator, candidate => WrapInRing(candidate, providerName));
    }

    /// <summary>
    /// Applies the tenant's provider checks, then returns the generator.
    /// </summary>
    /// <param name="tenantId">The tenant the images are generated for.</param>
    /// <param name="providerName">The provider the images are generated with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generator.</returns>
    /// <exception cref="TraconTenantProviderRefusedException">
    /// The tenant's egress policy does not allow the image provider, or the tenant
    /// has its own credential for it. The generator was built with the setup
    /// credential and cannot use the tenant's; generating anyway would bill the
    /// host's account, so the call is refused rather than sent (the live-voice rule).
    /// </exception>
    /// <remarks>
    /// The order is the one <see cref="TenantProviderAccess"/> fixes: the policy
    /// before any credential lookup. The provider is the one the call selected
    /// (<see cref="ImageProviderSelection.Select"/>), so the tenant's policy is asked about the provider the prompt goes to.
    /// </remarks>
    public async ValueTask<IImageGenerator> ResolveForTenantAsync(string tenantId, string providerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var requirePolicy = TenantProviderAccess.PolicyRequired(_tenantProviderOptions);

        if (_egressPolicies is not null || requirePolicy)
        {
            var policy = _egressPolicies is null
                ? null
                : await _egressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

            if (!TenantProviderAccess.IsAllowed(policy, providerName, requirePolicy))
            {
                throw new TraconTenantProviderRefusedException(
                    TenantProviderAccess.DescribeDenial(policy, tenantId, providerName, TenantProviderCallKind.Image));
            }
        }

        // A binding whose key has no value is refused there: the call does not
        // fall back to the setup key either.
        var credential = await TenantProviderAccess
            .ResolveCredentialAsync(_providerBindings, _credentialResolver, tenantId, providerName, cancellationToken)
            .ConfigureAwait(false);

        if (credential is not null)
        {
            throw new TraconTenantProviderRefusedException(
                $"Tenant '{tenantId}' has its own credential for '{providerName}', and the image generator " +
                "cannot use it. The image is not generated on the setup credential.");
        }

        return Resolve(providerName);
    }

    private IImageGenerator WrapInRing(IImageGenerator generator, string providerName)
        => new ImageGeneratorBuilder(ProviderFailureMasking.Wrap(generator, _services, providerName))
            .UseOpenTelemetry(_services.GetService<ILoggerFactory>(), TraconDiagnostics.ActivitySourceName)
            .Build();
}
#pragma warning restore MEAI001
