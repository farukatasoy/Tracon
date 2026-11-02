namespace Tracon.Tests.Differential;

/// <summary>
/// The differential suite every store implementation runs: the same generated sequences, the same hand-written model.
/// </summary>
/// <remarks>
/// <para>
/// A provider derives one class, supplies its stores in <see cref="CreateStoresAsync"/> and names its documented
/// differences in <see cref="ProviderTraits"/>. A sequence that a store answers differently from the model fails with
/// the step, both observations and the CsCheck seed that replays it
/// (<c>TRACON_DIFF_SEED=&lt;seed&gt;</c>).
/// </para>
/// <para>
/// This is the I-12 check: the four stores give one answer to one sequence of calls. It compares against a model that
/// is not derived from any store, so a mistake all four share does not stay hidden behind their agreement.
/// </para>
/// </remarks>
public abstract partial class StoreDifferentialSuite
{
    /// <summary>Gets the sequence count one state machine runs when the environment does not set one.</summary>
    protected virtual long Iterations => 120;

    /// <summary>Creates the stores under test, empty enough that sequences in their own tenants do not meet.</summary>
    /// <returns>The stores of the provider.</returns>
    internal abstract ValueTask<DifferentialStores> CreateStoresAsync();

    /// <summary>
    /// Runs session writers at the same time and checks that some serial order of the model explains every result: one
    /// create wins, one update from a version wins, a save never loses to a stale write.
    /// </summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Session_store_races_have_a_serial_explanation()
        => await new SessionStateMachine().CheckConcurrentAsync(await CreateStoresAsync(), Iterations, RaceRepeats);

    /// <summary>
    /// Runs idempotency reservations, completions and releases at the same time and checks that some serial order of the
    /// model explains every result: only one reservation of a free key wins.
    /// </summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Idempotency_store_races_have_a_serial_explanation()
        => await new IdempotencyStateMachine().CheckConcurrentAsync(await CreateStoresAsync(), Iterations, RaceRepeats);

    /// <summary>
    /// Runs API key revocations and uses at the same time and checks that some serial order of the model explains every
    /// result: of two revocations of one key only one reports it.
    /// </summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task ApiKey_store_races_have_a_serial_explanation()
        => await new ApiKeyStateMachine().CheckConcurrentAsync(await CreateStoresAsync(), Iterations, RaceRepeats);

    /// <summary>Gets how many times each generated race runs; a race that fails once in many is found by running it again.</summary>
    protected virtual int RaceRepeats => 4;

    /// <summary>Checks the run store against the model.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Run_store_agrees_with_the_model()
        => await new RunStateMachine().CheckAsync(await CreateStoresAsync(), Iterations);

    /// <summary>Checks the API key store against the model.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task ApiKey_store_agrees_with_the_model()
        => await new ApiKeyStateMachine().CheckAsync(await CreateStoresAsync(), Iterations);

    /// <summary>Checks the idempotency store against the model.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Idempotency_store_agrees_with_the_model()
        => await new IdempotencyStateMachine().CheckAsync(await CreateStoresAsync(), Iterations);

    /// <summary>Checks the session store against the model.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Session_store_agrees_with_the_model()
        => await new SessionStateMachine().CheckAsync(await CreateStoresAsync(), Iterations);
}
