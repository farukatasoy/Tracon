using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconSessionOwnershipOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// The empty string is the documented way to say that no policy lets a caller manage another user's sessions. A policy of blanks names a policy that does not exist, which reads the same and was written by mistake.
/// </para>
/// <para>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </para>
/// </remarks>
internal sealed class TraconSessionOwnershipOptionsValidator : IValidateOptions<TraconSessionOwnershipOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconSessionOwnershipOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        OptionText.AddWhenOnlyWhiteSpace(options.ManagementPolicy, nameof(TraconSessionOwnershipOptions), nameof(TraconSessionOwnershipOptions.ManagementPolicy), ref failures);

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
