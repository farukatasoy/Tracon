using System.Text.Json;
using AgentPrism.Sqlite.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;
using Microsoft.Data.Sqlite;

namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// SQLite'a ozgu tip ve davranis farklarinin testleri.
/// </summary>
/// <remarks>
/// Sozlesme testleri davranis <em>esitligini</em> korur. Buradaki testler ise
/// yalnizca SQLite tarafinda var olan tuzaklari kapatir; PostgreSQL/SQL
/// Server'da karsiliklari yoktur veya farkli mekanizmalarla kapanir.
/// </remarks>
public sealed class SqliteDialectTests(SqliteFixture fixture)
{
    /// <summary>
    /// SQLite'ta <c>decimal</c> her zaman TEXT olarak yazilir; <c>REAL</c>'e
    /// (double) donusum YOKTUR. Bu, kayan nokta kesinlik kaybini onler.
    /// </summary>
    [Fact]
    public async Task Maliyet_ondaligi_kesilmeden_gidip_gelir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var runId = AgentPrismId.NewId();
        await context.Runs.StartRunAsync(TestData.Run(runId));

        var cost = new RunCost
        {
            InputCost = 0.0000123456m,
            OutputCost = 12345.6789012345m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        };

        await context.Runs.UpdateRunCostAsync(runId, cost);

        var run = await context.Runs.GetRunAsync(runId);

