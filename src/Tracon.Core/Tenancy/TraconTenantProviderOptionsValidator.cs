using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconTenantProviderOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// A prefix of blanks is a boundary no key passes: every tenant provider key reference is refused, with a message about the key and not about the setting.
/// </para>
/// <para>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </para>
/// </remarks>
internal sealed class TraconTenantProviderOptionsValidator : IValidateOptions<TraconTenantProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconTenantProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        OptionText.AddWhenOnlyWhiteSpace(options.AllowedConfigurationPrefix, nameof(TraconTenantProviderOptions), nameof(TraconTenantProviderOptions.AllowedConfigurationPrefix), ref failures);

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
