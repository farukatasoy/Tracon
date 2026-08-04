using System.Collections.Concurrent;
using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <c>FOR UPDATE SKIP LOCKED</c> garantisinin kaniti: iki gercek
/// <see cref="IJobStore"/> orneği ayni veritabanina baglanip ayni kuyruk
/// icin yarisir.
/// </summary>
/// <remarks>
/// Bellek ici depoda bu garanti bir <c>lock</c> ile saglanir ve tek surecte
/// tartismasizdir; asil kanit yalnizca gercek PostgreSQL'e karsi, gercek
/// eszamanlilikla verilebilir. Gerekce: docs/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md,
/// bolum "Testler".
/// </remarks>
public sealed class JobStoreConcurrencyTests(PostgresFixture fixture)
{
    private const int JobCount = 50;

    [Fact]
    public async Task Iki_isci_ayni_isi_iki_kez_kiralamaz()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var seed = PostgresTestContext.Create(fixture, schemaName);
        await seed.Migrations.ApplyAsync();

        var expectedIds = new List<Guid>();

        for (var i = 0; i < JobCount; i++)
        {
            var job = await seed.Jobs.EnqueueAsync(TestData.Job(), ["girdi"]);
            expectedIds.Add(job.Id);
        }

        await using var workerA = PostgresTestContext.Create(fixture, schemaName);
        await using var workerB = PostgresTestContext.Create(fixture, schemaName);

        var leasedByA = new ConcurrentBag<Guid>();
        var leasedByB = new ConcurrentBag<Guid>();

        await Task.WhenAll(
            LeaseAllAsync(workerA.Jobs, "isci-a", leasedByA),
            LeaseAllAsync(workerB.Jobs, "isci-b", leasedByB));

        var all = leasedByA.Concat(leasedByB).ToList();

        // Hicbir is iki kez kiralanmadi (toplam sayi ve tekil sayi esit) ve
        // kuyruktaki her is tam olarak bir kere alindi. Kume esitligi kontrol
        // edilir; iki isci arasindaki bolusum sirasi onemli degildir.
        all.Count.ShouldBe(JobCount);
        all.Distinct().Count().ShouldBe(JobCount);
        all.ToHashSet().SetEquals(expectedIds).ShouldBeTrue();
    }

    private static async Task LeaseAllAsync(SqlJobStore store, string owner, ConcurrentBag<Guid> leased)
    {
        while (true)
        {
            var job = await store.LeaseAsync(owner, TimeSpan.FromMinutes(5));

            if (job is null)
            {
                return;
            }

            leased.Add(job.Id);
        }
    }
}
