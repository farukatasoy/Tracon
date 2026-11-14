namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// The in-memory trace store keeps the newest <see cref="InMemoryTraceStore.MaxTraces"/> runs' traces, one trace per run.
/// </summary>
/// <remarks>
/// The trace is the run's, so the limit counts runs. Runs that share a W3C trace id are separate traces and are trimmed
/// one by one, oldest first; a run written again keeps the place of its first write.
/// </remarks>
public sealed class InMemoryTraceStoreTrimTests
{
    private const string SharedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task Runs_that_share_a_trace_id_are_trimmed_one_trace_at_a_time_oldest_first()
    {
        var store = new InMemoryTraceStore { MaxTraces = 2 };
        var oldest = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var newest = Guid.NewGuid();

        await store.WriteSpansAsync(Batch(oldest, "oldest"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(middle, "middle"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(newest, "newest"), TestContext.Current.CancellationToken);

        (await store.GetTraceByRunAsync(oldest, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await store.GetTraceByRunAsync(middle, TestContext.Current.CancellationToken)).ShouldNotBeNull()
            .Spans.ShouldHaveSingleItem().Name.ShouldBe("middle");
        (await store.GetTraceByRunAsync(newest, TestContext.Current.CancellationToken)).ShouldNotBeNull()
            .Spans.ShouldHaveSingleItem().Name.ShouldBe("newest");
    }

    [Fact]
    public async Task A_run_written_again_is_one_trace_and_does_not_use_a_second_place()
    {
        var store = new InMemoryTraceStore { MaxTraces = 2 };
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await store.WriteSpansAsync(Batch(first, "first-1"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(first, "first-2"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(second, "second"), TestContext.Current.CancellationToken);

        // Three writes, two runs: nothing is over the limit, so nothing is dropped.
        (await store.GetTraceByRunAsync(first, TestContext.Current.CancellationToken)).ShouldNotBeNull().Spans.Count.ShouldBe(2);
        (await store.GetTraceByRunAsync(second, TestContext.Current.CancellationToken)).ShouldNotBeNull().Spans.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_run_that_was_trimmed_and_is_written_again_is_not_trimmed_by_the_place_of_its_first_write()
    {
        // The queue holds a run once, at its first write. A run that is written twice and then trimmed must not leave a
        // second entry behind: when the run comes back, that entry would trim it again at once and keep an older run.
        var store = new InMemoryTraceStore { MaxTraces = 2 };
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await store.WriteSpansAsync(Batch(first, "first-1"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(first, "first-2"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(second, "second"), TestContext.Current.CancellationToken);
        await store.WriteSpansAsync(Batch(third, "third"), TestContext.Current.CancellationToken);

        (await store.GetTraceByRunAsync(first, TestContext.Current.CancellationToken)).ShouldBeNull("the oldest run is trimmed");

        await store.WriteSpansAsync(Batch(first, "first-again"), TestContext.Current.CancellationToken);

        (await store.GetTraceByRunAsync(first, TestContext.Current.CancellationToken)).ShouldNotBeNull()
            .Spans.ShouldHaveSingleItem().Name.ShouldBe("first-again");
        (await store.GetTraceByRunAsync(second, TestContext.Current.CancellationToken)).ShouldBeNull("the run that was oldest after the first trim goes next");
        (await store.GetTraceByRunAsync(third, TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    private static TraceSpanBatch Batch(Guid runId, string name)
        => new()
        {
            TraceId = SharedTraceId,
            TenantId = "default",
            RunId = runId,
            Spans =
            [
                new TraceSpan
                {
                    Id = TraconId.NewId(),
                    SpanId = Guid.NewGuid().ToString("N")[..16],
                    Name = name,
                    StartedAt = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
                },
            ],
        };
}
