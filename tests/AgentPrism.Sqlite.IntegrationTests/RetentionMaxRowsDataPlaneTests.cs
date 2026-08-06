using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// <see cref="IRetentionStore.FindRowLimitCutoffAsync"/>'in SQLite uygulamasinin
/// gercek veritabanina karsi testleri (Faz 36, <c>MaxRows</c>). PostgreSQL
/// icin <c>RetentionDataPlaneTests</c> (AgentPrism.PostgreSql.IntegrationTests)
/// ile AYNI uc senaryoyu kapsar; davranis esitligini kanitlar.
/// </summary>
public sealed class RetentionMaxRowsDataPlaneTests(SqliteFixture fixture) : IAsyncLifetime
{
    private SqliteTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await SqliteTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

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
            $"SELECT COUNT(*) FROM {_context.TablePrefix}run_events WHERE run_id = '{runId}';");

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
    /// <c>workflow_checkpoints</c> icin esik, hedefin KENDI sutunundan degil
    /// BAGLI CALISTIRMANIN <c>completed_at</c>'inden (korele alt sorgu)
    /// hesaplanir — bkz. <c>RetentionTargetRegistry.RowLimitOrderExpression</c>.
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

        deleted.ShouldBe(2);
    }

    /// <summary>
    /// 🚨 Faz 36'nin yan bulgusu: <c>attachments</c>'in <c>WherePredicate</c>'i
    /// (Faz 25) BARE hedef adini ("attachments") korelasyon olarak kullaniyordu;
    /// SQLite'ta gercek FROM'lu nesne oneklidir ("t_...attachments") ve bare ad
    /// hicbir zaman eslesmiyordu — <c>NOT EXISTS</c> her zaman DOGRU degerlendi
    /// ve sahipli ekler de silinmeye aday sayildi. Registry'deki duzeltmeyle
    /// (tam nitelendirilmis korelasyon) birlikte bu test kanit tasir.
    /// </summary>
    [Fact]
    public async Task Attachments_sahipli_ek_silinmez_sahipsiz_ek_silinir()
    {
        var sessionId = Guid.NewGuid().ToString();
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}sessions (id, tenant_id, agent_name, state, schema_version, created_at, updated_at)
            VALUES ('{sessionId}', 'test', 'support', {emptyJsonState}, 1, '{Iso(DateTimeOffset.UtcNow)}', '{Iso(DateTimeOffset.UtcNow)}');
            """);

        await SeedAttachmentAsync(sessionId: sessionId);
        await SeedAttachmentAsync(sessionId: null);

        var cutoff = DateTimeOffset.UtcNow.AddDays(1);
        var deleted = await _context.RetentionData.DeleteBatchAsync(RetentionTargets.Attachments, cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        var remaining = await _context.ScalarAsync<long>($"SELECT COUNT(*) FROM {_context.TablePrefix}attachments;");

        remaining.ShouldBe(1);
    }

    private async Task SeedAttachmentAsync(string? sessionId)
    {
        var sessionSql = sessionId is null ? "NULL" : $"'{sessionId}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}attachments
                (id, tenant_id, session_id, file_name, media_type, byte_size, sha256, created_at)
            VALUES
                ('{Guid.NewGuid()}', 'test', {sessionSql}, 'a.txt', 'text/plain', 1, 'x', '{Iso(DateTimeOffset.UtcNow.AddDays(-1))}');
            """);
    }

    private async Task<Guid> SeedRunAsync(DateTimeOffset? completedAt = null)
    {
        var id = Guid.NewGuid();
        var completedSql = completedAt is { } value ? $"'{Iso(value)}'" : "NULL";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}runs
                (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, event_count)
            VALUES
                ('{id}', 'test', 'support', 1, '{Iso(DateTimeOffset.UtcNow.AddHours(-1))}', {completedSql}, 0, 0);
            """);

        return id;
    }

    private async Task SeedRunEventAsync(Guid runId, int seq, DateTimeOffset createdAt)
        => await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}run_events (run_id, seq, type, text, created_at)
            VALUES ('{runId}', {seq}, 0, 'olay', '{Iso(createdAt)}');
            """);

    private async Task SeedWorkflowCheckpointAsync(Guid runId)
    {
        const string emptyJsonState = "'{}'";

        await _context.ExecuteAsync($"""
            INSERT INTO {_context.TablePrefix}workflow_checkpoints
                (id, tenant_id, session_id, checkpoint_id, run_id, state, created_at)
            VALUES
                ('{Guid.NewGuid()}', 'test', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{runId}', {emptyJsonState}, '{Iso(DateTimeOffset.UtcNow)}');
            """);
    }

    private static string Iso(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
