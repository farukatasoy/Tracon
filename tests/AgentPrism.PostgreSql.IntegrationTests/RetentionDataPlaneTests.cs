using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="IRetentionStore"/>'un PostgreSQL uygulamasinin sayma/silme/arsiv
/// okuma davranisi. Faz 25'in DoD'sindeki gercek kanit gereksinimini karsilar:
/// eski satirlar duser, <c>runs</c> ozeti ve <c>audit_log</c> KORUNUR.
/// </summary>
public sealed class RetentionDataPlaneTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Eski_run_events_silinir_yeniler_kalir()
    {
        var runId = await SeedRunAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await SeedRunEventAsync(runId, seq: 1, createdAt: cutoff.AddDays(-5));
        await SeedRunEventAsync(runId, seq: 2, createdAt: cutoff.AddDays(-1));
        await SeedRunEventAsync(runId, seq: 3, createdAt: cutoff.AddDays(5));

        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, cutoff)).ShouldBe(2);

        var deleted = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, cutoff, batchSize: 100);

        deleted.ShouldBe(2);

        var remaining = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.run_events WHERE run_id = '{runId}';");

        remaining.ShouldBe(1);
    }

    [Fact]
    public async Task Silme_parti_parti_calisir()
    {
        var runId = await SeedRunAsync();
        var cutoff = DateTimeOffset.UtcNow;

        for (var i = 0; i < 25; i++)
        {
            await SeedRunEventAsync(runId, seq: i, createdAt: cutoff.AddDays(-1));
        }

        var firstBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, cutoff, batchSize: 10);
        var secondBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, cutoff, batchSize: 10);
        var thirdBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, cutoff, batchSize: 10);
        var fourthBatch = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, cutoff, batchSize: 10);

        firstBatch.ShouldBe(10);
        secondBatch.ShouldBe(10);
        thirdBatch.ShouldBe(5);
        fourthBatch.ShouldBe(0);
    }

    [Fact]
    public async Task Run_events_silinirken_runs_ozeti_korunur()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow.AddDays(-60));

        await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, DateTimeOffset.UtcNow, batchSize: 100);

        var runStillExists = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.runs WHERE id = '{runId}';");

        runStillExists.ShouldBe(1);
    }

    [Fact]
    public async Task Saklama_audit_log_hedefi_beyaz_listede_yoktur_ve_asla_silinmez()
    {
        var before = await _context.ScalarAsync<long>($"SELECT COUNT(*) FROM {_context.SchemaName}.audit_log;");

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.audit_log (id, tenant_id, actor, action, entity, created_at)
            VALUES (gen_random_uuid(), 'test', 'test-actor', 'agent.create', 'agent:demo', now() - interval '400 days');
            """);

        // 'audit_log' RetentionTargets beyaz listesinde YOKTUR; IRetentionStore
        // bilinmeyen hedefte ArgumentException firlatir — bu, denemenin bile
        // mumkun olmadigini kanitlar.
        await Should.ThrowAsync<ArgumentException>(async ()
            => await _context.RetentionData.CountOlderThanAsync("audit_log", DateTimeOffset.UtcNow));

        var after = await _context.ScalarAsync<long>($"SELECT COUNT(*) FROM {_context.SchemaName}.audit_log;");

        after.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Arsiv_okuma_satirlari_JSON_olarak_dondurur_ve_silmez()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow.AddDays(-60), text: "merhaba");

        var cutoff = DateTimeOffset.UtcNow;
        var rows = await _context.RetentionData.ReadForArchiveAsync(RetentionTargets.RunEvents, cutoff, batchSize: 10);

        rows.Count.ShouldBe(1);
        rows[0].Json.ShouldContain("merhaba");
        rows[0].Json.ShouldContain(runId.ToString());

        // Okuma SILMEZ.
        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, cutoff)).ShouldBe(1);
    }

    [Fact]
    public async Task Tamamlanmis_is_silinir_bekleyen_is_kalir()
    {
        var pendingId = await SeedJobAsync(status: 0, completedAt: null);
        var completedId = await SeedJobAsync(status: 3, completedAt: DateTimeOffset.UtcNow.AddDays(-60));

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.Jobs,
            DateTimeOffset.UtcNow.AddDays(-30),
            batchSize: 100);

        deleted.ShouldBe(1);

        var pendingExists = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.jobs WHERE id = '{pendingId}';");
        var completedExists = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.jobs WHERE id = '{completedId}';");

        pendingExists.ShouldBe(1);
        completedExists.ShouldBe(0);
    }

    /// <summary>
    /// 🚨 Faz 36'nin ana kanit gereksinimi: <c>MaxRows</c> esigi, en yeniden
    /// sayarak N. satirin OWN sutunundan dogru hesaplanir ve o esik MEVCUT
    /// parti silme mekanizmasina beslenince tabloyu tam olarak N satirda birakir.
    /// </summary>
    [Fact]
    public async Task MaxRows_esigi_dogru_hesaplanir_ve_hedefi_N_satirda_birakir()
    {
        var runId = await SeedRunAsync();

        for (var i = 0; i < 10; i++)
        {
            await SeedRunEventAsync(runId, seq: i, createdAt: DateTimeOffset.UtcNow.AddMinutes(-10 + i));
        }

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.RunEvents, maxRows: 4);

        cutoff.ShouldNotBeNull();

        var deleted = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, cutoff!.Value, batchSize: 100);

        deleted.ShouldBe(6);

        var remaining = await _context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {_context.SchemaName}.run_events WHERE run_id = '{runId}';");

        remaining.ShouldBe(4);
    }

    [Fact]
    public async Task MaxRows_esigi_tablo_sinirin_altindaysa_null_doner()
    {
        var runId = await SeedRunAsync();
        await SeedRunEventAsync(runId, seq: 1, createdAt: DateTimeOffset.UtcNow);

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.RunEvents, maxRows: 1000);

        cutoff.ShouldBeNull();
    }

    /// <summary>
    /// <c>workflow_checkpoints</c> icin MaxRows esigi, hedefin KENDI sutunundan
    /// degil BAGLI CALISTIRMANIN <c>completed_at</c>'inden (korele alt sorgu)
    /// hesaplanir — <see cref="RetentionTargetRegistry"/>'nin
    /// <c>RowLimitOrderExpression</c>'i. Bu, WherePredicate'in gercekten
    /// karsilastirdigi sutunla ayni olmalidir; aksi halde esik hicbir satiri
    /// silmezdi.
    /// </summary>
    [Fact]
    public async Task MaxRows_esigi_iliskili_tablo_uzerinden_dogru_hesaplanir()
    {
        var run1 = await SeedRunAsync(completedAt: DateTimeOffset.UtcNow.AddDays(-10));
        var run2 = await SeedRunAsync(completedAt: DateTimeOffset.UtcNow.AddDays(-5));
        var run3 = await SeedRunAsync(completedAt: DateTimeOffset.UtcNow);

        await SeedWorkflowCheckpointAsync(run1);
        await SeedWorkflowCheckpointAsync(run2);
        await SeedWorkflowCheckpointAsync(run3);

        var cutoff = await _context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.WorkflowCheckpoints, maxRows: 1);

        cutoff.ShouldNotBeNull();

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.WorkflowCheckpoints,
            cutoff!.Value,
            batchSize: 100);

        // Yalniz run1 ve run2'ye ait checkpoint'ler run3'ten (en yeni) daha
        // ESKIDIR ve wherePredicate'in EXISTS kosulunu saglar.
        deleted.ShouldBe(2);
    }

    private async Task<Guid> SeedRunAsync(DateTimeOffset? completedAt = null)
    {
        var id = Guid.NewGuid();
        var completedSql = completedAt is { } value ? $"'{value.UtcDateTime:O}'" : "NULL";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.runs
                (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, event_count)
            VALUES
                ('{id}', 'test', 'support', 1, now() - interval '1 hour', {completedSql}, false, 0);
            """);

        return id;
    }

    private async Task SeedWorkflowCheckpointAsync(Guid runId)
    {
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.workflow_checkpoints
                (id, tenant_id, session_id, checkpoint_id, run_id, state, created_at)
            VALUES
                (gen_random_uuid(), 'test', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{runId}', {emptyJsonState}, now());
            """);
    }

    private async Task SeedRunEventAsync(Guid runId, int seq, DateTimeOffset createdAt, string text = "olay")
        => await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.run_events (run_id, seq, type, text, created_at)
            VALUES ('{runId}', {seq}, 0, '{text}', '{createdAt.UtcDateTime:O}');
            """);

    private async Task<Guid> SeedJobAsync(short status, DateTimeOffset? completedAt)
    {
        var id = Guid.NewGuid();
        var completedSql = completedAt is { } value ? $"'{value.UtcDateTime:O}'" : "NULL";
        const string emptyJsonPayload = "'{}'::jsonb";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.SchemaName}.jobs
                (id, tenant_id, kind, target_name, status, payload, scheduled_for, completed_at, created_at)
            VALUES
                ('{id}', 'test', 0, 'support', {status}, {emptyJsonPayload}, now(), {completedSql}, now());
            """);

        return id;
    }
}
