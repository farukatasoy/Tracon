using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconMcpOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconMcpOptionsValidator : IValidateOptions<TraconMcpOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Discovery waits RefreshInterval between passes with Task.Delay, inside
        // a hosted service. A negative value threw there and stopped the host
        // after startup; zero turned the loop into a busy loop against every
        // configured server (F-278). While discovery is off no loop runs.
        if (options.Enabled && !TimerPeriod.IsValid(options.RefreshInterval))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.RefreshInterval)}",
                options.RefreshInterval,
                $" when {nameof(TraconMcpOptions.Enabled)} is on"));
        }

        return ValidateOptionsResult.Success;
    }
}
