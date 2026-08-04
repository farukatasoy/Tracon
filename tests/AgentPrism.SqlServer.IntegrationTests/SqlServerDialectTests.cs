using System.Text.Json;
using AgentPrism.SqlServer.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.SqlServer.IntegrationTests;

/// <summary>
/// SQL Server'a ozgu tip ve davranis farklarinin testleri.
/// </summary>
/// <remarks>
/// Sozlesme testleri davranis <em>esitligini</em> korur. Buradaki testler ise
/// yalnizca SQL Server tarafinda var olan tuzaklari kapatir; PostgreSQL'de
/// karsiliklari yoktur.
/// </remarks>
public sealed class SqlServerDialectTests(SqlServerFixture fixture)
{
    /// <summary>
    /// 🚨 Ondalik parametre kesinlik verilmeden gonderilirse SQL Server
    /// <c>decimal(18,0)</c> varsayar ve ondalik kismi SESSIZCE atar. Maliyet
    /// tutarlari tam sayiya yuvarlanirdi.
    /// </summary>
    [Fact]
    public async Task Maliyet_ondaligi_kesilmeden_gidip_gelir()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

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
    /// Zaman damgalari UTC yazilir ve UTC okunur. <c>datetimeoffset</c> stored
    /// ofseti korur; yazma tarafi her zaman UTC'ye cevirdigi icin okunan ofset
    /// sifir olmalidir.
    /// </summary>
    [Fact]
    public async Task Zaman_damgasi_utc_olarak_gidip_gelir()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

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
    /// Polimorfik JSON bozulmadan doner. K-027'nin anahtar siralama sorunu SQL
    /// Server'da yoktur; bu test bunun gercekten boyle oldugunu kanitlar.
    /// </summary>
    [Fact]
    public async Task Polimorfik_json_bozulmadan_gidip_gelir()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        // `$type` ayraci tasiyan, ic ice bir yuk. jsonb olsaydi anahtar sirasi
        // bozulur ve okuma JsonException verirdi.
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

        // Ilk ozellik hala `$type` olmalidir.
        raw.TrimStart().ShouldStartWith("{\"$type\"");
        loaded.State.GetProperty("aaaaaaaaaaaa").GetProperty("$type").GetString().ShouldBe("inner");
    }

    /// <summary>
    /// 🚨 SQL Server'in <c>uniqueidentifier</c> siralamasi bayt sirasina gore
    /// DEGILDIR; uuid v7 kimlikler kumelenmis bir anahtarda zaman sirali
    /// gorunmez. Bu yuzden yogun yazilan tablolarda birincil anahtar
    /// NONCLUSTERED'dir ve kumelenmis indeks zaman sutununa kuruludur (K-185).
    /// </summary>
    [Fact]
    public async Task Yogun_tablolarda_birincil_anahtar_kumelenmemistir()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        foreach (var table in new[] { "runs", "tool_invocations", "spans", "audit_log", "attachments" })
        {
            var clusteredOnPrimaryKey = await context.ScalarAsync<int>($"""
                SELECT COUNT(*)
                FROM sys.indexes i
                JOIN sys.tables t ON t.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = '{context.SchemaName}'
                  AND t.name = '{table}'
                  AND i.is_primary_key = 1
                  AND i.type_desc = 'CLUSTERED';
                """);

            clusteredOnPrimaryKey.ShouldBe(0, $"'{table}' tablosunun birincil anahtari kumelenmis olmamalidir.");
        }
    }

    /// <summary>
    /// Calistirmalar zaman sirali okunur. Kumelenmis indeks
    /// <c>(started_at, id)</c> uzerindedir; siralama uuid'in bayt sirasina
    /// baglanmamalidir.
    /// </summary>
    [Fact]
    public async Task Calistirmalar_baslangic_zamanina_gore_sirali_doner()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expected = new List<Guid>();

        for (var index = 0; index < 10; index++)
        {
            var runId = AgentPrismId.NewId();
            expected.Add(runId);

            await context.Runs.StartRunAsync(
                TestData.Run(runId) with { StartedAt = start.AddSeconds(index) });
        }

        var page = await context.Runs.QueryRunsAsync(new RunQuery { Take = 50 });

        // Sorgu yeniden eskiye siralar.
        expected.Reverse();
        page.Select(static run => run.Id).ShouldBe(expected);
    }

    /// <summary>
    /// Dizi sutunlari JSON olarak tasinir ve tam eslesme ile aranir; ekli bir
    /// son ek ('run.completed.v2') yanlislikla eslesmemelidir.
    /// </summary>
    [Fact]
    public async Task Olay_dizisi_tam_eslesme_ile_aranir()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

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
}
