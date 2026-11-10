using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A run that is retried continues its event stream instead of
/// restarting it.
/// </summary>
/// <remarks>
/// <para>
/// A lease takeover keeps the run's identity — <c>StartRunAsync</c> upserts the
/// row rather than opening a second one — but the event stream has no upsert.
/// It is append-only with a unique <c>(run_id, seq)</c> key, so the second
/// attempt's <c>seq = 0</c> collided with the first attempt's. The writer
/// disabled itself on that collision and then skipped every later store write,
/// including the completion. The work finished, the job endpoint said
/// Completed, and the run endpoint said Running forever — two endpoints
/// contradicting each other about the same work, with the cost of a real
/// provider call recorded nowhere.
/// </para>
/// <para>
/// Not an edge case: it was the deterministic outcome of EVERY takeover, which
/// is the whole point of leasing.
/// </para>
/// </remarks>
public sealed class RunEventSequenceResumeTests
{
    [Fact]
    public async Task A_second_attempt_continues_the_event_stream_and_still_completes_the_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var runId = TraconId.NewId();

        await RunOnceAsync(store, runId);
        var afterFirst = await ReadSequencesAsync(store, runId);

        // The takeover: the same run id, a brand new writer, exactly as a second
        // worker builds one after the lease expires.
        await RunOnceAsync(store, runId);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Id.ShouldBe(runId);
        run.Status.ShouldBe(RunStatus.Completed);
        run.CompletedAt.ShouldNotBeNull();

        var sequences = await ReadSequencesAsync(store, runId);
        sequences.Count.ShouldBeGreaterThan(afterFirst.Count);
        sequences.ShouldBeUnique();
        sequences.ShouldBe([.. sequences.Order()]);
    }

    [Fact]
    public async Task A_first_attempt_still_starts_at_zero()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var runId = TraconId.NewId();

        await RunOnceAsync(store, runId);

        (await ReadSequencesAsync(store, runId))[0].ShouldBe(0);
    }

    [Fact]
    public async Task A_run_whose_id_the_recorder_made_does_not_ask_the_store_for_a_sequence_that_cannot_exist()
    {
        // The id is new, so no attempt before this one can have written an event under it. The question costs a round-trip
        // to the store on every run, and over a network that is paid before the first byte of the answer.
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));

        await RunOnceAsync(store, runId: null);

        store.LastSequenceLookups.ShouldBe(0);
        (await store.ReadAsync(await OnlyRunIdAsync(store)))[0].Sequence.ShouldBe(0, "the stream still starts at zero without the question");
    }

    [Fact]
    public async Task A_run_whose_id_the_caller_chose_still_asks_because_the_id_may_be_a_retry()
    {
        // A job worker hands the same id to every attempt; only the store knows whether an earlier one wrote events.
        var store = new GroupSpyRunStore(new InMemoryRunStore(tenantContext: new FixedTenantContext()));

        await RunOnceAsync(store, TraconId.NewId());

        store.LastSequenceLookups.ShouldBe(1);
    }

    private static async Task<Guid> OnlyRunIdAsync(GroupSpyRunStore store)
        => (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem().Id;

    private static async Task RunOnceAsync(IRunStore store, Guid? runId)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(new FakeChatClient())),
            TestData.Registry());

        var agent = TestRunRecording.Agent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance);

        await agent.RunAsync(
            "hello",
            options: runId is { } chosen ? new TraconRunOptions { RunId = chosen } : null,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<List<long>> ReadSequencesAsync(InMemoryRunStore store, Guid runId)
    {
        var sequences = new List<long>();

        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            sequences.Add(runEvent.Sequence);
        }

        return sequences;
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
