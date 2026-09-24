using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Resolves a tenant's <see cref="TenantProviderBinding"/> into a
/// <see cref="ModelProviderCredential"/> by reading the bound configuration
/// key's value.
/// </summary>
/// <remarks>
/// <para>
/// A binding may name only a key inside its own tenant's key space:
/// <c>{prefix}{tenant}:...</c>, where the prefix is
/// <see cref="TraconTenantProviderOptions.AllowedConfigurationPrefix"/>. A
/// flat name directly under the prefix belongs to the default tenant
/// (<see cref="TraconOptions.DefaultTenantId"/>). Without the tenant segment,
/// tenant A could bind tenant B's key together with an endpoint override
/// that A controls, and every model call would send B's key there.
/// </para>
/// <para>
/// The rule is checked here too, not only where a binding is written. A
/// binding written before the rule existed (or through a store the endpoint
/// layer did not validate) must not silently read a key outside its tenant
/// (defense in two layers).
/// </para>
/// </remarks>
internal sealed class TenantProviderCredentialResolver
{
    private readonly IConfiguration? _configuration;
    private readonly IOptionsMonitor<TraconTenantProviderOptions> _options;
    private readonly IOptions<TraconOptions> _coreOptions;

    /// <summary>Creates a new resolver.</summary>
    /// <param name="configuration">
    /// The application's configuration root. When <see langword="null"/> (a
    /// hosting setup with no <see cref="IConfiguration"/> registered),
    /// <see cref="Resolve"/> always returns <see langword="null"/>.
    /// </param>
    /// <param name="options">The tenant provider options.</param>
    /// <param name="coreOptions">
    /// The core options; <see cref="TraconOptions.DefaultTenantId"/> names the
    /// tenant that owns the flat key names.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> or <paramref name="coreOptions"/> is <see langword="null"/>.
    /// </exception>
    public TenantProviderCredentialResolver(
        IConfiguration? configuration,
        IOptionsMonitor<TraconTenantProviderOptions> options,
        IOptions<TraconOptions> coreOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(coreOptions);

        _configuration = configuration;
        _options = options;
        _coreOptions = coreOptions;
    }

    /// <summary>
    /// Validates that a configuration key name is under the allowed prefix
    /// and inside the key space of <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="tenantId">The tenant the binding belongs to.</param>
    /// <param name="configurationKeyName">The configuration key name to check.</param>
    /// <exception cref="TraconException">
    /// The name is empty, does not start with
    /// <see cref="TraconTenantProviderOptions.AllowedConfigurationPrefix"/>,
    /// or names a key that belongs to another tenant.
    /// </exception>
    public void ValidateKeyName(string tenantId, string configurationKeyName)
        => ConfigurationKeyGuard.RequireTenantKey(
            configurationKeyName,
            _options.CurrentValue.AllowedConfigurationPrefix,
            tenantId,
            _coreOptions.Value.DefaultTenantId,
            "apiKeyConfigurationName");

    /// <summary>
    /// Resolves a binding into a credential by reading its configuration key.
    /// </summary>
    /// <param name="binding">The binding to resolve.</param>
    /// <returns>
    /// The resolved credential; <see langword="null"/> if the configuration
    /// key has no value (the binding exists, but the secret was never set —
    ///  this must not fall back to the global key silently).
    /// </returns>
    /// <exception cref="TraconException">
    /// <see cref="TenantProviderBinding.ApiKeyConfigurationName"/> is outside
    /// the allowed prefix or outside the binding's tenant.
    /// </exception>
    public ModelProviderCredential? Resolve(TenantProviderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        ValidateKeyName(binding.TenantId, binding.ApiKeyConfigurationName);

        var apiKey = _configuration?[binding.ApiKeyConfigurationName];

        return string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new ModelProviderCredential { ApiKey = apiKey, Endpoint = binding.Endpoint };
    }
}
