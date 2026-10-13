namespace Tracon;

/// <summary>
/// The two per-tenant checks every call to a provider passes before it leaves the
/// process: the tenant's egress policy, then the tenant's own credential.
/// </summary>
/// <remarks>
/// <para>
/// Written once and shared by every path that reaches a provider: the model
/// registry (the agent's own model, its fallbacks, the summarizer, the loop judge),
/// the live voice session, image generation and the speech provider resolver. Each path used to carry its own copy of the policy
/// check; a copy that is not there is a provider the tenant forbade receiving the
/// tenant's data, so the rule lives in one place.
/// </para>
/// <para>
/// The order matters. The policy is checked BEFORE any credential lookup: looking up
/// a key for a forbidden provider is a path that should not run at all.
/// </para>
/// </remarks>
// One copy on purpose (K-483: a hand-copied rule drifts; F-306: live voice had
// no copy at all and sent a forbidden tenant's audio to OpenAI).
internal static class TenantProviderAccess
{
    /// <summary>Refuses a provider the tenant's egress policy does not allow.</summary>
    /// <param name="policy">The tenant's policy; <see langword="null"/> when it has none (no restriction).</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The provider about to be called.</param>
    /// <param name="fallbackFor">
    /// The primary provider when <paramref name="provider"/> is one of its fallbacks;
    /// <see langword="null"/> otherwise. Only changes the message.
    /// </param>
    /// <exception cref="TraconException">The policy does not allow <paramref name="provider"/>.</exception>
    public static void EnsureAllowed(TenantEgressPolicy? policy, string tenantId, string provider, string? fallbackFor = null)
    {
        if (IsAllowed(policy, provider))
        {
            return;
        }

        throw new TraconException(DescribeDenial(policy!, tenantId, provider, fallbackFor));
    }

    /// <summary>Says whether the tenant's egress policy allows a provider.</summary>
    /// <param name="policy">The tenant's policy; <see langword="null"/> when it has none (no restriction).</param>
    /// <param name="provider">The provider.</param>
    /// <returns><see langword="true"/> when there is no policy or it names the provider.</returns>
    public static bool IsAllowed(TenantEgressPolicy? policy, string provider)
        => policy is null || policy.AllowedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);

    /// <summary>Describes a refusal of a model provider by the tenant's egress policy.</summary>
    /// <param name="policy">The policy that refused.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The refused provider.</param>
    /// <param name="fallbackFor">The primary provider when the refused one is its fallback.</param>
    /// <returns>The message.</returns>
    public static string DescribeDenial(TenantEgressPolicy policy, string tenantId, string provider, string? fallbackFor = null)
        => DescribeDenial(policy, tenantId, provider, TenantProviderCallKind.Model, fallbackFor);

    /// <summary>Describes a refusal by the tenant's egress policy.</summary>
    /// <param name="policy">The policy that refused.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The refused provider.</param>
    /// <param name="kind">The kind of provider the call was for; only changes the noun.</param>
    /// <param name="fallbackFor">The primary provider when the refused one is its fallback.</param>
    /// <returns>The message.</returns>
    public static string DescribeDenial(
        TenantEgressPolicy policy,
        string tenantId,
        string provider,
        TenantProviderCallKind kind,
        string? fallbackFor = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var allowed = string.Join(", ", policy.AllowedProviders);

        // The model wording is pinned by the model-path tests and by clients that
        // match it; a speech refusal names its own kind instead of claiming a model.
        var noun = kind == TenantProviderCallKind.Speech ? "speech provider" : "model provider";

        return fallbackFor is null
            ? $"Tenant '{tenantId}' is not allowed to call {noun} '{provider}'. Allowed providers: {allowed}."
            : $"Tenant '{tenantId}' is not allowed to call {noun} '{provider}' " +
              $"(used as a fallback for '{fallbackFor}'). Allowed providers: {allowed}.";
    }

    /// <summary>Resolves the tenant's own credential for a provider.</summary>
    /// <param name="bindings">The binding store; <see langword="null"/> when none is registered.</param>
    /// <param name="resolver">The resolver; <see langword="null"/> when none is registered.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The provider about to be called.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The tenant's credential, or <see langword="null"/> when the tenant has no
    /// binding for the provider (the setup-time credential is used).
    /// </returns>
    /// <exception cref="TraconException">
    /// The tenant has a binding whose configuration key has no value. The call does
    /// not fall back to the global key: that would bill the wrong account.
    /// </exception>
    public static async ValueTask<ModelProviderCredential?> ResolveCredentialAsync(
        ITenantProviderBindingStore? bindings,
        TenantProviderCredentialResolver? resolver,
        string tenantId,
        string provider,
        CancellationToken cancellationToken)
    {
        if (bindings is null || resolver is null)
        {
            return null;
        }

        var binding = await bindings.GetAsync(tenantId, provider, cancellationToken).ConfigureAwait(false);

        if (binding is null)
        {
            return null;
        }

        return resolver.Resolve(binding)
            ?? throw new TraconException(
                $"Tenant '{tenantId}' has a provider binding for '{provider}' pointing at configuration key " +
                $"'{binding.ApiKeyConfigurationName}', but that key has no value. Set it with " +
                $"`dotnet user-secrets set \"{binding.ApiKeyConfigurationName}\" \"<key>\"` " +
                "or through your configuration provider — the call does NOT fall back to the global key.");
    }
}

/// <summary>The kind of provider a tenant check is made for.</summary>
internal enum TenantProviderCallKind
{
    /// <summary>A model provider: the agent's model, a fallback, the summarizer or the loop judge.</summary>
    Model = 0,

    /// <summary>A speech provider: a synthesizer or a transcriber.</summary>
    Speech = 1,
}
