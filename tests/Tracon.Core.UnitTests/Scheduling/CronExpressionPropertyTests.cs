using CsCheck;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// Properties of <see cref="CronExpression"/> over generated expressions: whatever parses, the next run can be computed.
/// </summary>
/// <remarks>
/// <para>
/// A schedule is saved once and read for years. An expression that parses but cannot name a next run turned a worker
/// loop into an exception (<c>0 0 30 2 *</c>, the 30th of February, was accepted and then failed when the next run was
/// computed). The generators build whole expressions from the syntax the parser accepts, plus the digits and symbols
/// next to it, so both the refusals and the accepted forms are exercised. A failure prints the seed that reproduces it.
/// </para>
/// <para>
/// The oracle is the property, not a second implementation: an accepted expression returns a time after the
/// starting time, in a zone with daylight saving time, a zone with a half-hour offset, and UTC.
/// </para>
/// </remarks>
public sealed class CronExpressionPropertyTests
{
    private static readonly string[] ZoneIds =
    [
        "UTC",
        "America/New_York",
        "Europe/London",
        "Australia/Lord_Howe",
        "Asia/Kolkata",
    ];

    // Each position gets values in its own range, with the edges, so the combinations that matter (the 30th of February,
    // the 31st of a month with 30 days, a step that starts late) are common instead of one in a million.
    private static Gen<string> FieldOf(int min, int max)
    {
        var value = Gen.Int[min, max].Select(static n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var edge = Gen.OneOf<int>(min, max, max - 1).Select(static n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var outOfRange = Gen.OneOf<int>(min - 1, max + 1).Select(static n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return Gen.Frequency(
            (4, Gen.Const("*")),
            (6, value),
            (4, edge),
            (1, outOfRange),
            (3, Gen.Int[min, max].Select(Gen.Int[min, max]).Select(static (a, b) => $"{a}-{b}")),
            (3, Gen.Int[min, max].Select(Gen.Int[1, 30]).Select(static (a, step) => $"{a}/{step}")),
            (2, Gen.Int[1, 30].Select(static step => $"*/{step}")),
            (2, Gen.Int[min, max].Select(Gen.Int[min, max]).Select(static (a, b) => $"{a},{b}")),
            (1, Gen.OneOf<string>("L", "?", "", "-", "*/0", "1-", "/5", "7#2", "W")));
    }

    private static readonly Gen<string> Expression = Gen.Select(
        FieldOf(0, 59),
        FieldOf(0, 23),
        FieldOf(1, 31),
        FieldOf(1, 12),
        FieldOf(0, 7),
        static (minute, hour, dayOfMonth, month, dayOfWeek) => $"{minute} {hour} {dayOfMonth} {month} {dayOfWeek}");

    [Fact]
    public void An_accepted_expression_always_has_a_next_run_after_the_start()
    {
        // Seconds since the epoch, from 2000-01-01 to 2060-01-01: drawn from the range itself, so no draw is thrown away.
        var instants = Gen.Long[946_684_800L, 2_840_140_800L].Select(static seconds => DateTimeOffset.FromUnixTimeSeconds(seconds));

        Gen.Select(Expression, instants, Gen.OneOf<string>(ZoneIds))
            .Sample(
                static (text, from, zoneId) =>
                {
                    if (!CronExpression.TryParse(text, out var cron) || cron is null)
                    {
                        return;
                    }

                    var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                    var next = cron.GetNextOccurrence(from, zone);

                    (next > from).ShouldBeTrue($"'{text}' in {zoneId} after {from:O} gave {next:O}");
                },
                iter: 2_000);
    }

    [Fact]
    public void Parse_and_TryParse_agree_on_every_expression()
    {
        Expression.Sample(
            static text =>
            {
                var accepted = CronExpression.TryParse(text, out _);

                if (accepted)
                {
                    Should.NotThrow(() => CronExpression.Parse(text));
                }
                else
                {
                    Should.Throw<FormatException>(() => CronExpression.Parse(text));
                }
            },
            iter: 5_000);
    }
}
