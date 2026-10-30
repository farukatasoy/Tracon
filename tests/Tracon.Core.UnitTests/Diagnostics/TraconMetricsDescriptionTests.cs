using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The description of an instrument reaches the consumer as OpenTelemetry metadata, so it
/// says what the instrument measures and does not point at a record the consumer never
/// receives.
/// </summary>
public sealed class TraconMetricsDescriptionTests
{
    private static readonly Regex RecordReference = new(
        @"\b(?:[KF]-\d{2,3}|[Pp]hase \d+|docs/)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void The_cost_and_judge_instruments_describe_themselves_in_the_consumers_terms()
    {
        var descriptions = PublishedDescriptions();

        descriptions[TraconDiagnostics.RunCostCounterName].ShouldBe(
            "Cost per run, in currency units. Counts the run's own cost only, never the total of its sub-runs.");
        descriptions[TraconDiagnostics.JudgeCostCounterName].ShouldBe(
            "The OWN cost of a single IRunJudge call. Not included in the scored agent's cost.");
        descriptions[TraconDiagnostics.JudgeScoreHistogramName].ShouldBe(
            "Score given by an IRunJudge, 0-100.");
    }

    [Fact]
    public void No_instrument_description_points_at_the_development_record()
    {
        var offending = PublishedDescriptions()
            .Where(pair => pair.Value is not null && RecordReference.IsMatch(pair.Value))
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToList();

        offending.ShouldBeEmpty();
    }

    private static Dictionary<string, string?> PublishedDescriptions()
    {
        using var meterFactory = new TestMeterFactory();
        var descriptions = new Dictionary<string, string?>(StringComparer.Ordinal);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (ReferenceEquals(instrument.Meter, meterFactory.Meter))
            {
                descriptions[instrument.Name] = instrument.Description;
            }
        };
        listener.Start();

        using var metrics = new TraconMetrics(meterFactory, TenantTaggedMetricsOptions.Create());

        return descriptions;
    }
}
