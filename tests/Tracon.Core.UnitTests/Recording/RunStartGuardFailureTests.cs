using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// HATA-S4-003: a content guard that THROWS while inspecting the first user
/// message must still leave a run record behind.
/// </summary>
/// <remarks>
/// <para>
/// The input recorded into <c>RunStarted</c> and <see cref="IRunInputStore"/>
/// passes through the guard pipeline BEFORE the <c>runs</c> row is written, so
/// masked or blocked content never lands in the record raw. That order is
/// right and it stays. What was missing is that a failure in that step left no
/// record at all: the client saw an <c>error</c> frame on the stream, and
/// <c>GET /api/runs/{id}</c> then answered 404 forever. Audit, retry and
/// idempotency could none of them find the run.
/// </para>
/// <para>
/// The sibling of HATA-S4-012 (<see cref="RunStartCancellationTests"/>), where
/// the cancellation arrived AFTER the row was written. Both windows now close
/// the run instead of losing it.
/// </para>
/// </remarks>
public sealed class RunStartGuardFailureTests
{
    [Fact]
    public async Task A_guard_that_throws_before_the_run_row_exists_still_records_a_Failed_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.CompletedAt.ShouldNotBeNull();
        run.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_streaming_run_records_the_same_Failed_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync(
                "hello", cancellationToken: TestContext.Current.CancellationToken))
            {
                // The guard throws before the model is reached, so no frame arrives.
            }
        });

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.IsStreaming.ShouldBeTrue();
        run.Error.ShouldNotBeNull();
    }

    /// <summary>
    /// The guard failed, so its verdict on the text is unknown. Recording the
    /// text anyway would publish exactly the content the guard exists to hold
    /// back — the record is opened, and the query it carries is not the raw
    /// message.
    /// </summary>
    [Fact]
    public async Task The_unguarded_text_does_not_reach_the_record()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(store);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await agent.RunAsync(
                "my card number is 4111111111111111",
                cancellationToken: TestContext.Current.CancellationToken));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id))
        {
            (runEvent.Payload ?? string.Empty).ShouldNotContain("4111111111111111");
        }
    }

    /// <summary>
    /// The class scan HATA-S4-003's record asked for and left unmeasured: a
    /// guard that throws while inspecting the MODEL OUTPUT fails after the row
    /// exists, inside the protected region, so it was expected to record
    /// correctly. Expected is not measured — this measures it.
    /// </summary>
    [Fact]
    public async Task A_guard_that_throws_on_the_output_also_records_a_Failed_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var agent = CreateAgent(
            store,
            new StubContentGuard(context => context.Direction == ContentGuardDirection.Output
                ? throw new InvalidOperationException("guard is broken")
                : ContentGuardResult.Allow));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, StubContentGuard? guard = null)
    {
        guard ??= new StubContentGuard(_ => throw new InvalidOperationException("guard is broken"));
        var pipeline = TestData.ContentGuards(guards: guard);

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(pipeline, new FakeModelProvider(new FakeChatClient())),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            contentGuardPipeline: pipeline);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
