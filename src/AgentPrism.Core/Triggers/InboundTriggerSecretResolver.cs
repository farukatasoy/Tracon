using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Resolves an <see cref="InboundTrigger"/>'s signing secret by reading its
/// configured configuration key.
/// </summary>
/// <remarks>
/// The same shape as <see cref="TenantProviderCredentialResolver"/>:
/// <see cref="AgentPrismInboundTriggerOptions.AllowedConfigurationPrefix"/>
/// is checked here too, not only where a trigger is saved — a trigger written
/// before the prefix was configured must not silently read an out-of-prefix
/// configuration key.
/// </remarks>
public sealed class InboundTriggerSecretResolver
{
    private readonly IConfiguration? _configuration;
    private readonly IOptionsMonitor<AgentPrismInboundTriggerOptions> _options;

    /// <summary>Creates a new resolver.</summary>
    /// <param name="configuration">
    /// The application's configuration root. When <see langword="null"/>,
    /// <see cref="Resolve"/> always returns <see langword="null"/>.
    /// </param>
    /// <param name="options">The inbound trigger options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public InboundTriggerSecretResolver(IConfiguration? configuration, IOptionsMonitor<AgentPrismInboundTriggerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _configuration = configuration;
        _options = options;
    }

    /// <summary>Validates that a configuration key name is under the allowed prefix.</summary>
    /// <param name="configurationKeyName">The configuration key name to check.</param>
    /// <exception cref="AgentPrismException">
    /// The name is empty, or does not start with
    /// <see cref="AgentPrismInboundTriggerOptions.AllowedConfigurationPrefix"/>.
    /// </exception>
    public void ValidatePrefix(string configurationKeyName)
    {
        if (string.IsNullOrWhiteSpace(configurationKeyName))
        {
            throw new AgentPrismException("'signingSecretConfigurationName' cannot be empty.");
        }

        var prefix = _options.CurrentValue.AllowedConfigurationPrefix;

        if (!configurationKeyName.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"'{configurationKeyName}' is outside the allowed prefix. A trigger's signing " +
                $"secret may only reference a configuration key under '{prefix}'.");
        }
    }

    /// <summary>Resolves a trigger's signing secret by reading its configuration key.</summary>
    /// <param name="trigger">The trigger to resolve.</param>
    /// <returns>
    /// The secret value; <see langword="null"/> if the configuration key has
    /// no value.
    /// </returns>
    /// <exception cref="AgentPrismException">
    /// <see cref="InboundTrigger.SigningSecretConfigurationName"/> is outside the allowed prefix.
    /// </exception>
    public string? Resolve(InboundTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        ValidatePrefix(trigger.SigningSecretConfigurationName);

        var secret = _configuration?[trigger.SigningSecretConfigurationName];

        return string.IsNullOrWhiteSpace(secret) ? null : secret;
    }
}
