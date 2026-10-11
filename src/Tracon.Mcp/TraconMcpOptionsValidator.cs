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

        List<string>? failures = null;

        // Discovery waits RefreshInterval between passes with Task.Delay, inside
        // a hosted service. A negative value threw there and stopped the host
        // after startup; zero turned the loop into a busy loop against every
        // configured server (F-278). While discovery is off no loop runs.
        if (options.Enabled && !TimerPeriod.IsValid(options.RefreshInterval))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.RefreshInterval)}",
                options.RefreshInterval,
                $" when {nameof(TraconMcpOptions.Enabled)} is on"));
        }

        // F-280: every connect, list, resource read, test connection and OAuth start
        // arms ConnectionTimeout with CancelAfter. Out of range, the connect and the
        // refresh threw outside their per-server handling and aborted discovery for
        // every tenant; zero made every server unreachable. The definition validator
        // and the test-connection endpoint use it while discovery is off too.
        if (!TimerPeriod.IsValid(options.ConnectionTimeout))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.ConnectionTimeout)}",
                options.ConnectionTimeout));
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
