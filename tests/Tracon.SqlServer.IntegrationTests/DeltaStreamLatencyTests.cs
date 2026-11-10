using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A stream whose store sits behind a SQL Server connection does not wait for that connection once for each chunk. The same
/// 400 chunks go into the same store twice, written before the stream continues and written behind it. Over a connection
/// that answers nothing, the first stops at its first chunk and the second hands over every chunk; once the connection
/// answers, the first has paid a write for each chunk, the second a handful, and both hold every chunk.
/// </summary>
/// <remarks>
/// The database and the statements are real; the connection is held by the test, so no assertion depends on how fast the
/// machine is. The second test paces the stream with a model in real time and compares the two streams' durations, which
/// is a real-time claim and stays one; the times are recorded with <c>TRACON_PROBE_OUT</c> (a file path) for the record of
/// a measurement.
/// </remarks>
public sealed class DeltaStreamLatencyTests(SqlServerFixture fixture)
{
    private static readonly TimeSpan RoundTrip = TimeSpan.FromMilliseconds(2);

    [Fact]
    public async Task A_stream_over_a_held_connection_does_not_wait_for_each_chunk()
    {
        await using var context = SqlServerTestContext.Create(fixture, SqlServerTestContext.NewSchemaName());
        await context.Migrations.ApplyAsync();

        const int Chunks = 400;

        // Written in line: the stream stops at its first chunk, whose write the connection has not answered.
        await using var inLine = await DeltaStreamOverLatency.HoldAsync(context.Runs, behind: false, Chunks);

        await inLine.MeetsTheConnectionAsync();

        inLine.Stream.IsCompleted.ShouldBeFalse("the stream waits for the connection once for each chunk");

        var inLineStored = await inLine.ReleaseAsync();

        // Written behind: the stream hands over every chunk while no write has been answered.
        await using var behind = await DeltaStreamOverLatency.HoldAsync(context.Runs, behind: true, Chunks);

        await WaitUntil.TrueAsync(() => behind.Stream.IsCompleted, "the stream to hand over every chunk while the connection answers nothing");

        var behindStored = await behind.ReleaseAsync();

        foreach (var (stored, behindMode) in new[] { (inLineStored, false), (behindStored, true) })
        {
            stored.Count.ShouldBe(Chunks, $"every chunk is stored when the run is over (behind: {behindMode})");
            stored.Contiguous.ShouldBeTrue($"the sequence numbers have no gap and no repeat (behind: {behindMode})");
        }

        inLineStored.Calls.ShouldBeGreaterThanOrEqualTo(Chunks, "each chunk paid its round trip");
        behindStored.Calls.ShouldBeLessThan(Chunks / 4, "the chunks went to the store together, not one write for each");
    }

    [Fact]
    public async Task A_stream_paced_by_a_model_is_not_slowed_by_the_connection()
    {
        await using var context = SqlServerTestContext.Create(fixture, SqlServerTestContext.NewSchemaName());
        await context.Migrations.ApplyAsync();

        const int Chunks = 150;
        var interval = TimeSpan.FromMilliseconds(5);

        var inLine = await DeltaStreamOverLatency.RunAsync(context.Runs, behind: false, Chunks, RoundTrip, interval);
        var behind = await DeltaStreamOverLatency.RunAsync(context.Runs, behind: true, Chunks, RoundTrip, interval);

        DeltaStreamOverLatency.Record(inLine.Describe("SQL Server paced 5ms", RoundTrip));
        DeltaStreamOverLatency.Record(behind.Describe("SQL Server paced 5ms", RoundTrip));

        behind.Stored.ShouldBe(Chunks);
        behind.Contiguous.ShouldBeTrue();
        behind.StreamTime.ShouldBeLessThan(inLine.StreamTime); // real time: a stream paced in real time by a model, over a real connection
    }
}
