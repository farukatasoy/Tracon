using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Disaridan (<see cref="IRunCancellationRegistry.TryCancel"/>) tetiklenen bir
/// iptalin, suren bir model cagrisini gercekten kestigini ve <c>runs</c>
/// satirini <see cref="RunStatus.Canceled"/> yazdigini dogrular.
/// </summary>
public sealed class RunCancellationStatusTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Disaridan_iptal_akissiz_calistirmayi_Canceled_yazar()
    {
        var store = new InMemoryRunStore();
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(store, new BlockingChatClient(), registry);

        var runTask = agent.RunAsync("uzun bir metin yaz");

        await WaitUntilAsync(() => registry.ActiveCount == 1);

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        registry.TryCancel(run.Id, "test").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(async () => await runTask);

        var updated = await store.GetRunAsync(run.Id);
        updated!.Status.ShouldBe(RunStatus.Canceled);
        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Disaridan_iptal_akisli_calistirmayi_Canceled_yazar()
    {
        var store = new InMemoryRunStore();
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(store, new BlockingChatClient(), registry);

        var runTask = Task.Run(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("uzun bir metin yaz"))
            {
                // Ilk cerceveden sonra istemci gibi akisi tuketmeye devam eder;
                // BlockingChatClient ikinci cerceveyi hicbir zaman uretmez.
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
