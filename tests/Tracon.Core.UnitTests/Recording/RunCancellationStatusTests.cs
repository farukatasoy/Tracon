using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Recording;

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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }
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

    /// <summary>
    /// A chat client that fails the way <c>HttpClient</c> does when its own
    /// deadline elapses: a <see cref="TaskCanceledException"/> wrapping a
    /// <see cref="TimeoutException"/>, with no token cancelled anywhere.
    /// </summary>
    private sealed class TimingOutChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw Timeout();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw Timeout();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }

        private static TaskCanceledException Timeout()
        {
            const string Message = "The request to https://api.example/v1 timed out after 30s.";

            return new TaskCanceledException(Message, new TimeoutException(Message));
        }
    }
}
