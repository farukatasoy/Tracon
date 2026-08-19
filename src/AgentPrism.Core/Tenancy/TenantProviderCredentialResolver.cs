using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Resolves a tenant's <see cref="TenantProviderBinding"/> into a
/// <see cref="ModelProviderCredential"/> by reading the bound configuration
/// key's value.
/// </summary>
/// <remarks>
/// 🚨 <see cref="AgentPrismTenantProviderOptions.AllowedConfigurationPrefix"/>
/// is checked here too, not only where a binding is written. A binding
/// written before the prefix was configured (or through a store the endpoint
/// layer did not validate) must not silently read an out-of-prefix
/// configuration key (section 65.2: defense in two layers).
/// </remarks>
public sealed class TenantProviderCredentialResolver
{
    private readonly IConfiguration? _configuration;
    private readonly IOptionsMonitor<AgentPrismTenantProviderOptions> _options;

    /// <summary>Creates a new resolver.</summary>
    /// <param name="configuration">
    /// The application's configuration root. When <see langword="null"/> (a
    /// hosting setup with no <see cref="IConfiguration"/> registered),
    /// <see cref="Resolve"/> always returns <see langword="null"/>.
    /// </param>
    /// <param name="options">The tenant provider options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public TenantProviderCredentialResolver(IConfiguration? configuration, IOptionsMonitor<AgentPrismTenantProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _configuration = configuration;
        _options = options;
    }

    /// <summary>
    /// Validates that a configuration key name is under the allowed prefix.
    /// </summary>
    /// <param name="configurationKeyName">The configuration key name to check.</param>
    /// <exception cref="AgentPrismException">
    /// The name is empty, or does not start with
    /// <see cref="AgentPrismTenantProviderOptions.AllowedConfigurationPrefix"/>.
    /// </exception>
    public void ValidatePrefix(string configurationKeyName)
    {
        if (string.IsNullOrWhiteSpace(configurationKeyName))
        {
            throw new AgentPrismException("'apiKeyConfigurationName' cannot be empty.");
        }

        var prefix = _options.CurrentValue.AllowedConfigurationPrefix;

        if (!configurationKeyName.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"'{configurationKeyName}' is outside the allowed prefix. A tenant provider " +
                $"binding may only reference a configuration key under '{prefix}'.");
        }
    }

    /// <summary>
    /// Resolves a binding into a credential by reading its configuration key.
    /// </summary>
    /// <param name="binding">The binding to resolve.</param>
    /// <returns>
    /// The resolved credential; <see langword="null"/> if the configuration
    /// key has no value (the binding exists, but the secret was never set —
    /// section 65.4, this must not fall back to the global key silently).
    /// </returns>
    /// <exception cref="AgentPrismException">
    /// <see cref="TenantProviderBinding.ApiKeyConfigurationName"/> is outside the allowed prefix.
    /// </exception>
    public ModelProviderCredential? Resolve(TenantProviderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        ValidatePrefix(binding.ApiKeyConfigurationName);

        var apiKey = _configuration?[binding.ApiKeyConfigurationName];

        return string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new ModelProviderCredential { ApiKey = apiKey, Endpoint = binding.Endpoint };
    }
}
