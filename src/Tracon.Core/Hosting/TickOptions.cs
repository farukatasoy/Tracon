using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Reads the options of a background loop at the start of a tick, and tells the loop to skip the tick when it cannot.
/// </summary>
/// <typeparam name="T">The options type.</typeparam>
/// <remarks>
/// <para>
/// A host that adds a configuration source that reloads makes <see cref="IOptionsMonitor{TOptions}"/> recompute on every
/// change. A value the options validation refuses makes the monitor throw <see cref="OptionsValidationException"/> on
/// every read until a valid value replaces it. A loop that reads its options at the top of the tick, outside any
/// <see langword="try"/>, lets that exception end the loop, and a <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// whose <c>ExecuteAsync</c> throws stops the host by default.
/// </para>
/// <para>
/// So the loop reads through this type. A read that fails skips the tick, the timer keeps running, and the first tick
/// that can read a valid value works again. The loop does not carry on with an earlier value: it holds none, and running
/// on a value the operator has since replaced would make a reload mean something different from one loop to the next.
/// Any other exception is not caught here; it is a defect and not a configuration.
/// </para>
/// <para>
/// The warning is written once for a refusal that repeats, because a tick comes every few seconds and the same text
/// every few seconds would bury the log. A different refusal, or the same one after a valid read, is written again.
/// </para>
/// <para>
/// One instance serves one loop and is not thread safe: only the loop's own thread of control calls it.
/// </para>
/// </remarks>
/// <param name="monitor">The monitor to read.</param>
/// <param name="logger">Where a skipped tick is reported; <see langword="null"/> reports nowhere.</param>
/// <param name="service">What the loop does, in words a log line can carry: "approval expiration scan".</param>
internal sealed class TickOptions<T>(IOptionsMonitor<T> monitor, ILogger? logger, string service)
    where T : class
{
    private string? _lastRefusal;

    /// <summary>Reads the current options.</summary>
    /// <param name="options">The options when the read succeeded; <see langword="null"/> otherwise.</param>
    /// <returns><see langword="true"/> when the tick can go on; <see langword="false"/> when it is to be skipped.</returns>
    public bool TryRead([NotNullWhen(true)] out T? options)
    {
        try
        {
            options = monitor.CurrentValue;
            _lastRefusal = null;

            return true;
        }
        catch (OptionsValidationException exception)
        {
            options = null;

            if (!string.Equals(_lastRefusal, exception.Message, StringComparison.Ordinal))
            {
                _lastRefusal = exception.Message;

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "The options of the {Service} cannot be read, so its tick is skipped. The loop keeps running and works again when a valid value is configured.",
                        service);
                }
            }

            return false;
        }
    }
}
