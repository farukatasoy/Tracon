using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Memory;

/// <summary>
/// The in-memory half of the content eraser hook: the session manager's delete and the data subject store
/// that runs without SQL. The SQL half is in <c>Tracon.Sqlite.IntegrationTests.SubjectContentEraserTests</c>.
/// </summary>
public sealed class SubjectContentEraserHookTests
{
    private static readonly DataSubjectScope Scope = new() { SessionIds = ["session-1"], RunIds = [], ConversationIds = [] };

    [Fact]
    public async Task Deleting_a_session_erases_its_copy_first_and_only_for_a_session_that_exists()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var eraser = new RecordingEraser();
        var manager = new AgentSessionManager(store, FixedTenantContext.Default, contentErasers: [eraser]);
        var agent = new ChatClientAgent(new FakeChatClient());
        var session = await manager.GetOrCreateSessionAsync(agent, "session-1", TestContext.Current.CancellationToken);
        await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);
        eraser.OnErase = () => store.GetAsync("session-1", CancellationToken.None).AsTask().GetAwaiter().GetResult().ShouldNotBeNull("the copy goes before the row");

        (await manager.DeleteSessionAsync("session-1", TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await manager.DeleteSessionAsync("never-opened", TestContext.Current.CancellationToken)).ShouldBeFalse();

        eraser.Erased.ShouldBe([("default", "session-1")]);
    }

    [Fact]
    public async Task A_copy_that_cannot_be_erased_leaves_the_session_so_a_repeat_call_finishes()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var eraser = new RecordingEraser { FailWith = new InvalidOperationException("store is down") };
        var manager = new AgentSessionManager(store, FixedTenantContext.Default, contentErasers: [eraser]);
        var agent = new ChatClientAgent(new FakeChatClient());
        var session = await manager.GetOrCreateSessionAsync(agent, "session-1", TestContext.Current.CancellationToken);
        await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await manager.DeleteSessionAsync("session-1", TestContext.Current.CancellationToken));

        (await store.GetAsync("session-1", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        eraser.FailWith = null;

        (await manager.DeleteSessionAsync("session-1", TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_an_eraser_deleting_a_session_is_what_it_was()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = new ChatClientAgent(new FakeChatClient());
        var session = await manager.GetOrCreateSessionAsync(agent, "session-1", TestContext.Current.CancellationToken);
        await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);

        (await manager.DeleteSessionAsync("session-1", TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_store_without_sql_counts_erases_and_exports_the_copy()
    {
        var eraser = new RecordingEraser { Records = 3 };
        var store = new NullDataSubjectStore([eraser]);
        var order = new List<string>();
        eraser.OnErase = () => order.Add("erase");

        (await store.PreviewAsync("default", Scope, TestContext.Current.CancellationToken))[RecordingEraser.TargetName].ShouldBe(3);
        eraser.Erased.ShouldBeEmpty();

        var erased = await store.EraseAsync(
            "default",
            Scope,
            (counts, _) =>
            {
                order.Add($"audit:{counts[RecordingEraser.TargetName]}");

                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        erased[RecordingEraser.TargetName].ShouldBe(3);
        order.ShouldBe(["erase", "audit:3"]);

        using var document = JsonDocument.Parse((await store.ExportAsync("default", Scope, TestContext.Current.CancellationToken)).Json);
        document.RootElement.GetProperty(RecordingEraser.TargetName).GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task The_store_without_sql_and_without_an_eraser_does_what_it_always_did()
    {
        var store = new NullDataSubjectStore();
        var audited = false;

        (await store.PreviewAsync("default", Scope, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await store.ExportAsync("default", Scope, TestContext.Current.CancellationToken)).Json.ShouldBe("{}");

        await store.EraseAsync(
            "default",
            Scope,
            (_, _) =>
            {
                audited = true;

                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        audited.ShouldBeTrue();
    }

    [Fact]
    public async Task A_copy_that_cannot_be_erased_fails_the_erasure_before_the_audit_entry()
    {
        var store = new NullDataSubjectStore([new RecordingEraser { FailWith = new InvalidOperationException("store is down") }]);
        var audited = false;

        await Should.ThrowAsync<InvalidOperationException>(async () => await store.EraseAsync(
            "default",
            Scope,
            (_, _) =>
            {
                audited = true;

                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken));

        audited.ShouldBeFalse();
    }

    private sealed class RecordingEraser : ISubjectContentEraser
    {
        public const string TargetName = "conversation_memory";

        public int Records { get; init; }

        public Exception? FailWith { get; set; }

        public Action? OnErase { get; set; }

        public List<(string Tenant, string Session)> Erased { get; } = [];

        public string Target => TargetName;

        public ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
            => new(Records);

        public ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
        {
            OnErase?.Invoke();

            if (FailWith is not null)
            {
                throw FailWith;
            }

            Erased.AddRange(sessionIds.Select(session => (tenantId, session)));

            return new(Records);
        }

        public ValueTask WriteExportAsync(string tenantId, IReadOnlyList<string> sessionIds, Utf8JsonWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteStartArray(TargetName);

            for (var index = 0; index < Records; index++)
            {
                writer.WriteStartObject();
                writer.WriteString("content", "text");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            return ValueTask.CompletedTask;
        }
    }
}
