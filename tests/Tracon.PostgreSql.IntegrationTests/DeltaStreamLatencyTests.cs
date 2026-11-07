using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// A stream whose store sits behind a PostgreSQL connection with a round trip of two milliseconds does not wait for that
/// round trip once for each chunk. The same 400 chunks go into the same store twice, written before the stream continues
/// and written behind it; the second must hold the stream up for a fraction of the first, and hold every chunk when the
/// run is over.
/// </summary>
/// <remarks>
/// The round trip is added in front of every write; the database and the statements are real. The times are recorded with
/// <c>TRACON_PROBE_OUT</c> (a file path) for the record of a measurement; the assertion is a ratio far from any noise: 400
/// chunks at two milliseconds cannot be written one by one in less than 800 ms.
/// </remarks>
public sealed class DeltaStreamLatencyTests(PostgresFixture fixture)
{
    private static readonly TimeSpan RoundTrip = TimeSpan.FromMilliseconds(2);

    [Fact]
    public async Task A_stream_over_a_two_millisecond_connection_does_not_wait_for_each_chunk()
    {
        await using var context = PostgresTestContext.Create(fixture, PostgresTestContext.NewSchemaName());
        await context.Migrations.ApplyAsync();

        const int Chunks = 400;

        var inLine = await DeltaStreamOverLatency.RunAsync(context.Runs, behind: false, Chunks, RoundTrip);
        var behind = await DeltaStreamOverLatency.RunAsync(context.Runs, behind: true, Chunks, RoundTrip);

        DeltaStreamOverLatency.Record(inLine.Describe("PostgreSQL", RoundTrip));
        DeltaStreamOverLatency.Record(behind.Describe("PostgreSQL", RoundTrip));

        foreach (var result in new[] { inLine, behind })
        {
            result.Stored.ShouldBe(Chunks, $"every chunk is stored when the run is over (behind: {result.Behind})");
            result.Contiguous.ShouldBeTrue($"the sequence numbers have no gap and no repeat (behind: {result.Behind})");
        }

        inLine.StreamTime.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(Chunks * 2), "each chunk paid its round trip");
        behind.StreamTime.ShouldBeLessThan(inLine.StreamTime / 4);
    }

    [Fact]
    public async Task A_stream_paced_by_a_model_is_not_slowed_by_the_connection()
    {
        await using var context = PostgresTestContext.Create(fixture, PostgresTestContext.NewSchemaName());
        await context.Migrations.ApplyAsync();

        const int Chunks = 150;
        var interval = TimeSpan.FromMilliseconds(5);

        var inLine = await DeltaStreamOverLatency.RunAsync(context.Runs, behind: false, Chunks, RoundTrip, interval);
        var behind = await DeltaStreamOverLatency.RunAsync(context.Runs, behind: true, Chunks, RoundTrip, interval);

        DeltaStreamOverLatency.Record(inLine.Describe("PostgreSQL paced 5ms", RoundTrip));
        DeltaStreamOverLatency.Record(behind.Describe("PostgreSQL paced 5ms", RoundTrip));

        behind.Stored.ShouldBe(Chunks);
        behind.Contiguous.ShouldBeTrue();
        behind.StreamTime.ShouldBeLessThan(inLine.StreamTime);
    }
}