        run.ShouldNotBeNull();
        run.Cost.ShouldNotBeNull();
        run.Cost.InputCost.ShouldBe(0.0000123456m);
        run.Cost.OutputCost.ShouldBe(12345.6789012345m);
    }

    /// <summary>
    /// Zaman damgalari UTC yazilir ve UTC okunur. <c>SqliteDialect.AddTimestamp</c>
    /// her zaman <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c> yazar; okunan ofset sifir
    /// olmalidir.
    /// </summary>
    [Fact]
    public async Task Zaman_damgasi_utc_olarak_gidip_gelir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        // Bilerek UTC OLMAYAN bir ofsetle yazilir.
        var startedAt = new DateTimeOffset(2026, 3, 15, 12, 30, 45, TimeSpan.FromHours(3));
        var runId = AgentPrismId.NewId();

        await context.Runs.StartRunAsync(TestData.Run(runId) with { StartedAt = startedAt });

        var run = await context.Runs.GetRunAsync(runId);

        run.ShouldNotBeNull();
        run.StartedAt.Offset.ShouldBe(TimeSpan.Zero);
        run.StartedAt.ToUniversalTime().ShouldBe(startedAt.ToUniversalTime());
    }

    /// <summary>
    /// Polimorfik JSON bozulmadan doner. SQLite JSON'u duz metin olarak
    /// saklar; anahtar sirasi PostgreSQL'in <c>jsonb</c>'sinin aksine korunur.
    /// </summary>
    [Fact]
    public async Task Polimorfik_json_bozulmadan_gidip_gelir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        const string state = """
            {"$type":"agentprism.test","b":1,"aaaaaaaaaaaa":{"$type":"inner","z":"son","a":"ilk"}}
            """;

        var record = new SessionRecord
        {
            Id = "oturum-json",
            AgentName = "test-agent",
            State = JsonDocument.Parse(state).RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.Sessions.SaveAsync(record);

        var loaded = await context.Sessions.GetAsync("oturum-json");

        loaded.ShouldNotBeNull();

        var raw = loaded.State.GetRawText();

        raw.TrimStart().ShouldStartWith("{\"$type\"");
        loaded.State.GetProperty("aaaaaaaaaaaa").GetProperty("$type").GetString().ShouldBe("inner");
    }

    /// <summary>
    /// Dizi sutunlari JSON olarak tasinir ve <c>json_each</c> ile TAM eslesme
    /// aranir; ekli bir son ek ('run.completed.v2') yanlislikla eslesmemelidir.
    /// </summary>
    [Fact]
    public async Task Olay_dizisi_tam_eslesme_ile_aranir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        await context.Webhooks.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = AgentPrismId.NewId(),
            TenantId = "default",
            Name = "tam-eslesme",
            Url = "https://example.test/hook",
            Events = ["run.completed.v2"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var matches = await context.Webhooks.FindForEventAsync("default", "run.completed");

        matches.ShouldBeEmpty();

        var exact = await context.Webhooks.FindForEventAsync("default", "run.completed.v2");

        exact.Count.ShouldBe(1);
        exact[0].Events.ShouldBe(["run.completed.v2"]);
    }

    /// <summary>
    /// 🚨 <c>Microsoft.Data.Sqlite</c> guid'i BUYUK harfle yazar. Bu, uuid v7'nin
    /// zaman sirali onekinin sozluksel sirasini bozmaz; ama <c>SqliteDialect</c>'in
    /// bunu kucuk harfe CEVIRMEMESI kritiktir (bkz. sinif dokumani) — cunku
    /// zorunlu Guid'ler <c>DbHelpers.Add</c> ile (surucu varsayilani), nullable
    /// Guid'ler <c>Dialect.AddUuid</c> ile yazilir; ikisi FARKLI harf buyuklugu
    /// kullansaydi ayni mantiksal kimlik iki temsille saklanir ve bu testin
    /// dayandigi JOIN/WHERE esitligi (run_id uzerinden trace arama) SESSIZCE
    /// basarisiz olurdu.
    /// </summary>
    [Fact]
    public async Task Nullable_ve_zorunlu_guid_yazma_yollari_tutarlidir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var runId = AgentPrismId.NewId();
        await context.Runs.StartRunAsync(TestData.Run(runId));

        // WriteSpansAsync -> UpsertTraceAsync run_id'yi Dialect.AddUuid (nullable) ile yazar.
        await context.Traces.WriteSpansAsync(new TraceSpanBatch
        {
            TenantId = "default",
            TraceId = Guid.NewGuid().ToString("N"),
            RunId = runId,
            Spans =
            [
                new TraceSpan
                {
                    Id = AgentPrismId.NewId(),
                    SpanId = Guid.NewGuid().ToString("N")[..16],
                    Name = "test-span",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        // GetTraceByRunAsync ayni run_id'yi DbHelpers.Add (zorunlu) ile filtreler.
        var trace = await context.Traces.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.RunId.ShouldBe(runId);
    }

    /// <summary>
    /// WAL modu ve <c>busy_timeout</c> her yeni baglantida otomatik ayarlanir;
    /// tuketicinin baglanti dizesinde ayrica belirtmesi gerekmez.
    /// </summary>
    [Fact]
    public async Task WAL_ve_busy_timeout_baglanti_acilisinda_ayarlanir()
    {
        await using var dataSource = new SqliteDataSource(fixture.ConnectionString);
        await using var connection = (SqliteConnection)await dataSource.OpenConnectionAsync();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode;";
            ((string)command.ExecuteScalar()!).ShouldBe("wal");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA busy_timeout;";
            ((long)command.ExecuteScalar()!).ShouldBe(5000);
        }
    }

    /// <summary>
    /// Yabanci anahtar zorlamasi SQLite'ta VARSAYILAN OLARAK KAPALIDIR; her
    /// baglantida acikca acilmalidir (<c>SqliteDataSource</c>). Kapali kalsaydi
    /// <c>ON DELETE CASCADE</c> yan tumceleri sessizce yok sayilirdi.
    /// </summary>
    [Fact]
    public async Task Yabanci_anahtar_zorlamasi_etkindir()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var exception = await Should.ThrowAsync<SqliteException>(async () =>
            await context.ExecuteAsync(
                $"""
                INSERT INTO {context.TablePrefix}tool_invocations
                    (id, run_id, tool_name, created_at)
                VALUES ('{Guid.NewGuid():D}', '{Guid.NewGuid():D}', 't', '2026-01-01T00:00:00.0000000Z');
                """));

        exception.Message.ShouldContain("FOREIGN KEY");
    }
}
