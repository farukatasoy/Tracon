using Tracon.Testing.Contracts.Storage;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// The concurrent-writer cases of the store contracts really run their calls at the same time.
/// </summary>
/// <remarks>
/// <para>
/// A contract case that lets calls start one after the other on one thread cannot turn a store that looks and then writes
/// red: against the in-memory stores, with the store made non-atomic, six such cases caught it 0 times in 20. They are rounds
/// of callers released together now (<c>SimultaneousCalls</c>), and 20 of 20 runs catch the same store.
/// </para>
/// <para>
/// This is what keeps them that way. Each case runs on an in-memory store wrapped in <see cref="OverlapProbe{T}"/>, and the test
/// fails if no two calls were ever inside the store at once. The fixtures derive from the real contract the way a third-party
/// store would, but they are <see langword="private"/> nested types, so xunit never runs them as tests of their own; this
/// class calls their inherited <c>[Fact]</c> methods directly.
/// </para>
/// </remarks>
public sealed class ConcurrentWriterContractOverlapTests
{
    [Fact]
    public Task The_session_create_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new SessionFixture(), static contract => contract.TryCreateAsync_only_one_concurrent_call_with_the_same_id_wins());

    [Fact]
    public Task The_session_update_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new SessionFixture(), static contract => contract.TryUpdateAsync_only_one_concurrent_call_from_the_same_version_wins());

    [Fact]
    public Task The_run_score_same_name_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new RunScoreFixture(), static contract => contract.Concurrent_writes_of_the_SAME_name_leave_exactly_one_row());

    [Fact]
    public Task The_run_score_no_message_id_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new RunScoreFixture(), static contract => contract.Concurrent_writes_with_null_and_empty_MessageId_leave_exactly_one_row());

    [Fact]
    public Task The_workflow_response_answers_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new ResponseFixture(), static contract => contract.Concurrent_answers_to_one_request_leave_exactly_one_claim());

    [Fact]
    public Task The_workflow_response_replacements_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new ResponseFixture(), static contract => contract.Concurrent_replacements_of_one_record_leave_exactly_one_claim());

    [Fact]
    public async Task A_case_that_starts_its_calls_one_after_the_other_is_caught_by_the_probe()
    {
        // The probe's own check: calls awaited one at a time never overlap, whatever the store.
        var store = OverlapProbe.Wrap<ISessionStore>(new InMemorySessionStore(), out var probe);

        for (var i = 0; i < 8; i++)
        {
            await store.TryCreateAsync(TestData.Session($"serial-{i}"));
        }

        probe.MostInFlight.ShouldBe(1);
    }

    private static async Task AssertOverlapAsync<TContract>(TContract contract, Func<TContract, Task> fact)
        where TContract : IProbedContract
    {
        await contract.InitializeAsync().ConfigureAwait(false);
        await fact(contract).ConfigureAwait(false);

        contract.MostInFlight.ShouldBeGreaterThan(1, "no two calls were ever inside the store together: the case is not concurrent");
    }

    private interface IProbedContract
    {
        int MostInFlight { get; }

        ValueTask InitializeAsync();
    }

    private sealed class SessionFixture : SessionStoreContract, IProbedContract
    {
        private OverlapProbe<ISessionStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<ISessionStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<ISessionStore>(new InMemorySessionStore(AmbientTenant), out _probe));
    }

    private sealed class RunScoreFixture : RunScoreStoreContract, IProbedContract
    {
        private OverlapProbe<IRunScoreStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IRunScoreStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IRunScoreStore>(new InMemoryRunScoreStore(), out _probe));
    }

    private sealed class ResponseFixture : WorkflowResponseStoreContract, IProbedContract
    {
        private OverlapProbe<IWorkflowResponseStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IWorkflowResponseStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IWorkflowResponseStore>(new InMemoryWorkflowResponseStore(AmbientTenant), out _probe));
    }
}
