using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The retention sweep of the <c>traces</c> target on Sqlite: a trace is the run's, so the cutoff decides on the run's own
/// start and never on the start of a trace another run shares.
/// </summary>
/// <remarks>
/// Two runs of one tenant carry the same W3C trace id. When the header was keyed by that id, it was one row whose
/// <c>started_at</c> was the oldest start of the two, so the sweep removed the newer run's spans at the older run's cutoff.
/// Every moment is a fixed timestamp; nothing waits.
/// </remarks>
public sealed class TraceRetentionTests(SqliteFixture fixture)
{
    private const string SharedTraceId = "0af7651916cd43dd8448eb211c80319c";

    private static readonly DateTimeOffset Old = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Recent = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Cutoff = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retention_removes_only_the_trace_of_the_run_that_started_before_the_cutoff()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var oldRun = Guid.NewGuid();
        var recentRun = Guid.NewGuid();
        await context.Runs.StartRunAsync(TestData.Run(oldRun));
        await context.Runs.StartRunAsync(TestData.Run(recentRun));

        await WriteAsync(context, oldRun, "old", Old);
        await WriteAsync(context, recentRun, "recent", Recent);

        var deleted = await context.RetentionData.DeleteBatchAsync(RetentionTargets.Traces, tenantId: null, Cutoff, batchSize: 100);

        deleted.ShouldBe(1);
        (await context.Traces.GetTraceByRunAsync(oldRun)).ShouldBeNull();

        var kept = await context.Traces.GetTraceByRunAsync(recentRun);

        kept.ShouldNotBeNull();
        kept.TraceId.ShouldBe(SharedTraceId);
        kept.Spans.Select(static span => span.Name).Order(StringComparer.Ordinal).ToArray().ShouldBe(["recent-child", "recent-root"]);

        // The spans of the removed trace went with it and nothing else did.
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.TablePrefix}spans;")).ShouldBe(2);
    }

    private static async Task WriteAsync(SqliteTestContext context, Guid runId, string name, DateTimeOffset startedAt)
        => await context.Traces.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = SharedTraceId,
            TenantId = "default",
            RunId = runId,
            Spans =
            [
                new TraceSpan { Id = TraconId.NewId(), SpanId = Guid.NewGuid().ToString("N")[..16], Name = $"{name}-root", StartedAt = startedAt, EndedAt = startedAt.AddSeconds(2) },
                new TraceSpan { Id = TraconId.NewId(), SpanId = Guid.NewGuid().ToString("N")[..16], Name = $"{name}-child", StartedAt = startedAt.AddSeconds(1), EndedAt = startedAt.AddSeconds(2) },
            ],
        });
}
