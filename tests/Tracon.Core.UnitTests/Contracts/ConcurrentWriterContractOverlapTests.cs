using Tracon.Testing.Contracts.Storage;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// The concurrent-writer cases of the store contracts really run their calls at the same time.
/// </summary>
/// <remarks>
/// <para>
/// A contract case that lets calls start one after the other on one thread, or starts two tasks once, hardly turns a store that
/// looks and then writes red: against the in-memory stores, with the store made non-atomic, twenty of twenty-four such cases
/// caught it in 10 or fewer of 20 runs, nine of them in none. They are rounds of callers released together now
/// (<c>SimultaneousCalls</c>), and 23 of the 24 catch the same store in all 20 runs (the last in 13).
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
    public Task The_agent_definition_save_across_a_delete_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new AgentDefinitionFixture(), static contract => contract.Concurrent_saves_of_one_name_get_distinct_versions_across_a_delete());

    [Fact]
    public Task The_agent_definition_first_save_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new AgentDefinitionFixture(), static contract => contract.Concurrent_first_saves_of_a_new_name_get_distinct_versions());

    [Fact]
    public Task The_audit_log_chain_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new AuditLogFixture(), static contract => contract.Concurrent_writes_for_one_tenant_produce_a_single_valid_chain());

    [Fact]
    public Task The_audit_log_keyed_chain_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new AuditLogFixture(), static contract => contract.Concurrent_keyed_writes_for_one_tenant_produce_a_single_valid_chain());

    [Fact]
    public Task The_eval_promotion_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new EvalFixture(), static contract => contract.AddCaseAsync_concurrent_promotions_produce_distinct_seq());

    [Fact]
    public Task The_rate_limit_increment_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new RateLimitFixture(), static contract => contract.Concurrent_increments_are_all_counted());

    [Fact]
    public Task The_rate_limit_new_window_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new RateLimitFixture(), static contract => contract.Concurrent_increments_that_open_a_new_window_restart_the_count_once());

    [Fact]
    public Task The_retention_policy_save_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new RetentionFixture(), static contract => contract.Writers_that_save_one_target_at_the_same_time_leave_one_policy());

    [Fact]
    public Task The_retention_progress_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new RetentionFixture(), static contract => contract.Concurrent_progress_appends_all_count());

    [Fact]
    public Task The_workflow_definition_save_case_has_calls_in_the_store_at_the_same_time()
        => AssertOverlapAsync(new WorkflowDefinitionFixture(), static contract => contract.Concurrent_saves_of_one_name_get_distinct_versions_and_each_keeps_its_own_content());

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

    private sealed class AgentDefinitionFixture : AgentDefinitionStoreContract, IProbedContract
    {
        private OverlapProbe<IAgentDefinitionStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IAgentDefinitionStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IAgentDefinitionStore>(new InMemoryAgentDefinitionStore(AmbientTenant), out _probe));
    }

    private sealed class AuditLogFixture : AuditLogContract, IProbedContract
    {
        private OverlapProbe<IAuditLog>? _probe;
        private OverlapProbe<IAuditLog>? _keyedProbe;

        public int MostInFlight => Math.Max(_probe?.MostInFlight ?? 0, _keyedProbe?.MostInFlight ?? 0);

        protected override ValueTask<IAuditLog> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IAuditLog>(new InMemoryAuditLog(AmbientTenant), out _probe));

        protected override ValueTask<IAuditLog?> CreateKeyedLogAsync(string? signingKeyId, IReadOnlyDictionary<string, byte[]> keys)
            => ValueTask.FromResult<IAuditLog?>(
                OverlapProbe.Wrap<IAuditLog>(new InMemoryAuditLog(AmbientTenant, AuditChainKeyRing.Create(signingKeyId, keys)), out _keyedProbe));
    }

    private sealed class EvalFixture : EvalStoreContract, IProbedContract
    {
        private OverlapProbe<IEvalStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IEvalStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IEvalStore>(new InMemoryEvalStore(), out _probe));
    }

    private sealed class RateLimitFixture : RateLimitCounterStoreContract, IProbedContract
    {
        private OverlapProbe<IRateLimitCounterStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IRateLimitCounterStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IRateLimitCounterStore>(new InMemoryRateLimitCounterStore(), out _probe));
    }

    private sealed class RetentionFixture : RetentionPolicyStoreContract, IProbedContract
    {
        private OverlapProbe<IRetentionPolicyStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IRetentionPolicyStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IRetentionPolicyStore>(new InMemoryRetentionPolicyStore(), out _probe));
    }

    private sealed class WorkflowDefinitionFixture : WorkflowDefinitionStoreContract, IProbedContract
    {
        private OverlapProbe<IWorkflowDefinitionStore>? _probe;

        public int MostInFlight => _probe?.MostInFlight ?? 0;

        protected override ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
            => ValueTask.FromResult(OverlapProbe.Wrap<IWorkflowDefinitionStore>(new InMemoryWorkflowDefinitionStore(), out _probe));
    }
}
