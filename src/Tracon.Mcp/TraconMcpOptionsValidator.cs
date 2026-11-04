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
        // configured server. While discovery is off no loop runs.
        if (options.Enabled && !TimerPeriod.IsValid(options.RefreshInterval))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.RefreshInterval)}",
                options.RefreshInterval,
                $" when {nameof(TraconMcpOptions.Enabled)} is on"));
        }

        // Every connect, list, resource read, test connection and OAuth start
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

        // A limit of zero or less refuses every response, the handshake included, and the server reads as unreachable
        // with no word of the limit.
        if (options.MaxResponseBytes < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.MaxResponseBytes)} must be at least 1. " +
                $"Actual value: {options.MaxResponseBytes}.");
        }

        // Zero or less kept no tool (the loop stopped at the first one, with a warning that quoted the limit) and trimmed
        // every resource to nothing, with no word at start. The two limits that count resources and prompts document that
        // a value below one keeps none, so a host can say "no resources" with them; these three have no such meaning.
        AddWhenBelowOne(options.MaxToolsPerServer, nameof(TraconMcpOptions.MaxToolsPerServer), ref failures);
        AddWhenBelowOne(options.MaxResourceBytesPerResource, nameof(TraconMcpOptions.MaxResourceBytesPerResource), ref failures);
        AddWhenBelowOne(options.MaxResourceBytesTotal, nameof(TraconMcpOptions.MaxResourceBytesTotal), ref failures);

        // The documentation says "must be greater than zero" and nothing checked it: the cache refused the entry or kept it
        // for no time, and the server asked for authorization again at every refresh, with no word of the setting.
        if (options.OAuthTokenSlidingLifetime <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.OAuthTokenSlidingLifetime)} must be greater than zero. " +
                $"Actual value: {options.OAuthTokenSlidingLifetime}.");
        }

        // Compared with the age of the last good listing; it arms no timer.
        if (options.StaleToolRetention < TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.StaleToolRetention)} must not be negative. " +
                $"Actual value: {options.StaleToolRetention}.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddWhenBelowOne(int value, string memberName, ref List<string>? failures)
    {
        if (value < 1)
        {
            (failures ??= []).Add($"{nameof(TraconMcpOptions)}.{memberName} must be at least 1. Actual value: {value}.");
        }
    }
}
