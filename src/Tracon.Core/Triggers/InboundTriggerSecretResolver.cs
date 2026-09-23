using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Resolves an <see cref="InboundTrigger"/>'s signing secret by reading its
/// configured configuration key.
/// </summary>
/// <remarks>
/// The same shape as <see cref="TenantProviderCredentialResolver"/>: the key
/// must sit under
/// <see cref="TraconInboundTriggerOptions.AllowedConfigurationPrefix"/> and
/// inside the trigger's own tenant (<c>{prefix}{tenant}:...</c>; a flat name
/// belongs to the default tenant). It is checked here too, not only where a
/// trigger is saved — a trigger written before the rule existed must not
/// silently read a key outside its tenant.
/// </remarks>
internal sealed class InboundTriggerSecretResolver
{
    private readonly IConfiguration? _configuration;
    private readonly IOptionsMonitor<TraconInboundTriggerOptions> _options;
    private readonly IOptions<TraconOptions> _coreOptions;

    /// <summary>Creates a new resolver.</summary>
    /// <param name="configuration">
    /// The application's configuration root. When <see langword="null"/>,
    /// <see cref="Resolve"/> always returns <see langword="null"/>.
    /// </param>
    /// <param name="options">The inbound trigger options.</param>
    /// <param name="coreOptions">The core options; they name the default tenant.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> or <paramref name="coreOptions"/> is <see langword="null"/>.
    /// </exception>
    public InboundTriggerSecretResolver(
        IConfiguration? configuration,
        IOptionsMonitor<TraconInboundTriggerOptions> options,
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
    /// <param name="tenantId">The tenant the trigger belongs to.</param>
    /// <param name="configurationKeyName">The configuration key name to check.</param>
    /// <exception cref="TraconException">
    /// The name is empty, does not start with
    /// <see cref="TraconInboundTriggerOptions.AllowedConfigurationPrefix"/>,
    /// or names a key that belongs to another tenant.
    /// </exception>
    public void ValidateKeyName(string tenantId, string configurationKeyName)
        => ConfigurationKeyGuard.RequireTenantKey(
            configurationKeyName,
            _options.CurrentValue.AllowedConfigurationPrefix,
            tenantId,
            _coreOptions.Value.DefaultTenantId,
            "signingSecretConfigurationName");

    /// <summary>Resolves a trigger's signing secret by reading its configuration key.</summary>
    /// <param name="trigger">The trigger to resolve.</param>
    /// <returns>
    /// The secret value; <see langword="null"/> if the configuration key has
    /// no value.
    /// </returns>
    /// <exception cref="TraconException">
    /// <see cref="InboundTrigger.SigningSecretConfigurationName"/> is outside
    /// the allowed prefix or outside the trigger's tenant.
    /// </exception>
    public string? Resolve(InboundTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        ValidateKeyName(trigger.TenantId, trigger.SigningSecretConfigurationName);

        var secret = _configuration?[trigger.SigningSecretConfigurationName];

        return string.IsNullOrWhiteSpace(secret) ? null : secret;
    }
}
