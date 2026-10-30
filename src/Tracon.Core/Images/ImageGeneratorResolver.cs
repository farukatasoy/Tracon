using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly IOptions<TraconImageOptions> _options;
    private readonly ITenantEgressPolicyStore? _egressPolicies;
    private readonly ITenantProviderBindingStore? _providerBindings;
    private readonly TenantProviderCredentialResolver? _credentialResolver;
    private readonly IOptions<TraconTenantProviderOptions>? _tenantProviderOptions;
    private readonly ConditionalWeakTable<IImageGenerator, IImageGenerator> _rings = new();

    public ImageGeneratorResolver(
        IServiceProvider services,
        IOptions<TraconImageOptions> options,
        ITenantEgressPolicyStore? egressPolicies = null,
        ITenantProviderBindingStore? providerBindings = null,
        TenantProviderCredentialResolver? credentialResolver = null,
        IOptions<TraconTenantProviderOptions>? tenantProviderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _options = options;
        _egressPolicies = egressPolicies;
        _providerBindings = providerBindings;
        _credentialResolver = credentialResolver;
        _tenantProviderOptions = tenantProviderOptions;
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

        return generator is null
            ? throw new TraconException(
                $"{nameof(TraconImageOptions)} is enabled but no image generator is registered for provider " +
                $"'{providerName}'. Install a supported provider package and call UseOpenAIImages(), " +
                "UseAzureOpenAIImages(), or UseGoogleImages().")
            : _rings.GetValue(generator, WrapInRing);
    }

    /// <summary>
    /// Applies the tenant's provider checks, then returns the generator.
    /// </summary>
    /// <param name="tenantId">The tenant the images are generated for.</param>
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
        }

        return Resolve();
    }

    private IImageGenerator WrapInRing(IImageGenerator generator)
        => new ImageGeneratorBuilder(generator)
            .UseOpenTelemetry(_services.GetService<ILoggerFactory>(), TraconDiagnostics.ActivitySourceName)
            .Build();
}
#pragma warning restore MEAI001
