using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Verifies that <see cref="AmbientRunAttributionScope"/> survives the streaming
/// path (phase 68).
/// </summary>
/// <remarks>
/// 🚨 The trap this file exists for has been paid for four times in this repo: an
/// <see cref="AsyncLocal{T}"/> write does not flow back out of an async method,
/// and inside an <c>async IAsyncEnumerable</c> body the execution context is
/// restored around every <c>yield return</c>. The defence chosen here is to
/// RESOLVE the attribution in the synchronous <c>PrepareRun</c> body, before the
/// first await — so the run keeps the identity it started with even if the caller
/// closes its scope halfway through consuming the stream.
/// </remarks>
public sealed class AmbientAttributionStreamingTests
{
    [Fact]
    public async Task An_ambient_scope_reaches_a_non_streaming_run()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store);

        using (AmbientRunAttributionScope.Begin(
            "ada",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" }))
        {
            await agent.RunAsync("hello");
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.UserId.ShouldBe("ada");
        run.Labels!["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task An_ambient_scope_reaches_a_streaming_run()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store);

        using (AmbientRunAttributionScope.Begin("ada", labels: null))
        {
            await foreach (var _ in agent.RunStreamingAsync("hello"))
            {
                // The updates themselves are not the subject; consuming the whole
                // stream inside the scope is.
            }
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.UserId.ShouldBe("ada");
    }

    [Fact]
    public async Task Closing_the_scope_mid_stream_does_not_lose_the_attribution()
    {
        // 🚨 The real streaming failure mode. The caller opens a scope, starts the
        // stream, and its scope ends while updates are still arriving — a
        // background pump or a middleware whose `using` closes first. The run must
        // still be recorded against the user it started as.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store, updates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "one"),
            new ChatResponseUpdate(ChatRole.Assistant, "two"),
            new ChatResponseUpdate(ChatRole.Assistant, "three"),
        ]);

        var scope = AmbientRunAttributionScope.Begin("ada", labels: null);
        var stream = agent.RunStreamingAsync("hello").GetAsyncEnumerator();

        try
        {
            // Pull the first update, then close the scope while the run is still
            // in flight.
            (await stream.MoveNextAsync()).ShouldBeTrue();
            scope.Dispose();
            AmbientRunAttributionScope.CurrentUserId.ShouldBeNull();

            while (await stream.MoveNextAsync())
            {
                // Drain the rest with NO ambient scope open.
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);
        run.UserId.ShouldBe("ada");
    }

    [Fact]
    public async Task A_run_started_outside_any_scope_records_no_attribution()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = CreateAgent(store);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.UserId.ShouldBeNull();
        run.Labels.ShouldBeNull();
    }

    [Fact]
    public void Nested_scopes_restore_the_outer_value()
    {
        using (AmbientRunAttributionScope.Begin("outer", labels: null))
        {
            AmbientRunAttributionScope.CurrentUserId.ShouldBe("outer");

            using (AmbientRunAttributionScope.Begin("inner", labels: null))
            {
                AmbientRunAttributionScope.CurrentUserId.ShouldBe("inner");
            }

            AmbientRunAttributionScope.CurrentUserId.ShouldBe("outer");
        }

        AmbientRunAttributionScope.CurrentUserId.ShouldBeNull();
    }

    [Fact]
    public void Beginning_a_scope_with_an_illegal_label_set_throws_rather_than_trimming()
    {
        // The loud boundary for a direct .NET caller. Trimming here would let a
        // caller believe all nine labels were recorded.
        var tooMany = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < RunLabels.MaxCount + 1; index++)
        {
            tooMany[$"key-{index}"] = "value";
        }

        Should.Throw<ArgumentException>(() => AmbientRunAttributionScope.Begin("ada", tooMany));

        // The failed call must leave no scope behind.
        AmbientRunAttributionScope.CurrentUserId.ShouldBeNull();
        AmbientRunAttributionScope.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_label_snapshot_does_not_change_when_the_caller_mutates_its_dictionary()
    {
        // The caller may hand over a live request object. A run record must not
        // change under whoever reads it later.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" };

        using (AmbientRunAttributionScope.Begin("ada", labels))
        {
            labels["team"] = "billing";
            labels["extra"] = "added-after";

            AmbientRunAttributionScope.CurrentLabels!["team"].ShouldBe("payments");
            AmbientRunAttributionScope.CurrentLabels!.Count.ShouldBe(1);
        }
    }

    private static RunRecordingAgent CreateAgent(
        IRunStore store,
        IReadOnlyList<ChatResponseUpdate>? updates = null)
    {
        var client = new FakeChatClient(streamingUpdates: updates);

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            attributionContext: new DefaultRunAttributionContext());
    }
}
