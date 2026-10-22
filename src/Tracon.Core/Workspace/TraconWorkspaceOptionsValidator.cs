using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconWorkspaceOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconWorkspaceOptionsValidator : IValidateOptions<TraconWorkspaceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.MaxFileBytes < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWorkspaceOptions)}.{nameof(TraconWorkspaceOptions.MaxFileBytes)} " +
                $"must be at least 1. Actual value: {options.MaxFileBytes}.");
        }

        if (options.MaxSessionBytes < options.MaxFileBytes)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWorkspaceOptions)}.{nameof(TraconWorkspaceOptions.MaxSessionBytes)} " +
                $"must not be smaller than {nameof(TraconWorkspaceOptions.MaxFileBytes)}: a session could not hold one full file. " +
                $"Actual values: {options.MaxSessionBytes} and {options.MaxFileBytes}.");
        }

        if (options.MaxTenantBytes < options.MaxSessionBytes)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWorkspaceOptions)}.{nameof(TraconWorkspaceOptions.MaxTenantBytes)} " +
                $"must not be smaller than {nameof(TraconWorkspaceOptions.MaxSessionBytes)}: a tenant could not hold one full session. " +
                $"Actual values: {options.MaxTenantBytes} and {options.MaxSessionBytes}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
