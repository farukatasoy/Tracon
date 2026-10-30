using Microsoft.Extensions.Options;

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
// One copy on purpose (a hand-copied rule drifts;live voice had
// no copy at all and sent a forbidden tenant's audio to OpenAI).
internal static class TenantProviderAccess
{
    /// <summary>The configuration key that makes a tenant with no egress policy call no provider.</summary>
    public const string RequirePolicySetting = "Tracon:TenantProviders:RequireEgressPolicy";

    /// <summary>
    /// Reads whether a tenant with no egress policy may call no provider at all
    /// (<see cref="TraconTenantProviderOptions.RequireEgressPolicy"/>).
    /// </summary>
    /// <param name="options">The tenant provider options; <see langword="null"/> when none are available.</param>
    /// <returns><see langword="true"/> when a missing policy refuses the call.</returns>
    /// <remarks>
    /// Read from <see cref="IOptions{TOptions}"/>, so the value is fixed when the host
    /// starts, as the production profile gate sees it. That is deliberate: the compiled-agent
    /// cache keys an agent on the tenant's policy, not on this switch, so a value that changed
    /// under a running host would leave an agent compiled under the old one in service. No options
    /// at all means the switch is off: only a hand-built component has none, never a container.
    /// </remarks>
    public static bool PolicyRequired(IOptions<TraconTenantProviderOptions>? options)
        => options?.Value.RequireEgressPolicy ?? false;

    /// <summary>Refuses a provider the tenant's egress policy does not allow.</summary>
    /// <param name="policy">
    /// The tenant's policy; <see langword="null"/> when it has none (no restriction, unless
    /// <paramref name="requirePolicy"/> is set).
    /// </param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The provider about to be called.</param>
    /// <param name="requirePolicy">
    /// Whether a tenant with no policy may call no provider
    /// (<see cref="TraconTenantProviderOptions.RequireEgressPolicy"/>).
    /// </param>
    /// <param name="kind">The kind of provider the call is for; only changes the noun in the message.</param>
    /// <param name="fallbackFor">
    /// The primary provider when <paramref name="provider"/> is one of its fallbacks;
    /// <see langword="null"/> otherwise. Only changes the message.
    /// </param>
    /// <exception cref="TraconTenantProviderRefusedException">The policy does not allow <paramref name="provider"/>.</exception>
    public static void EnsureAllowed(
        TenantEgressPolicy? policy,
        string tenantId,
        string provider,
        bool requirePolicy,
        TenantProviderCallKind kind = TenantProviderCallKind.Model,
        string? fallbackFor = null)
    {
        if (IsAllowed(policy, provider, requirePolicy))
        {
            return;
        }

        throw new TraconTenantProviderRefusedException(DescribeDenial(policy, tenantId, provider, kind, fallbackFor));
    }

    /// <summary>Says whether the tenant's egress policy allows a provider.</summary>
    /// <param name="policy">
    /// The tenant's policy; <see langword="null"/> when it has none (no restriction, unless
    /// <paramref name="requirePolicy"/> is set).
    /// </param>
    /// <param name="provider">The provider.</param>
    /// <param name="requirePolicy">
    /// Whether a tenant with no policy may call no provider
    /// (<see cref="TraconTenantProviderOptions.RequireEgressPolicy"/>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the policy names the provider, or there is no policy and none is required.
    /// </returns>
    public static bool IsAllowed(TenantEgressPolicy? policy, string provider, bool requirePolicy)
        => policy is null
            ? !requirePolicy
            : policy.AllowedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);

    /// <summary>Describes a refusal of a model provider by the tenant's egress policy.</summary>
    /// <param name="policy">The policy that refused; <see langword="null"/> when the tenant has none.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The refused provider.</param>
    /// <param name="fallbackFor">The primary provider when the refused one is its fallback.</param>
    /// <returns>The message.</returns>
    public static string DescribeDenial(TenantEgressPolicy? policy, string tenantId, string provider, string? fallbackFor = null)
        => DescribeDenial(policy, tenantId, provider, TenantProviderCallKind.Model, fallbackFor);

    /// <summary>Describes a refusal by the tenant's egress policy.</summary>
    /// <param name="policy">
    /// The policy that refused; <see langword="null"/> when the tenant has none and the
    /// deployment requires one.
    /// </param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="provider">The refused provider.</param>
    /// <param name="kind">The kind of provider the call was for; only changes the noun.</param>
    /// <param name="fallbackFor">The primary provider when the refused one is its fallback.</param>
    /// <returns>The message.</returns>
    public static string DescribeDenial(
        TenantEgressPolicy? policy,
        string tenantId,
        string provider,
        TenantProviderCallKind kind,
        string? fallbackFor = null)
    {
        // The model wording is pinned by the model-path tests and by clients that
        // match it; a speech refusal names its own kind instead of claiming a model.
        var noun = kind switch
        {
            TenantProviderCallKind.Speech => "speech provider",
            TenantProviderCallKind.Embedding => "embedding provider",
            TenantProviderCallKind.Image => "image provider",
            TenantProviderCallKind.LiveVoice => "live voice provider",
            _ => "model provider",
        };

        var used = fallbackFor is null ? string.Empty : $" (used as a fallback for '{fallbackFor}')";

        if (policy is null)
        {
            // The tenant has no row and the deployment asked for one. The message
            // names the setting so the operator learns WHY a tenant that was never
            // restricted is refused, and what turns it back.
            return $"Tenant '{tenantId}' has no egress policy, and {RequirePolicySetting} is on, so it is not " +
                   $"allowed to call {noun} '{provider}'{used}. Save an egress policy for the tenant " +
                   "(PUT /api/tenants/{tenantId}/egress) that lists the providers it may call, or turn " +
                   $"{RequirePolicySetting} off.";
        }

        var allowed = string.Join(", ", policy.AllowedProviders);

        return $"Tenant '{tenantId}' is not allowed to call {noun} '{provider}'{used}. Allowed providers: {allowed}.";
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
    /// <exception cref="TraconTenantProviderRefusedException">
    /// The tenant has a binding whose configuration key has no value, or a key the
    /// tenant may not read (outside the allowed prefix or the tenant's own segment).
    /// The call does not fall back to the global key: that would bill the wrong account.
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

        ModelProviderCredential? credential;

        try
        {
            credential = resolver.Resolve(binding);
        }
        catch (TraconException exception) when (exception is not TraconTenantProviderRefusedException)
        {
            // A key outside the allowed prefix or the tenant's segment is the tenant's
            // own rule refusing too. The three callers used to wrap every error of
            // this call themselves; one wrap here keeps the refusal one type.
            throw new TraconTenantProviderRefusedException(exception.Message, exception);
        }

        return credential
            ?? throw new TraconTenantProviderRefusedException(
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

    /// <summary>The knowledge base's embedding provider.</summary>
    Embedding = 2,

    /// <summary>The image generator's provider.</summary>
    Image = 3,

    /// <summary>The provider of a live (speech-to-speech) voice session.</summary>
    LiveVoice = 4,
}
