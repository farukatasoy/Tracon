using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies that a cancellation triggered from the outside (<see
/// cref="IRunCancellationRegistry.TryCancel"/>) actually cuts off an
/// in-flight model call, and writes <see cref="RunStatus.Canceled"/> to the
/// <c>runs</c> row.
/// </summary>
public sealed class RunCancellationStatusTests
{
    [Fact]
    public async Task External_cancellation_writes_Canceled_for_a_non_streaming_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(store, new BlockingChatClient(), registry);

        var runTask = agent.RunAsync("write a long piece of text");

        // The registry entry is written BEFORE the run row - RunRecordingAgent
        // registers first so a cancel can land during the start write. Waiting
        // for the entry alone raced the read below (measured: net10.0 leg,
        // Phase 184). Wait for what the next line reads.
        await WaitUntil.TrueAsync(async () =>
            registry.ActiveCount == 1 && (await store.QueryRunsAsync(new RunQuery())).Count == 1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        registry.TryCancel(run.Id, "test").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(async () => await runTask);

        var updated = await store.GetRunAsync(run.Id);
        updated!.Status.ShouldBe(RunStatus.Canceled);
        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task External_cancellation_writes_Canceled_for_a_streaming_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(store, new BlockingChatClient(), registry);

        var runTask = Task.Run(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("write a long piece of text"))
            {
                // Keeps consuming the stream like a client would, after the first frame;
                // BlockingChatClient never produces a second frame.
            }
        });

        // The registry entry is written BEFORE the run row - RunRecordingAgent
        // registers first so a cancel can land during the start write. Waiting
        // for the entry alone raced the read below (measured: net10.0 leg,
        // Phase 184). Wait for what the next line reads.
        await WaitUntil.TrueAsync(async () =>
            registry.ActiveCount == 1 && (await store.QueryRunsAsync(new RunQuery())).Count == 1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        registry.TryCancel(run.Id, "test").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(async () => await runTask);

        var updated = await store.GetRunAsync(run.Id);
        updated!.Status.ShouldBe(RunStatus.Canceled);
        registry.ActiveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_uncancelled_OperationCanceledException_writes_Failed_not_Canceled(bool streaming)
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(store, new TimingOutChatClient(), registry);

        if (streaming)
        {
            await Should.ThrowAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in agent.RunStreamingAsync("write a long piece of text"))
                {
                    // The client throws before producing a first frame.
                }
            });
        }
        else
        {
            await Should.ThrowAsync<OperationCanceledException>(async () => await agent.RunAsync("write a long piece of text"));
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        // 🚨 Nobody cancelled this run. HttpClient reports its OWN request
        // timeout as a TaskCanceledException, so the exception TYPE cannot
        // decide the run's status - only the run's own token can. Recorded as
        // Canceled, a provider outage would vanish from the failure numbers and
        // the caller would be handed an empty success. Measured in Phase 157.
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        registry.ActiveCount.ShouldBe(0);
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, IChatClient client, IRunCancellationRegistry registry)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            cancellationRegistry: registry);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
