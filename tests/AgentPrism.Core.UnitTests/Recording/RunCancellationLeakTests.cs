using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Verifies that the cancellation registry empties no matter how the run
/// ends. A leak is a <see cref="CancellationTokenSource"/> resource leak,
/// and only these tests can catch it.
/// </summary>
public sealed class RunCancellationLeakTests
{
    [Fact]
    public async Task Registry_empties_after_a_successful_run()
    {
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(new InMemoryRunStore(tenantContext: new FixedTenantContext()), new FakeChatClient(), registry);

        await agent.RunAsync("hello");

        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Registry_empties_after_a_failed_run()
    {
        var registry = new RunCancellationRegistry();
        var client = new FakeChatClient(_ => throw new InvalidOperationException("model crashed"));
        var agent = CreateAgent(new InMemoryRunStore(tenantContext: new FixedTenantContext()), client, registry);

        var exception = await Should.ThrowAsync<AgentPrismException>(async () => await agent.RunAsync("hello"));

        exception.ErrorType.ShouldBe("upstream_error");

        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Registry_empties_after_a_stream_is_abandoned_midway()
    {
        var registry = new RunCancellationRegistry();
        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "He"),
            new ChatResponseUpdate(ChatRole.Assistant, "llo"),
        ]);
        var agent = CreateAgent(new InMemoryRunStore(tenantContext: new FixedTenantContext()), client, registry);

        // The consumer abandons enumeration after the FIRST frame.
        // `await foreach` disposes the enumerator even on an early exit; the
        // `using` fields in the outer iterator (cancellationSource/registration)
        // must go through that same dispose chain.
        await foreach (var _ in agent.RunStreamingAsync("hi"))
        {
            break;
        }

        registry.ActiveCount.ShouldBe(0);
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, FakeChatClient client, IRunCancellationRegistry registry)
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
