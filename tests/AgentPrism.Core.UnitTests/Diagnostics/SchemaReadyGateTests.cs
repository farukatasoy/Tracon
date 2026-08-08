namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// <see cref="SchemaReadyGate"/> sozlesmesi.
/// </summary>
/// <remarks>
/// 🚨 Bu testlerin varlik sebebi olculmus bir kusurdur (Faz 42, 2026-08-08 raporu
/// bolum 2.3): <c>MigrationHostedService.StartAsync</c> migration'lari tam bekler
/// ama <c>BackgroundService.StartAsync</c> <c>ExecuteAsync</c>'i beklemeden doner.
/// Kayit sirasi <c>.UseMcp()</c> → <c>.UseSqlite()</c> ise ilk SQL denemesi
/// migration bitmeden calisir ve "no such table" verir. Gerekce: K-354.
/// </remarks>
public sealed class SchemaReadyGateTests
{
    /// <summary>
    /// Bellek ici kurulum: hicbir SQL saglayicisi kayitli degil. Kapi
    /// KENDILIGINDEN aciktir; aksi hâlde arka plan servisleri sonsuza dek beklerdi.
    /// </summary>
    [Fact]
    public async Task Kayitli_SQL_saglayicisi_yoksa_kapi_hemen_acilir()
    {
        var gate = new SchemaReadyGate([]);

        await gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// SQL saglayicisi kayitliyken kapi <see cref="SchemaReadyGate.MarkReady"/>
    /// cagrilana kadar KAPALI kalir. Bu, kusurun ta kendisini kapatan davranistir.
    /// </summary>
    [Fact]
    public async Task SQL_saglayicisi_kayitliysa_kapi_MarkReady_oncesi_kapali_kalir()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);

        var waiter = gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.IsReady.ShouldBeFalse();
        waiter.IsCompleted.ShouldBeFalse("Migration bitmeden kapi acilmamalidir.");

        gate.MarkReady();

        await waiter;

        gate.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// Migration basarisiz olursa kapi hic acilmaz. Barindirici zaten kapanir;
    /// bekleyen servis iptal uzerinden cikar ve sonsuza dek asili kalmaz.
    /// </summary>
    [Fact]
    public async Task Kapi_acilmadan_iptal_edilirse_bekleyen_cikar()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("PostgreSQL")]);

        using var cts = new CancellationTokenSource();
        var waiter = gate.WaitAsync(cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiter);
        gate.IsReady.ShouldBeFalse();
    }

    /// <summary>Birden fazla <c>MarkReady</c> zararsizdir.</summary>
    [Fact]
    public async Task MarkReady_birden_fazla_cagrilabilir()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);

        gate.MarkReady();
        gate.MarkReady();

        await gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.IsReady.ShouldBeTrue();
    }

    /// <summary>Kapi acildiktan sonra gelen bekleyen hemen gecer.</summary>
    [Fact]
    public async Task Kapi_acildiktan_sonra_bekleyen_hemen_gecer()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SqlServer")]);

        gate.MarkReady();

        var waiter = gate.WaitAsync(TestContext.Current.CancellationToken);

        await waiter;
        waiter.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
