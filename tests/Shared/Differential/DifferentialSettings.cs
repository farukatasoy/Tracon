using System.Globalization;

namespace Tracon.Tests.Differential;

/// <summary>
/// The knobs of the differential and race suites. Every default is the budget of a normal CI run; the environment
/// raises it for a long periodic run.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>TRACON_DIFF_ITER</c> - sequences per state machine (default: the per-provider value the suite passes).</item>
/// <item><c>TRACON_DIFF_STEPS</c> - longest sequence (default 24).</item>
/// <item><c>TRACON_DIFF_SECONDS</c> - run each machine for this many seconds instead of a fixed count.</item>
/// <item><c>TRACON_DIFF_SEED</c> - replay one failing sequence; CsCheck prints the value with every failure.</item>
/// </list>
/// A long run sets <c>TRACON_DIFF_SECONDS</c> and <c>TRACON_DIFF_STEPS</c> and filters the test class by name
/// (<c>--filter-class "*StoreDifferential*"</c>) on the compiled test executable.
/// </remarks>
internal static class DifferentialSettings
{
    private const int DefaultSteps = 24;

    /// <summary>Gets the sequence count for one machine.</summary>
    /// <param name="providerDefault">The count to use when the environment does not set one.</param>
    /// <returns>The iteration count.</returns>
    public static long Iterations(long providerDefault)
        => Read("TRACON_DIFF_ITER") is { } text && long.TryParse(text, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : providerDefault;

    /// <summary>Gets the longest generated sequence.</summary>
    public static int MaxSteps
        => Read("TRACON_DIFF_STEPS") is { } text && int.TryParse(text, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : DefaultSteps;

    /// <summary>Gets the time budget in seconds, or -1 when a fixed iteration count applies.</summary>
    public static int Seconds
        => Read("TRACON_DIFF_SECONDS") is { } text && int.TryParse(text, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : -1;

    /// <summary>Gets the seed that replays one failing sequence, or <see langword="null"/>.</summary>
    public static string? Seed => Read("TRACON_DIFF_SEED");

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
