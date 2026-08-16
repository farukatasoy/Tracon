using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Verifies that a cancellation triggered from the outside (<see
/// cref="IRunCancellationRegistry.TryCancel"/>) actually cuts off an
/// in-flight model call, and writes <see cref="RunStatus.Canceled"/> to the
/// <c>runs</c> row.
/// </summary>
public sealed class RunCancellationStatusTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task External_cancellation_writes_Canceled_for_a_non_streaming_run()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(store, new BlockingChatClient(), registry);

        var runTask = agent.RunAsync("write a long piece of text");

        await WaitUntilAsync(() => registry.ActiveCount == 1);

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

        await WaitUntilAsync(() => registry.ActiveCount == 1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        registry.TryCancel(run.Id, "test").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(async () => await runTask);

        var updated = await store.GetRunAsync(run.Id);
        updated!.Status.ShouldBe(RunStatus.Canceled);
        registry.ActiveCount.ShouldBe(0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, BlockingChatClient client, IRunCancellationRegistry registry)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            cancellationRegistry: registry);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
