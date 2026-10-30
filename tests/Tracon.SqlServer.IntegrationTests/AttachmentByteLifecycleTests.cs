using Microsoft.Extensions.DependencyInjection;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Retention, erasure, and the attachment store delete an attachment's
/// external bytes BEFORE its row, and keep the row when the byte delete fails.
/// A workflow run's attachments are not orphans while the run can still use
/// them.
/// </summary>
/// <remarks>
/// <para>
/// Retention and erasure ran a plain <c>DELETE</c> and never read
/// <c>external_uri</c>, so the bytes in <see cref="IAttachmentStorage"/>
/// stayed forever. The attachment store deleted the row first and the bytes
/// second, so a storage failure lost the location for good.
/// </para>
/// <para>
/// The stores are resolved through <c>AddTracon().UseSqlServer()</c>, not built
/// with <c>new</c>: the retention and erasure stores never received the
/// storage from the container, and only a container-built store shows that.
/// </para>
/// </remarks>
public sealed class AttachmentByteLifecycleTests(SqlServerSchemaFixture schema)
    : IClassFixture<SqlServerSchemaFixture>, IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private const string WorkflowSession = "wf-s";
    private const string EmptyJsonState = "N'{}'";

    private readonly RecordingAttachmentStorage _storage = new();
    private ServiceProvider _services = null!;

    private IAttachmentStore Attachments => _services.GetRequiredService<IAttachmentStore>();

    private IRetentionStore Retention => _services.GetRequiredService<IRetentionStore>();

    private IDataSubjectStore DataSubjects => _services.GetRequiredService<IDataSubjectStore>();

    private string Schema => schema.Context.SchemaName;

    /// <summary>The cutoff of the default seven-day attachment policy.</summary>
    private static DateTimeOffset SevenDayCutoff => DateTimeOffset.UtcNow.AddDays(-7);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await schema.ResetAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IAttachmentStorage>(_storage);
        services.AddTracon().UseSqlServer(options =>
        {
            options.ConnectionString = schema.Context.Options.ConnectionString;
            options.SchemaName = schema.Context.SchemaName;
            options.AutoApplyMigrations = false;
        });

        _services = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task Retention_deletes_the_external_bytes_of_an_orphaned_attachment()
    {
        var saved = await SaveAsync(sessionId: null);
        var uri = _storage.Stored.ShouldHaveSingleItem();

        var deleted = await Retention.DeleteBatchAsync(
            RetentionTargets.Attachments, Tenant, DateTimeOffset.UtcNow.AddDays(1), batchSize: 100);

        deleted.ShouldBe(1);
        _storage.Deleted.ShouldBe([uri]);
        (await Attachments.GetAsync(Tenant, saved.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Archive_mode_deletes_the_bytes_and_rows_of_the_archived_attachments_only()
    {
        // The archive mode deleted another batch than the one it
        // archived. With bytes in IAttachmentStorage, it deletes the bytes of
        // the archived rows first, then those rows, and nothing else.
        for (var i = 0; i < 3; i++)
        {
            await SaveAsync(sessionId: null);
        }

        var archived = new List<ArchiveRow>();

        var deleted = await Retention.ArchiveBatchAsync(
            RetentionTargets.Attachments,
            Tenant,
            DateTimeOffset.UtcNow.AddDays(1),
            batchSize: 2,
            (rows, _) =>
            {
                archived.AddRange(rows);

                return ValueTask.CompletedTask;
            });

        archived.Count.ShouldBe(2);
        deleted.ShouldBe(2);
        _storage.Stored.Count.ShouldBe(1);

        var archivedLocations = archived
            .Select(static row => System.Text.Json.JsonDocument.Parse(row.Json).RootElement.GetProperty("external_uri").GetString())
            .ToList();

        _storage.Deleted.Select(static uri => uri.OriginalString).ShouldBe(archivedLocations, ignoreOrder: true);
    }

    [Fact]
    public async Task Retention_keeps_the_row_when_the_byte_delete_fails()
    {
        var saved = await SaveAsync(sessionId: null);
        _storage.FailDeletes = true;

        var deleted = await Retention.DeleteBatchAsync(
            RetentionTargets.Attachments, Tenant, DateTimeOffset.UtcNow.AddDays(1), batchSize: 100);

        deleted.ShouldBe(0);
        (await Attachments.GetAsync(Tenant, saved.Id)).ShouldNotBeNull("the row is the only record of where the bytes are");

        // The next run retries the row and finishes the job.
        _storage.FailDeletes = false;
        (await Retention.DeleteBatchAsync(
            RetentionTargets.Attachments, Tenant, DateTimeOffset.UtcNow.AddDays(1), batchSize: 100)).ShouldBe(1);
        _storage.Stored.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(RunStatus.AwaitingInput, 60, true)]
    [InlineData(RunStatus.Running, null, true)]
    [InlineData(RunStatus.Completed, 1, true)]
    [InlineData(RunStatus.Completed, 60, false)]
    public async Task A_workflow_runs_attachment_is_an_orphan_only_after_the_run_is_done(
        RunStatus status,
        int? completedDaysAgo,
        bool kept)
    {
        await SeedWorkflowRunAsync(Tenant, status, completedDaysAgo);
        var saved = await SaveAsync(WorkflowSession);
        await BackdateAsync(saved.Id, days: 30);

        var deleted = await Retention.DeleteBatchAsync(RetentionTargets.Attachments, Tenant, SevenDayCutoff, batchSize: 100);

        deleted.ShouldBe(kept ? 0 : 1);
        (await Attachments.GetAsync(Tenant, saved.Id) is not null).ShouldBe(kept);
        _storage.Deleted.Count.ShouldBe(kept ? 0 : 1);
    }

    [Fact]
    public async Task Another_tenants_session_or_workflow_run_does_not_own_the_attachment()
    {
        await SeedSessionAsync(WorkflowSession, "tenant-b");
        await SeedWorkflowRunAsync("tenant-b", RunStatus.AwaitingInput, completedDaysAgo: 60);
        var saved = await SaveAsync(WorkflowSession);
        await BackdateAsync(saved.Id, days: 30);

        var deleted = await Retention.DeleteBatchAsync(RetentionTargets.Attachments, Tenant, SevenDayCutoff, batchSize: 100);

        deleted.ShouldBe(1);
        _storage.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task Erasure_deletes_the_external_bytes()
    {
        await SaveAsync("subject-session");
        var uri = _storage.Stored.ShouldHaveSingleItem();

        var deleted = await DataSubjects.EraseAsync(
            Tenant, Scope("subject-session"), static (_, _) => ValueTask.CompletedTask);

        deleted[DataSubjectTargetRegistry.Attachments].ShouldBe(1);
        _storage.Deleted.ShouldBe([uri]);
    }

    [Fact]
    public async Task Erasure_fails_and_keeps_the_rows_when_the_byte_delete_fails()
    {
        await SeedSessionAsync("subject-session", Tenant);
        var saved = await SaveAsync("subject-session");
        _storage.FailDeletes = true;
        var auditWritten = false;

        await Should.ThrowAsync<IOException>(async () => await DataSubjects.EraseAsync(
            Tenant,
            Scope("subject-session"),
            (_, _) =>
            {
                auditWritten = true;
                return ValueTask.CompletedTask;
            }));

        auditWritten.ShouldBeFalse("an erasure that did not happen is not written to the audit trail");
        (await Attachments.GetAsync(Tenant, saved.Id)).ShouldNotBeNull();
        (await SessionCountAsync("subject-session")).ShouldBe(1);
    }

    [Fact]
    public async Task Erasure_preview_does_not_delete_bytes()
    {
        var saved = await SaveAsync("subject-session");

        var preview = await DataSubjects.PreviewAsync(Tenant, Scope("subject-session"));

        preview[DataSubjectTargetRegistry.Attachments].ShouldBe(1);
        _storage.Deleted.ShouldBeEmpty();
        (await Attachments.GetAsync(Tenant, saved.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Attachment_delete_keeps_the_row_when_the_byte_delete_fails()
    {
        var saved = await SaveAsync(sessionId: null);
        _storage.FailDeletes = true;

        await Should.ThrowAsync<IOException>(async () => await Attachments.DeleteAsync(Tenant, saved.Id));

        (await Attachments.GetAsync(Tenant, saved.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Session_attachment_delete_keeps_the_rows_when_the_byte_delete_fails()
    {
        var first = await SaveAsync("closing-session");
        var second = await SaveAsync("closing-session");
        _storage.FailDeletes = true;

        await Should.ThrowAsync<IOException>(async () => await Attachments.DeleteBySessionAsync(Tenant, "closing-session"));

        (await Attachments.GetAsync(Tenant, first.Id)).ShouldNotBeNull();
        (await Attachments.GetAsync(Tenant, second.Id)).ShouldNotBeNull();

        _storage.FailDeletes = false;
        (await Attachments.DeleteBySessionAsync(Tenant, "closing-session")).ShouldBe(2);
        _storage.Stored.ShouldBeEmpty();
    }

    private static DataSubjectScope Scope(string sessionId) => new()
    {
        SessionIds = [sessionId],
        RunIds = [],
        ConversationIds = [],
    };

    private async Task<AttachmentDescriptor> SaveAsync(string? sessionId)
        => await Attachments.SaveAsync(new AttachmentContent
        {
            TenantId = Tenant,
            SessionId = sessionId,
            FileName = "photo.png",
            MediaType = "image/png",
            Data = new byte[] { 1, 2, 3 },
        });

    private async Task BackdateAsync(Guid id, int days)
        => await schema.Context.ExecuteAsync(
            $"UPDATE {Schema}.attachments SET created_at = DATEADD(day, -{days}, SYSDATETIMEOFFSET()) WHERE id = '{id}';");

    private async Task SeedWorkflowRunAsync(string tenantId, RunStatus status, int? completedDaysAgo)
    {
        var completed = completedDaysAgo is { } days ? $"DATEADD(day, -{days}, SYSDATETIMEOFFSET())" : "NULL";

        await schema.Context.ExecuteAsync($"""
            INSERT INTO {Schema}.runs
                (id, tenant_id, agent_name, session_id, status, started_at, completed_at, is_streaming, event_count, kind, workflow_name)
            VALUES
                ('{Guid.NewGuid()}', '{tenantId}', 'review', '{WorkflowSession}', {(int)status},
                 DATEADD(day, -90, SYSDATETIMEOFFSET()), {completed}, 0, 0, {(int)RunKind.Workflow}, 'review');
            """);
    }

    private async Task SeedSessionAsync(string id, string tenantId)
        => await schema.Context.ExecuteAsync($"""
            INSERT INTO {Schema}.sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

    private async Task<int> SessionCountAsync(string id)
        => await schema.Context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {Schema}.sessions WHERE id = '{id}' AND tenant_id = '{Tenant}';");
}
