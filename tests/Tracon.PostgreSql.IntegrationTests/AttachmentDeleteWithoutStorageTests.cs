using Microsoft.Extensions.DependencyInjection;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// An application that no longer registers an <see cref="IAttachmentStorage"/> does not delete an attachment row that points at
/// external content, whichever way the delete comes: the attachment itself, its session, or the erasure of a data subject.
/// </summary>
/// <remarks>
/// <para>
/// The row is the only record of where the bytes are. A plain <c>DELETE</c> would leave them in the storage with nothing that
/// names them, and an erasure would report the subject erased while their files stay. The call fails and nothing is deleted:
/// not the row, not the bytes, and not the session's other attachments, which wait for the call that can delete them all.
/// Retention keeps such rows too (<c>RetentionWithoutAttachmentStorageTests</c>); a delete somebody asked for cannot keep a
/// row and report it done, so it refuses.
/// </para>
/// <para>
/// Two stores are built over one database: one WITH the storage, which writes the external attachments, and one WITHOUT, which
/// is the application after the registration was removed.
/// </para>
/// </remarks>
public sealed class AttachmentDeleteWithoutStorageTests(PostgresSchemaFixture schema)
    : IClassFixture<PostgresSchemaFixture>, IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private const string SessionId = "session-1";

    private readonly RecordingAttachmentStorage _storage = new();
    private ServiceProvider _withStorage = null!;
    private ServiceProvider _withoutStorage = null!;

    private IAttachmentStore ExternalAttachments => _withStorage.GetRequiredService<IAttachmentStore>();

    private IAttachmentStore Attachments => _withoutStorage.GetRequiredService<IAttachmentStore>();

    private string Schema => schema.Context.SchemaName;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await schema.ResetAsync();

        _withStorage = Build(storage: _storage);
        _withoutStorage = Build(storage: null);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _withStorage.DisposeAsync();
        await _withoutStorage.DisposeAsync();
    }

    [Fact]
    public async Task Deleting_an_attachment_with_external_content_is_refused_and_the_row_and_the_bytes_stay()
    {
        var external = await SaveExternalAsync();

        var refusal = await Should.ThrowAsync<TraconException>(async () => await Attachments.DeleteAsync(Tenant, external.Id));

        refusal.Message.ShouldContain("IAttachmentStorage");
        refusal.Message.ShouldNotContain(Tenant, Case.Insensitive);
        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldNotBeNull("the row is the only record of where the bytes are");
        _storage.Stored.ShouldHaveSingleItem();
        _storage.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_an_inline_attachment_and_one_that_does_not_exist_still_answers_as_before()
    {
        var inline = await SaveInlineAsync();

        (await Attachments.DeleteAsync(Tenant, inline.Id)).ShouldBeTrue();
        (await Attachments.DeleteAsync(Tenant, inline.Id)).ShouldBeFalse();
        (await Attachments.DeleteAsync(Tenant, Guid.NewGuid())).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_session_that_has_an_attachment_with_external_content_deletes_nothing_not_even_the_inline_ones()
    {
        var external = await SaveExternalAsync();
        var inline = await SaveInlineAsync();

        await Should.ThrowAsync<TraconException>(async () => await Attachments.DeleteBySessionAsync(Tenant, SessionId));

        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldNotBeNull();
        (await ExternalAttachments.GetAsync(Tenant, inline.Id)).ShouldNotBeNull("the call that can delete the whole session deletes it");
        (await AttachmentCountAsync()).ShouldBe(2);
        _storage.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_session_whose_attachments_are_all_inline_works_without_the_storage()
    {
        await SaveInlineAsync();
        await SaveInlineAsync();

        (await Attachments.DeleteBySessionAsync(Tenant, SessionId)).ShouldBe(2);
        (await AttachmentCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Erasing_a_subject_with_an_attachment_with_external_content_is_refused_before_anything_is_erased()
    {
        var external = await SaveExternalAsync();
        await SaveInlineAsync();
        var store = _withoutStorage.GetRequiredService<IDataSubjectStore>();
        var called = false;

        var refusal = await Should.ThrowAsync<TraconException>(async () => await store.EraseAsync(
            Tenant,
            Scope(),
            (_, _) =>
            {
                called = true;

                return ValueTask.CompletedTask;
            }));

        refusal.Message.ShouldContain("IAttachmentStorage");
        called.ShouldBeFalse("the audit entry of an erasure is not written for an erasure that did not happen");
        (await AttachmentCountAsync()).ShouldBe(2);
        (await ExternalAttachments.GetAsync(Tenant, external.Id)).ShouldNotBeNull();
        _storage.Deleted.ShouldBeEmpty();

        // A preview deletes nothing and is not refused: it says what the erasure would do.
        (await store.PreviewAsync(Tenant, Scope()))[DataSubjectTargetRegistry.Attachments].ShouldBe(2);
    }

    [Fact]
    public async Task Erasing_a_subject_whose_attachments_are_all_inline_works_without_the_storage()
    {
        await SaveInlineAsync();

        var erased = await _withoutStorage.GetRequiredService<IDataSubjectStore>()
            .EraseAsync(Tenant, Scope(), static (_, _) => ValueTask.CompletedTask);

        erased[DataSubjectTargetRegistry.Attachments].ShouldBe(1);
        (await AttachmentCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_storage_registered_the_attachment_the_session_and_the_subject_are_deleted_with_their_content()
    {
        var first = await SaveExternalAsync();

        (await ExternalAttachments.DeleteAsync(Tenant, first.Id)).ShouldBeTrue();

        await SaveExternalAsync();

        (await ExternalAttachments.DeleteBySessionAsync(Tenant, SessionId)).ShouldBe(1);

        await SaveExternalAsync();

        var erased = await _withStorage.GetRequiredService<IDataSubjectStore>()
            .EraseAsync(Tenant, Scope(), static (_, _) => ValueTask.CompletedTask);

        erased[DataSubjectTargetRegistry.Attachments].ShouldBe(1);
        (await AttachmentCountAsync()).ShouldBe(0);
        _storage.Stored.ShouldBeEmpty("every external content went with its row");
    }

    private static DataSubjectScope Scope()
        => new() { SessionIds = [SessionId], RunIds = [], ConversationIds = [] };

    private ServiceProvider Build(IAttachmentStorage? storage)
    {
        var services = new ServiceCollection();

        if (storage is not null)
        {
            services.AddSingleton(storage);
        }

        services.AddLogging();
        services.AddTracon().UsePostgreSql(options =>
        {
            options.ConnectionString = schema.Context.Options.ConnectionString;
            options.SchemaName = schema.Context.SchemaName;
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }

    private Task<AttachmentDescriptor> SaveExternalAsync() => SaveAsync(ExternalAttachments);

    private Task<AttachmentDescriptor> SaveInlineAsync() => SaveAsync(Attachments);

    private static async Task<AttachmentDescriptor> SaveAsync(IAttachmentStore store)
        => await store.SaveAsync(new AttachmentContent
        {
            TenantId = Tenant,
            SessionId = SessionId,
            FileName = "photo.png",
            MediaType = "image/png",
            Data = new byte[] { 1, 2, 3 },
        });

    private async Task<long> AttachmentCountAsync()
        => await schema.Context.ScalarAsync<long>($"SELECT COUNT(*) FROM {Schema}.attachments;");
}
