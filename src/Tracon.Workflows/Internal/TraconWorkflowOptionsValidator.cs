using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconWorkflowOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// Every workflow run arms <see cref="TraconWorkflowOptions.RunTimeout"/> as
/// its deadline with a <see cref="CancellationTokenSource"/>, which refuses a
/// negative value or more than about 49.7 days. Out of range, every run threw when it
/// started - long after the host did. Zero cancelled every run at once. The value is
/// checked only while workflows are on; nothing arms it otherwise.
/// </para>
/// <para>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </para>
/// </remarks>
internal sealed class TraconWorkflowOptionsValidator : IValidateOptions<TraconWorkflowOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Enabled && !TimerPeriod.IsValid(options.RunTimeout))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.RunTimeout)}",
                options.RunTimeout,
                $" when {nameof(TraconWorkflowOptions.Enabled)} is on"));
        }

        // Checked whether or not the engine is on: the cap guards a definition SAVE,
        // which does not need the engine.
        if (options.MaxNodeSettingsLength < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxNodeSettingsLength)} must be at least 1 " +
                $"(bytes of a node's settings JSON). Actual value: {options.MaxNodeSettingsLength}.");
        }

        return ValidateOptionsResult.Success;
    }
}
