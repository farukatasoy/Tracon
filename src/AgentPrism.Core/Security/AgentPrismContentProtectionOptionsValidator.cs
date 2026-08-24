using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Validates <see cref="AgentPrismContentProtectionOptions"/> settings at application startup.</summary>
/// <remarks>
/// <para>
/// Checks structure only — that <see cref="AgentPrismContentProtectionOptions.ActiveKeyId"/>
/// is set and names an entry in <see cref="AgentPrismContentProtectionOptions.Keys"/>. It
/// does not resolve a key's raw material through <c>IConfiguration</c>: that
/// resolution is lazy and happens on first use (<see cref="AesGcmContentProtector"/>),
/// so a startup validator that resolved it eagerly would defeat the laziness
/// and would need its own <c>IConfiguration</c> dependency for no lasting benefit.
/// </para>
/// <para>
/// Hand-written; <c>ValidateDataAnnotations()</c> relies on reflection and produces
/// <c>IL2026</c>. <c>AgentPrism.Core</c> must stay AOT-compatible.
/// </para>
/// </remarks>
internal sealed class AgentPrismContentProtectionOptionsValidator : IValidateOptions<AgentPrismContentProtectionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismContentProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismContentProtectionOptions)}.{nameof(AgentPrismContentProtectionOptions.Enabled)} is true, but " +
                $"{nameof(AgentPrismContentProtectionOptions.ActiveKeyId)} is not set.");
        }
        else if (!options.Keys.ContainsKey(options.ActiveKeyId))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismContentProtectionOptions)}.{nameof(AgentPrismContentProtectionOptions.ActiveKeyId)} " +
                $"('{options.ActiveKeyId}') has no matching entry in " +
                $"{nameof(AgentPrismContentProtectionOptions.Keys)}.");
        }

        foreach (var (keyId, configurationKeyName) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(configurationKeyName))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismContentProtectionOptions)}.{nameof(AgentPrismContentProtectionOptions.Keys)}['{keyId}'] " +
                    "cannot be empty. It must name the configuration key the key's raw value is read from.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
