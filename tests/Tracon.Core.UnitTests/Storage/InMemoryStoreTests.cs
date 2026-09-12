using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Storage;

public sealed class InMemoryAgentDefinitionStoreTests
{
    [Fact]
    public async Task Saving_increments_the_version_and_keeps_history()
    {
        var store = new InMemoryAgentDefinitionStore();

        var first = await store.SaveAsync(TestData.Definition("a") with { Instructions = "first" });
        var second = await store.SaveAsync(TestData.Definition("a") with { Instructions = "second" });

        first.Version.ShouldBe(1);
        second.Version.ShouldBe(2);

        (await store.GetAsync("a"))!.Instructions.ShouldBe("second");
        (await store.ListVersionsAsync("a")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_saved_record_is_marked_with_a_database_origin()
    {
        var store = new InMemoryAgentDefinitionStore();

        var saved = await store.SaveAsync(TestData.Definition("a") with { Origin = AgentDefinitionOrigin.Code });

        saved.Origin.ShouldBe(AgentDefinitionOrigin.Database);
    }

    [Fact]
    public async Task Version_history_is_sorted_newest_to_oldest()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition("a"));
        await store.SaveAsync(TestData.Definition("a"));
        await store.SaveAsync(TestData.Definition("a"));

        var versions = await store.ListVersionsAsync("a");

        versions.Select(static v => v.Version).ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task Rollback_saves_the_old_version_as_a_new_version()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition("a") with { Instructions = "first" });
        await store.SaveAsync(TestData.Definition("a") with { Instructions = "second" });

        var restored = await store.RollbackAsync("a", version: 1);

        restored.Version.ShouldBe(3);
        restored.Instructions.ShouldBe("first");

        // Rollback does not delete history.
        (await store.ListVersionsAsync("a")).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Rolling_back_to_a_nonexistent_version_throws()
    {
        var store = new InMemoryAgentDefinitionStore();
        await store.SaveAsync(TestData.Definition("a"));

        await Should.ThrowAsync<TraconException>(async () => await store.RollbackAsync("a", version: 99));
    }

    [Fact]
    public async Task Deleting_removes_all_versions()
    {
        var store = new InMemoryAgentDefinitionStore();
        await store.SaveAsync(TestData.Definition("a"));
        await store.SaveAsync(TestData.Definition("a"));

        (await store.DeleteAsync("a")).ShouldBeTrue();
        (await store.GetAsync("a")).ShouldBeNull();
        (await store.DeleteAsync("a")).ShouldBeFalse();
    }
}

public sealed class InMemoryRunStoreTests
{
    [Fact]
    public async Task Events_are_read_by_sequence_number()
    {
        var store = new InMemoryRunStore();
        var runId = TraconId.NewId();

        await store.StartRunAsync(NewRun(runId));

        for (var i = 0; i < 5; i++)
        {
            await store.AppendEventAsync(NewEvent(runId, i));
        }

        var sequences = new List<long>();
        await foreach (var runEvent in store.ReadEventsAsync(runId, fromSequence: 2))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public async Task An_event_cannot_be_appended_to_a_run_that_does_not_exist()
    {
        var store = new InMemoryRunStore();

        await Should.ThrowAsync<TraconException>(
            async () => await store.AppendEventAsync(NewEvent(TraconId.NewId(), 0)));
    }

    [Fact]
    public async Task Query_filters_by_agent_name()
    {
        var store = new InMemoryRunStore();

        await store.StartRunAsync(NewRun(TraconId.NewId(), "alpha"));
        await store.StartRunAsync(NewRun(TraconId.NewId(), "beta"));
        await store.StartRunAsync(NewRun(TraconId.NewId(), "alpha"));

        var results = await store.QueryRunsAsync(new RunQuery { AgentName = "alpha" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static run => run.AgentName == "alpha");
    }

    [Fact]
    public async Task Query_sorts_newest_to_oldest()
    {
        var store = new InMemoryRunStore();
        var now = DateTimeOffset.UtcNow;

        await store.StartRunAsync(NewRun(TraconId.NewId()) with { StartedAt = now.AddMinutes(-10) });
        await store.StartRunAsync(NewRun(TraconId.NewId()) with { StartedAt = now });
        await store.StartRunAsync(NewRun(TraconId.NewId()) with { StartedAt = now.AddMinutes(-5) });

        var results = await store.QueryRunsAsync(new RunQuery());

        results[0].StartedAt.ShouldBe(now);
        results[^1].StartedAt.ShouldBe(now.AddMinutes(-10));
    }

    [Fact]
    public async Task When_the_upper_limit_is_exceeded_the_oldest_run_is_dropped()
    {
        var store = new InMemoryRunStore { MaxRuns = 3 };
        var ids = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var id = TraconId.NewId();
            ids.Add(id);
            await store.StartRunAsync(NewRun(id));
        }

        (await store.GetRunAsync(ids[0])).ShouldBeNull();
        (await store.GetRunAsync(ids[4])).ShouldNotBeNull();
    }

    private static RunStartInfo NewRun(Guid id, string agentName = "test-agent")
        => new() { RunId = id, AgentName = agentName, StartedAt = DateTimeOffset.UtcNow };

    private static RunEvent NewEvent(Guid runId, long sequence)
        => new()
        {
            RunId = runId,
            Sequence = sequence,
            Type = RunEventType.MessageDelta,
            Timestamp = DateTimeOffset.UtcNow,
        };
}

public sealed class TraconIdTests
{
    [Fact]
    public void Generated_id_is_version_7()
    {
        var id = TraconId.NewId();

        // The top 4 bits of byte 7 carry the version number (in big-endian representation).
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _).ShouldBeTrue();

        (bytes[6] >> 4).ShouldBe(7);
        (bytes[8] >> 6).ShouldBe(2); // RFC 9562 variant: binary 10
    }

    [Fact]
    public void Ids_are_time_ordered()
    {
        var baseTime = DateTimeOffset.UtcNow;

        var earlier = TraconId.NewId(baseTime);
        var later = TraconId.NewId(baseTime.AddSeconds(1));

        // UUIDv7's text representation preserves time order.
        string.CompareOrdinal(earlier.ToString(), later.ToString()).ShouldBeLessThan(0);
    }

    [Fact]
    public void The_timestamp_can_be_read_back()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var id = TraconId.NewId(timestamp);

        var recovered = TraconId.GetTimestamp(id);

        // Millisecond resolution; sub-units are lost.
        recovered.ToUnixTimeMilliseconds().ShouldBe(timestamp.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void An_id_that_is_not_version_7_is_rejected()
    {
        Should.Throw<ArgumentException>(() => TraconId.GetTimestamp(Guid.Empty));
    }
}
