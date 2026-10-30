using System.Globalization;
using System.Text.Json;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The copy of a session's content that lives outside SQL (conversation memory in the
/// host's vector store) goes when its session goes, whichever way the session goes through these two
/// stores: an erasure of its data subject, or its retention period.
/// </summary>
/// <remarks>
/// The eraser here is a recording fake: what is under test is WHEN the SQL stores call it, with WHICH
/// tenant and sessions, and what a failure does to the rows. The real eraser has its own tests in
/// <c>Tracon.Core.UnitTests</c>.
/// </remarks>
public sealed class SubjectContentEraserTests(SqliteFixture fixture)
{
    private const string EmptyJsonState = "'{}'";

    private static readonly DateTimeOffset Cutoff = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_erasure_deletes_the_copy_of_the_subjects_sessions_and_reports_its_count()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var eraser = new RecordingEraser { Records = 3 };
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [eraser]);

        var deleted = await store.EraseAsync("tenant-a", Scope("session-1"), static (_, _) => ValueTask.CompletedTask);

        deleted[RecordingEraser.TargetName].ShouldBe(3);
        eraser.Erased.ShouldBe([("tenant-a", "session-1")]);
        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeFalse();
    }

    [Fact]
    public async Task The_copy_goes_before_the_audit_callback_and_the_callback_sees_its_count()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var eraser = new RecordingEraser { Records = 2 };
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [eraser]);
        var order = new List<string>();
        eraser.OnErase = () => order.Add("erase");

        await store.EraseAsync(
            "tenant-a",
            Scope("session-1"),
            (counts, _) =>
            {
                order.Add($"audit:{counts[RecordingEraser.TargetName]}");

                return ValueTask.CompletedTask;
            });

        order.ShouldBe(["erase", "audit:2"]);
    }

    [Fact]
    public async Task A_copy_that_cannot_be_erased_fails_the_call_before_any_row_is_deleted_and_writes_no_audit_entry()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var eraser = new RecordingEraser { FailWith = new InvalidOperationException("vector store is down") };
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [eraser]);
        var audited = false;

        await Should.ThrowAsync<InvalidOperationException>(async () => await store.EraseAsync(
            "tenant-a",
            Scope("session-1"),
            (_, _) =>
            {
                audited = true;

                return ValueTask.CompletedTask;
            }));

        audited.ShouldBeFalse();
        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeTrue();

        // The repeat call finds the session and finishes the erasure.
        eraser.FailWith = null;
        await store.EraseAsync("tenant-a", Scope("session-1"), static (_, _) => ValueTask.CompletedTask);
        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_failure_after_the_copy_is_gone_leaves_every_row_in_place_and_a_repeat_call_finishes_the_erasure()
    {
        // The copies go before the transaction opens, so a failing audit write finds them already deleted. The rows roll
        // back, the sessions are still there to be named, and the repeat call erases whatever the store still holds.
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var eraser = new RecordingEraser { Records = 2 };
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [eraser]);

        await Should.ThrowAsync<InvalidOperationException>(async () => await store.EraseAsync(
            "tenant-a",
            Scope("session-1"),
            static (_, _) => throw new InvalidOperationException("the audit write failed")));

        eraser.Erased.ShouldBe([("tenant-a", "session-1")], "the copy was erased before the callback ran");
        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeTrue("the rows rolled back");

        var counts = await store.EraseAsync("tenant-a", Scope("session-1"), static (_, _) => ValueTask.CompletedTask);

        eraser.Erased.Count.ShouldBe(2, "the repeat call asks the store again");
        counts[RecordingEraser.TargetName].ShouldBe(2);
        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_preview_counts_the_copy_and_deletes_nothing()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var eraser = new RecordingEraser { Records = 4 };
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [eraser]);

        var preview = await store.PreviewAsync("tenant-a", Scope("session-1"));

        preview[RecordingEraser.TargetName].ShouldBe(4);
        eraser.Erased.ShouldBeEmpty();
        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_export_lists_the_copy_beside_the_rows()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var store = new SqlDataSubjectStore(context.StoreContext, contentErasers: [new RecordingEraser { Records = 1 }]);

        var export = await store.ExportAsync("tenant-a", Scope("session-1"));

        using var document = JsonDocument.Parse(export.Json);
        document.RootElement.GetProperty(RecordingEraser.TargetName).GetArrayLength().ShouldBe(1);
        document.RootElement.GetProperty("sessions").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Without_an_eraser_the_report_has_no_extra_target()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");

        var preview = await context.DataSubjects.PreviewAsync("tenant-a", Scope("session-1"));

        preview.ContainsKey(RecordingEraser.TargetName).ShouldBeFalse();
    }

    [Fact]
    public async Task A_retention_run_erases_the_copy_of_each_expired_session_in_its_own_tenant_before_the_row()
    {
        // 🚨 A session id is unique inside one tenant only: the same id in two tenants is two sessions.
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "shared-id", "tenant-a");
        await SeedSessionAsync(context, "shared-id", "tenant-b");
        await SeedSessionAsync(context, "other-id", "tenant-b");
        var eraser = new RecordingEraser();
        var store = new SqlRetentionStore(context.StoreContext, contentErasers: [eraser]);
        var rowsPresentAtErase = new List<bool>();
        eraser.OnEraseAsync = async (tenant, sessions) =>
        {
            foreach (var session in sessions)
            {
                rowsPresentAtErase.Add(await SessionExistsAsync(context, session, tenant));
            }
        };

        var deleted = await store.DeleteBatchAsync(RetentionTargets.Sessions, tenantId: null, Cutoff, batchSize: 10);

        deleted.ShouldBe(3);
        eraser.Erased.OrderBy(static pair => pair.Tenant, StringComparer.Ordinal).ThenBy(static pair => pair.Session, StringComparer.Ordinal)
            .ShouldBe([("tenant-a", "shared-id"), ("tenant-b", "other-id"), ("tenant-b", "shared-id")]);
        rowsPresentAtErase.ShouldBe([true, true, true]);
        (await SessionCountAsync(context)).ShouldBe(0);
    }

    [Fact]
    public async Task A_retention_run_for_one_tenant_erases_only_that_tenants_sessions()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "shared-id", "tenant-a");
        await SeedSessionAsync(context, "shared-id", "tenant-b");
        var eraser = new RecordingEraser();
        var store = new SqlRetentionStore(context.StoreContext, contentErasers: [eraser]);

        await store.DeleteBatchAsync(RetentionTargets.Sessions, "tenant-a", Cutoff, batchSize: 10);

        eraser.Erased.ShouldBe([("tenant-a", "shared-id")]);
        (await SessionExistsAsync(context, "shared-id", "tenant-b")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_copy_that_cannot_be_erased_keeps_the_expired_rows_for_the_next_run()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "session-1", "tenant-a");
        var eraser = new RecordingEraser { FailWith = new InvalidOperationException("vector store is down") };
        var store = new SqlRetentionStore(context.StoreContext, contentErasers: [eraser]);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.DeleteBatchAsync(RetentionTargets.Sessions, tenantId: null, Cutoff, batchSize: 10));

        (await SessionExistsAsync(context, "session-1", "tenant-a")).ShouldBeTrue();

        eraser.FailWith = null;
        (await store.DeleteBatchAsync(RetentionTargets.Sessions, tenantId: null, Cutoff, batchSize: 10)).ShouldBe(1);
    }

    [Fact]
    public async Task An_archive_run_hands_the_rows_to_the_sink_then_erases_the_copy_then_deletes_the_rows()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionAsync(context, "shared-id", "tenant-a");
        await SeedSessionAsync(context, "shared-id", "tenant-b");
        var eraser = new RecordingEraser();
        var store = new SqlRetentionStore(context.StoreContext, contentErasers: [eraser]);
        var order = new List<string>();
        eraser.OnErase = () => order.Add("erase");

        var deleted = await store.ArchiveBatchAsync(
            RetentionTargets.Sessions,
            tenantId: null,
            Cutoff,
            batchSize: 10,
            (rows, _) =>
            {
                order.Add($"archive:{rows.Count}");

                return ValueTask.CompletedTask;
            });

        deleted.ShouldBe(2);
        order.ShouldBe(["archive:2", "erase", "erase"]);
        eraser.Erased.Count.ShouldBe(2);
        (await SessionCountAsync(context)).ShouldBe(0);
    }

    [Fact]
    public async Task A_target_other_than_sessions_never_calls_the_eraser()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var eraser = new RecordingEraser();
        var store = new SqlRetentionStore(context.StoreContext, contentErasers: [eraser]);

        await store.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, Cutoff, batchSize: 10);

        eraser.Erased.ShouldBeEmpty();
    }

    private static DataSubjectScope Scope(params string[] sessionIds)
        => new() { SessionIds = sessionIds, RunIds = [], ConversationIds = [] };

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static async Task SeedSessionAsync(SqliteTestContext context, string id, string tenantId)
    {
        var old = Iso(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}sessions (id, tenant_id, agent_name, state, state_schema_version, created_at, updated_at)
            VALUES ('{id}', '{tenantId}', 'support', {EmptyJsonState}, 1, '{old}', '{old}');
            """);
    }

    private static async Task<bool> SessionExistsAsync(SqliteTestContext context, string id, string tenantId)
        => await context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM {context.TablePrefix}sessions WHERE id = '{id}' AND tenant_id = '{tenantId}';
            """) > 0;

    private static async Task<long> SessionCountAsync(SqliteTestContext context)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {context.TablePrefix}sessions;");

    /// <summary>An eraser that records what it was asked to erase and answers a fixed count.</summary>
    private sealed class RecordingEraser : ISubjectContentEraser
    {
        public const string TargetName = "conversation_memory";

        public int Records { get; init; }

        public Exception? FailWith { get; set; }

        public Action? OnErase { get; set; }

        public Func<string, IReadOnlyList<string>, Task>? OnEraseAsync { get; set; }

        public List<(string Tenant, string Session)> Erased { get; } = [];

        public string Target => TargetName;

        public ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
            => new(Records);

        public async ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
        {
            OnErase?.Invoke();

            if (OnEraseAsync is not null)
            {
                await OnEraseAsync(tenantId, sessionIds);
            }

            if (FailWith is not null)
            {
                throw FailWith;
            }

            Erased.AddRange(sessionIds.Select(session => (tenantId, session)));

            return Records;
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
