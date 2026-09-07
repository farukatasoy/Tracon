using System.Globalization;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Text that leaves the process — a persisted run error, a ProblemDetails body,
/// a tool result handed back to the model, a CLI line a pipeline greps — formats
/// its numbers with the invariant culture, never the machine's.
/// </summary>
/// <remarks>
/// <para>
/// Found on a tr-TR machine while running phase 153's sample-app check:
/// <c>agentprism eval</c> printed "in 3,7 s". The decimal comma is only the
/// visible half; the same class writes "0,004500" into a stored run error and
/// "%50" into a ProblemDetails body, so the same deployment reads differently
/// depending on which machine produced the row.
/// </para>
/// <para>
/// The analyzer does <strong>not</strong> catch this. CA1305 looks for
/// <c>string.Format</c>/<c>ToString</c> without an <c>IFormatProvider</c>; an
/// interpolated string with a format specifier compiles to
/// <c>DefaultInterpolatedStringHandler</c> instead and is invisible to it
/// (measured: raising CA1305 to <c>warning</c> across the solution reported
/// zero hits while the sites below existed). The guard is therefore
/// behavioural, not an analyzer rule.
/// </para>
/// <para>
/// Which specifiers are actually at risk was measured, not assumed, on
/// <c>tr-TR</c>: <c>0.0</c> → "3,7" and <c>0.000000</c> → "1,500000" (decimal
/// separator), <c>P0</c> → "%50" against the invariant "50 %". <c>F0</c> and
/// <c>0</c> are <strong>not</strong> at risk — neither groups digits in any
/// culture, so the three <c>:F0</c> sites first suspected here were left
/// alone. Three real sites remain: <see cref="AgentRunBudget"/> (covered
/// below), <c>PreflightGate</c> (<c>P0</c>) and <c>EvalCommand</c>'s duration
/// line. The latter two live behind a host and a process boundary; covering
/// them needs a culture-scoped test that would leak into the parallel tests
/// beside it, so they are fixed at the source and stated as uncovered rather
/// than guarded by a test that proves nothing.
/// </para>
/// </remarks>
public sealed class InvariantShippedTextTests
{
    [Fact]
    public void An_exhausted_cost_budget_reads_the_same_on_every_machine()
    {
        // The message is persisted as the run's error text, so a comma here
        // makes the same row parse differently depending on where it was made.
        using var _ = new CultureScope("tr-TR");

        var budget = new AgentRunBudget { MaxTotalCost = 1.5m };
        budget.RecordUsage(0, 1.5m);

        var message = budget.DescribeModelCallExhaustion();

        message.ShouldContain("1.500000/1.500000");
        message.ShouldNotContain("1,500000");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
