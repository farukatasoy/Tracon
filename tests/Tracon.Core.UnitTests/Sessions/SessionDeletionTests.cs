using System.Text.Json;
using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Sessions;

/// <summary>
/// The order and the failure behavior of <see cref="SessionDeletion"/>.
/// </summary>
/// <remarks>
/// The functional tests prove that every caller reaches the same deletion. These pin what
/// the deletion does once it runs: copy, attachments, record, in that order, so a step that
/// fails leaves the record in place and a repeat call finishes the job.
/// </remarks>
public sealed class SessionDeletionTests
{
    private const string SessionId = "session-1";

    [Fact]
    public async Task The_copy_goes_first_then_the_attachments_and_the_record_last()
    {
        var fixture = await OpenAsync();

        fixture.Eraser.OnErase = () => fixture.Order.Add("copy");
        fixture.Attachments.OnDelete = () =>
        {
            fixture.Order.Add("attachments");
            fixture.Store.GetAsync(SessionId, CancellationToken.None).AsTask().GetAwaiter().GetResult()
                .ShouldNotBeNull("the record is still there when the attachments go");
        };

        (await fixture.Deletion.DeleteAsync(SessionId, TestContext.Current.CancellationToken)).ShouldBeTrue();

        fixture.Order.ShouldBe(["copy", "attachments"]);
        (await fixture.Store.GetAsync(SessionId, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await fixture.Attachments.Inner.GetAsync("default", fixture.AttachmentId, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task An_attachment_store_that_fails_leaves_the_record_so_a_repeat_call_finishes()
    {
        var fixture = await OpenAsync();

        fixture.Attachments.FailWith = new InvalidOperationException("attachment store is down");

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await fixture.Deletion.DeleteAsync(SessionId, TestContext.Current.CancellationToken));

        (await fixture.Store.GetAsync(SessionId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await fixture.Attachments.Inner.GetAsync("default", fixture.AttachmentId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        fixture.Attachments.FailWith = null;

        (await fixture.Deletion.DeleteAsync(SessionId, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await fixture.Attachments.Inner.GetAsync("default", fixture.AttachmentId, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_copy_that_fails_touches_neither_the_attachments_nor_the_record()
    {
        var fixture = await OpenAsync();

        fixture.Eraser.FailWith = new InvalidOperationException("copy store is down");

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await fixture.Deletion.DeleteAsync(SessionId, TestContext.Current.CancellationToken));

        fixture.Attachments.DeleteCalls.ShouldBe(0);
        (await fixture.Store.GetAsync(SessionId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_session_that_does_not_exist_deletes_nothing_and_its_attachments_stay()
    {
        var fixture = await OpenAsync();

        (await fixture.Deletion.DeleteAsync("never-opened", TestContext.Current.CancellationToken)).ShouldBeFalse();

        fixture.Attachments.DeleteCalls.ShouldBe(0);
        fixture.Eraser.Erased.ShouldBeEmpty();
        (await fixture.Store.GetAsync(SessionId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_attachments_are_deleted_in_the_tenant_of_the_call_with_the_callers_token()
    {
        var fixture = await OpenAsync(tenant: "tenant-x");
        using var source = new CancellationTokenSource();

        await fixture.Deletion.DeleteAsync(SessionId, source.Token);

        fixture.Attachments.LastTenant.ShouldBe("tenant-x");
        fixture.Attachments.LastSessionId.ShouldBe(SessionId);
        fixture.Attachments.LastToken.ShouldBe(source.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_empty_session_id_is_refused_before_anything_is_touched(string sessionId)
    {
        var fixture = await OpenAsync();

        await Should.ThrowAsync<ArgumentException>(
            async () => await fixture.Deletion.DeleteAsync(sessionId, TestContext.Current.CancellationToken));

        fixture.Attachments.DeleteCalls.ShouldBe(0);
    }

    private static async Task<Fixture> OpenAsync(string tenant = "default")
    {
        var tenantContext = new FixedTenantContext(tenant);
        var store = new InMemorySessionStore(tenantContext);
        var eraser = new RecordingEraser();
        var manager = new AgentSessionManager(store, tenantContext, contentErasers: [eraser]);
        var agent = new ChatClientAgent(new FakeChatClient());
        var session = await manager.GetOrCreateSessionAsync(agent, SessionId, TestContext.Current.CancellationToken);

        await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);

        var attachments = new RecordingAttachmentStore(new InMemoryAttachmentStore());
        var saved = await attachments.Inner.SaveAsync(
            new AttachmentContent
            {
                TenantId = tenantContext.TenantId,
                SessionId = SessionId,
                FileName = "a.png",
                MediaType = "image/png",
                Data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            },
            TestContext.Current.CancellationToken);

        return new Fixture(
            new SessionDeletion(manager, attachments, tenantContext),
            store,
            eraser,
            attachments,
            saved.Id,
            []);
    }

    private sealed record Fixture(
        SessionDeletion Deletion,
        InMemorySessionStore Store,
        RecordingEraser Eraser,
        RecordingAttachmentStore Attachments,
        Guid AttachmentId,
        List<string> Order);

    private sealed class RecordingEraser : ISubjectContentEraser
    {
        public Exception? FailWith { get; set; }

        public Action? OnErase { get; set; }

        public List<(string Tenant, string Session)> Erased { get; } = [];

        public string Target => "recording_copy";

        public ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
            => new(0);

        public ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
        {
            OnErase?.Invoke();

            if (FailWith is not null)
            {
                throw FailWith;
            }

            Erased.AddRange(sessionIds.Select(session => (tenantId, session)));

            return new(0);
        }

        public ValueTask WriteExportAsync(string tenantId, IReadOnlyList<string> sessionIds, Utf8JsonWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteStartArray(Target);
            writer.WriteEndArray();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A real in-memory store whose session delete is observable and can fail.</summary>
    private sealed class RecordingAttachmentStore(InMemoryAttachmentStore inner) : IAttachmentStore
    {
        public InMemoryAttachmentStore Inner { get; } = inner;

        public Exception? FailWith { get; set; }

        public Action? OnDelete { get; set; }

        public int DeleteCalls { get; private set; }

        public string? LastTenant { get; private set; }

        public string? LastSessionId { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public ValueTask<AttachmentDescriptor> SaveAsync(AttachmentContent content, CancellationToken cancellationToken = default)
            => Inner.SaveAsync(content, cancellationToken);

        public ValueTask<AttachmentDescriptor?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => Inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<Stream?> OpenReadAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => Inner.OpenReadAsync(tenantId, id, cancellationToken);

        public ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(AttachmentQuery query, CancellationToken cancellationToken = default)
            => Inner.ListAsync(query, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => Inner.DeleteAsync(tenantId, id, cancellationToken);

        public ValueTask<int> DeleteBySessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            LastTenant = tenantId;
            LastSessionId = sessionId;
            LastToken = cancellationToken;
            OnDelete?.Invoke();

            if (FailWith is not null)
            {
                throw FailWith;
            }

            return Inner.DeleteBySessionAsync(tenantId, sessionId, cancellationToken);
        }
    }
}
