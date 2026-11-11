using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Tracon.Tests.Common;

/// <summary>
/// Condition waits for tests: poll the signal a test depends on instead of
/// sleeping a fixed time and hoping the signal arrived.
/// </summary>
/// <remarks>
/// <para>
/// A fixed <c>Task.Delay</c> before a POSITIVE assertion is a claim
/// about the machine: under a loaded full-solution run the awaited state
/// arrives later, and the test fails although the product is right. Polling
/// removes the claim - the wait ends the moment the condition holds.
/// </para>
/// <para>
/// 🚨 The timeout only bounds a failure; it is NOT a performance budget. When
/// the condition already holds, a long limit costs nothing. A short one says
/// "this machine is fast", which a loaded run proves false - the second shape
/// of the same defect (<c>.md</c>). The
/// default is therefore generous, and a caller narrows it only when the
/// timeout itself is what the test asserts.
/// </para>
/// <para>
/// When no description is passed, the condition's own source text is used, so
/// a timeout still says what never became true.
/// </para>
/// <para>
/// Linked into every test project by <c>tests/Directory.Build.props</c>; one
/// copy replaces the private poll loops each test class used to carry.
/// </para>
/// </remarks>
internal static class WaitUntil
{
    /// <summary>The default bound on a failure. Not a performance budget - see the class remarks.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The pause between two probes.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>The least time a probe gets, so one started at the bound can still answer.</summary>
    private static readonly TimeSpan MinimumProbeTime = TimeSpan.FromMilliseconds(50);

    /// <summary>Waits until <paramref name="condition"/> returns <see langword="true"/>.</summary>
    /// <param name="condition">The signal to wait for. An exception it throws ends the wait.</param>
    /// <param name="description">What the wait is for, used in the timeout message; the condition's source text when omitted.</param>
    /// <param name="timeout">The failure bound; <see cref="DefaultTimeout"/> when omitted.</param>
    /// <param name="cancellationToken">Ends the wait early; the current test's token when omitted.</param>
    /// <exception cref="TimeoutException">The condition did not hold within the bound.</exception>
    public static Task TrueAsync(
        Func<bool> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => PollAsync(() => Task.FromResult(condition()), static done => done, description, describe: null, timeout, cancellationToken);

    /// <summary>Waits until the asynchronous <paramref name="condition"/> returns <see langword="true"/>.</summary>
    /// <param name="condition">The signal to wait for. An exception it throws ends the wait.</param>
    /// <param name="description">What the wait is for, used in the timeout message; the condition's source text when omitted.</param>
    /// <param name="timeout">The failure bound; <see cref="DefaultTimeout"/> when omitted.</param>
    /// <param name="cancellationToken">Ends the wait early; the current test's token when omitted.</param>
    /// <exception cref="TimeoutException">The condition did not hold within the bound.</exception>
    public static Task TrueAsync(
        Func<Task<bool>> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => PollAsync(condition, static done => done, description, describe: null, timeout, cancellationToken);

    /// <summary>
    /// Probes a value until <paramref name="accept"/> takes it, and returns it.
    /// The timeout message carries the last value probed.
    /// </summary>
    /// <typeparam name="T">The probed value.</typeparam>
    /// <param name="probe">Reads the current value. An exception it throws ends the wait.</param>
    /// <param name="accept">Whether the value is the one the test waits for.</param>
    /// <param name="description">What the wait is for, used in the timeout message; the acceptance test's source text when omitted.</param>
    /// <param name="timeout">The failure bound; <see cref="DefaultTimeout"/> when omitted.</param>
    /// <param name="cancellationToken">Ends the wait early; the current test's token when omitted.</param>
    /// <returns>The first value <paramref name="accept"/> took.</returns>
    /// <exception cref="TimeoutException">No probed value was accepted within the bound.</exception>
    public static Task<T> ValueAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> accept,
        [CallerArgumentExpression(nameof(accept))] string description = "",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => PollAsync(probe, accept, description, Describe, timeout, cancellationToken);

    private static async Task<T> PollAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> accept,
        string description,
        Func<T, string>? describe,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var limit = timeout ?? DefaultTimeout;
        var token = cancellationToken.CanBeCanceled ? cancellationToken : TestContext.Current.CancellationToken;
        var elapsed = Stopwatch.StartNew();
        var pulseFrom = PulseMonitor.Recorder?.Now ?? 0;
        var polls = 0;
        var longestGap = TimeSpan.Zero;
        var lastPoll = Stopwatch.GetTimestamp();

        while (true)
        {
            token.ThrowIfCancellationRequested();

            if (polls++ > 0)
            {
                var gap = Stopwatch.GetElapsedTime(lastPoll);
                longestGap = gap > longestGap ? gap : longestGap;
            }

            lastPoll = Stopwatch.GetTimestamp();

            // 🚨 The bound and the token reach the probe too. A probe that never
            // returns (a stuck store call, an unanswered request) would otherwise
            // hang the test past both - the one failure a wait exists to report.
            var remaining = limit - elapsed.Elapsed;
            var pending = probe();
            T value;

            try
            {
                value = await pending
                    .WaitAsync(remaining > MinimumProbeTime ? remaining : MinimumProbeTime, token)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException exception) when (!pending.IsCompleted)
            {
                throw new TimeoutException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Waited {limit.TotalSeconds:0.###} s for {description}; the probe itself never returned.{Diagnosis(polls, longestGap, pulseFrom)}"),
                    exception);
            }

            if (accept(value))
            {
                return value;
            }

            if (elapsed.Elapsed >= limit)
            {
                var last = describe is null ? string.Empty : $" Last value: {describe(value)}.";

                throw new TimeoutException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waited {limit.TotalSeconds:0.###} s for {description}; it never happened.{last}{Diagnosis(polls, longestGap, pulseFrom)}"));
            }

            await Task.Delay(PollInterval, token).ConfigureAwait(false); // delay: poll
        }
    }

    /// <summary>
    /// What the wait itself saw, so a timeout says whether the condition never held or the process stood still.
    /// </summary>
    /// <remarks>
    /// A wait that ran 1500 polls with a 20 ms gap between them and no stall waited on a condition that never held. A wait with
    /// a long gap, or a long process stall or pool delay, waited on a machine that was not running it.
    /// </remarks>
    private static string Diagnosis(int polls, TimeSpan longestGap, long pulseFrom)
    {
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $" Polls: {polls}; longest gap between polls: {longestGap.TotalMilliseconds:0} ms");

        if (PulseMonitor.Window(pulseFrom) is { } window)
        {
            text += window.Measurements == 0
                ? "; the process heartbeat recorded nothing inside this wait"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"; longest process stall: {window.LongestStall.TotalMilliseconds:0} ms; longest thread pool delay: {window.LongestPoolDelay.TotalMilliseconds:0} ms");
        }

        return text + ".";
    }

    private static string Describe<T>(T value) => value?.ToString() ?? "null";
}
