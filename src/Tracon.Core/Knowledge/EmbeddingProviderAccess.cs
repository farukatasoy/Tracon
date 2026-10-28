using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The tenant's provider checks for the knowledge base's embedding calls: the
/// egress policy first, then the tenant's own credential.
/// </summary>
/// <remarks>
/// <para>
/// Every embedding call asks this before the tenant's text leaves the process: the
/// <c>search_knowledge</c> query, the knowledge search endpoint and a document upload.
/// The rule is the one <see cref="TenantProviderAccess"/> fixes for every other path
/// to a provider; image generation is the precedent for a generator built with the
/// setup key.
/// </para>
/// <para>
/// The provider is named by <see cref="TraconKnowledgeOptions.EmbeddingProvider"/>,
/// or else by the name the generator reports. The reported name alone cannot be
/// trusted for a policy (measured: the OpenAI and the Azure OpenAI generators both
/// report <c>openai</c>, Google.GenAI reports <c>gcp.gen_ai</c>), so a configured
/// name wins. When neither is known, a tenant with a policy is refused: its policy
/// cannot be judged, and letting the call through would send its text to an
/// unknown provider.
/// </para>
/// <para>
/// Nothing is cached. The policy and the binding are read on every call, so a
/// policy saved now stops the next call.
/// </para>
/// </remarks>
internal sealed class EmbeddingProviderAccess
{
    private readonly IOptions<TraconKnowledgeOptions> _options;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _generator;
    private readonly ITenantEgressPolicyStore? _egressPolicies;
    private readonly ITenantProviderBindingStore? _providerBindings;
    private readonly TenantProviderCredentialResolver? _credentialResolver;
    private readonly IOptions<TraconTenantProviderOptions>? _tenantProviderOptions;

    /// <summary>Creates the access check.</summary>
    /// <param name="options">The knowledge options; <see cref="TraconKnowledgeOptions.EmbeddingProvider"/> names the provider.</param>
    /// <param name="generator">The registered embedding generator; its reported name is the fallback.</param>
    /// <param name="egressPolicies">The tenant egress policy store; <see langword="null"/> applies no policy.</param>
    /// <param name="providerBindings">The tenant provider binding store; <see langword="null"/> checks no binding.</param>
    /// <param name="credentialResolver">Resolves a binding's key; <see langword="null"/> checks no binding.</param>
    /// <param name="tenantProviderOptions">
    /// The tenant provider options; <see langword="null"/> does not require a policy.
    /// </param>
    public EmbeddingProviderAccess(
        IOptions<TraconKnowledgeOptions> options,
        IEmbeddingGenerator<string, Embedding<float>>? generator = null,
        ITenantEgressPolicyStore? egressPolicies = null,
        ITenantProviderBindingStore? providerBindings = null,
        TenantProviderCredentialResolver? credentialResolver = null,
        IOptions<TraconTenantProviderOptions>? tenantProviderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _generator = generator;
        _egressPolicies = egressPolicies;
        _providerBindings = providerBindings;
        _credentialResolver = credentialResolver;
        _tenantProviderOptions = tenantProviderOptions;
    }

    /// <summary>The provider name the checks are made against, or <see langword="null"/> when none is known.</summary>
    public string? ProviderName
        => _options.Value.EmbeddingProvider is { Length: > 0 } configured
            ? configured
            : _generator?.GetService<EmbeddingGeneratorMetadata>()?.ProviderName is { Length: > 0 } reported ? reported : null;

    /// <summary>Refuses an embedding call the tenant's provider checks do not allow.</summary>
    /// <param name="tenantId">The tenant whose text is about to be embedded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the call is allowed.</returns>
    /// <exception cref="TraconTenantProviderRefusedException">
    /// The tenant's egress policy does not allow the embedding provider, the provider
    /// is not named while the tenant has a policy, or the tenant has its own credential
    /// for the provider, which the generator cannot use.
    /// </exception>
    public async ValueTask EnsureAllowedAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var providerName = ProviderName;
        var requirePolicy = TenantProviderAccess.PolicyRequired(_tenantProviderOptions);
        var policy = _egressPolicies is null
            ? null
            : await _egressPolicies.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (providerName is null)
        {
            // With a policy required, a tenant with no policy is refused here too: the provider
            // is not named, so nothing says the call is one the policy would allow (F-254).
            if (policy is not null || requirePolicy)
            {
                throw new TraconTenantProviderRefusedException(
                    $"Tenant '{tenantId}' " +
                    (policy is not null
                        ? "has an egress policy"
                        : $"must have an egress policy ({TenantProviderAccess.RequirePolicySetting} is on)") +
                    ", and the embedding provider is not named, so the " +
                    $"policy cannot be applied. Set {TraconKnowledgeOptions.SectionName}:" +
                    $"{nameof(TraconKnowledgeOptions.EmbeddingProvider)} to the provider the embedding generator calls.");
            }

            return;
        }

        if (!TenantProviderAccess.IsAllowed(policy, providerName, requirePolicy))
        {
            throw new TraconTenantProviderRefusedException(
                TenantProviderAccess.DescribeDenial(policy, tenantId, providerName, TenantProviderCallKind.Embedding));
        }

        // A binding whose key has no value is refused there: the call does not fall
        // back to the setup key either.
        var credential = await TenantProviderAccess
            .ResolveCredentialAsync(_providerBindings, _credentialResolver, tenantId, providerName, cancellationToken)
            .ConfigureAwait(false);

        if (credential is not null)
        {
            throw new TraconTenantProviderRefusedException(
                $"Tenant '{tenantId}' has its own credential for '{providerName}', and the embedding generator " +
                "cannot use it. The text is not embedded on the setup credential.");
        }
    }
}
