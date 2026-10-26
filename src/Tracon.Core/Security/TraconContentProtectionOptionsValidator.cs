using System.Globalization;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates <see cref="TraconContentProtectionOptions"/> settings at application startup.</summary>
/// <remarks>
/// <para>
/// Checks structure only — that every <see cref="TraconContentProtectionOptions.Keys"/>
/// entry names a configuration key, that every <see cref="TraconContentProtectionOptions.Columns"/>
/// member is a defined <see cref="ProtectedColumn"/>, and, while protection is on,
/// that <see cref="TraconContentProtectionOptions.ActiveKeyId"/> is set and names an
/// entry in <see cref="TraconContentProtectionOptions.Keys"/>. It does not resolve a
/// key's raw material through <c>IConfiguration</c>: that resolution is lazy and
/// happens on first use (<see cref="AesGcmContentProtector"/>), so a startup
/// validator that resolved it eagerly would defeat the laziness and would need its
/// own <c>IConfiguration</c> dependency for no lasting benefit.
/// </para>
/// <para>
/// The <c>Keys</c> and <c>Columns</c> checks run while protection is OFF too.
/// Resolving a key does not look at <see cref="TraconContentProtectionOptions.Enabled"/>:
/// an old encrypted row is still read through <c>Keys</c> after protection is turned
/// off. The same rule as <c>TraconSchedulingOptionsValidator</c>, which checks its
/// collections whatever <c>Enabled</c> says.
/// </para>
/// <para>
/// Hand-written; <c>ValidateDataAnnotations()</c> relies on reflection and produces
/// <c>IL2026</c>. <c>Tracon.Core</c> must stay AOT-compatible.
/// </para>
/// </remarks>
internal sealed class TraconContentProtectionOptionsValidator : IValidateOptions<TraconContentProtectionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconContentProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        foreach (var (keyId, configurationKeyName) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(configurationKeyName))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconContentProtectionOptions)}.{nameof(TraconContentProtectionOptions.Keys)}['{keyId}'] " +
                    "cannot be empty. It must name the configuration key the key's raw value is read from.");
            }
        }

        foreach (var column in options.Columns)
        {
            if (!Enum.IsDefined(column))
            {
                (failures ??= []).Add(UndefinedColumn(column));
            }
        }

        if (options.Enabled)
        {
            AddActiveKeyFailures(options, ref failures);
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Names an undefined <c>Columns</c> member that code added; the binder leaves out one it cannot read.</summary>
    private static string UndefinedColumn(ProtectedColumn column)
        => $"{nameof(TraconContentProtectionOptions)}.{nameof(TraconContentProtectionOptions.Columns)} contains " +
           $"{((int)column).ToString(CultureInfo.InvariantCulture)}, which is not a {nameof(ProtectedColumn)} value. " +
           $"Known names: {string.Join(", ", Enum.GetNames<ProtectedColumn>())}.";

    private static void AddActiveKeyFailures(TraconContentProtectionOptions options, ref List<string>? failures)
    {
        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
        {
            (failures ??= []).Add(
                $"{nameof(TraconContentProtectionOptions)}.{nameof(TraconContentProtectionOptions.Enabled)} is true, but " +
                $"{nameof(TraconContentProtectionOptions.ActiveKeyId)} is not set.");
        }
        else if (!options.Keys.ContainsKey(options.ActiveKeyId))
        {
            (failures ??= []).Add(
                $"{nameof(TraconContentProtectionOptions)}.{nameof(TraconContentProtectionOptions.ActiveKeyId)} " +
                $"('{options.ActiveKeyId}') has no matching entry in " +
                $"{nameof(TraconContentProtectionOptions.Keys)}.");
        }
    }
}
