
namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IJobStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir. Kira
/// suresi dolma testi gercek zamanla calisir (kisa bir kira + kisa bir
/// bekleme): PostgreSQL uygulamasi kendi saatini enjekte edilebilir bir
/// <see cref="TimeProvider"/> uzerinden almaz, bu yuzden sahte zaman burada
/// kullanilamaz.
/// </remarks>
public abstract class JobStoreContract : TenantIsolationContract<IJobStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
        => (await Store.EnqueueAsync(TestData.Job(tenantId), [name])).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (Guid)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.QueryAsync(new JobQuery { TenantId = tenantId })).Count;

    /// <inheritdoc />
    /// <remarks>
    /// Is kuyrugunda silme yoktur; kiraciya ait tek yikici islem iptaldir.
    /// </remarks>
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.CancelAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Kuyruga_eklenen_is_beklemede_baslar_ve_ogeleri_olusturur()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a", "b", "c"]);

        job.Status.ShouldBe(JobStatus.Pending);
        job.TotalItems.ShouldBe(3);
        job.Attempt.ShouldBe(0);

        var items = await Store.ListItemsAsync(job.Id);
        items.Count.ShouldBe(3);
        items.Select(static i => i.Seq).ShouldBe([0, 1, 2]);
        items.Select(static i => i.Input).ShouldBe(["a", "b", "c"]);
        items.ShouldAllBe(static i => i.Status == JobItemStatus.Pending);
    }

    [Fact]
    public async Task Zamani_gelmemis_is_kiralanamaz()
    {
        await Store.EnqueueAsync(TestData.Job(scheduledFor: DateTimeOffset.UtcNow.AddMinutes(5)), ["a"]);

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Kiralama_leased_yapar_ve_denemeyi_artirir()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();

        var leased = await Store.LeaseAsync(owner, TimeSpan.FromMinutes(5));

        leased.ShouldNotBeNull();
        leased.Id.ShouldBe(job.Id);
        leased.Status.ShouldBe(JobStatus.Leased);
        leased.LeaseOwner.ShouldBe(owner);
        leased.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Kirali_is_suresi_dolmadan_baska_isciye_verilmez()
    {
        await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Kira_suresi_dolunca_is_yeniden_kiralanabilir()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        await Store.LeaseAsync(Owner(), TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var reclaimed = await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        reclaimed.ShouldNotBeNull();
        reclaimed.Id.ShouldBe(job.Id);
        reclaimed.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task MarkRunningAsync_yalnizca_gercek_sahibi_icin_calisir()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        var owner = Owner();
        await Store.LeaseAsync(owner, TimeSpan.FromMinutes(5));

        (await Store.MarkRunningAsync(job.Id, Owner())).ShouldBeFalse();
        (await Store.MarkRunningAsync(job.Id, owner)).ShouldBeTrue();

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Running);
    }

    [Fact]
    public async Task ReportItemAsync_idempotenttir()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a", "b"]);
        var result = new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed, RunId = Guid.NewGuid() };

        await Store.ReportItemAsync(result);
        await Store.ReportItemAsync(result);

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(0);
    }

    [Fact]
    public async Task ReportItemAsync_basarili_ve_basarisiz_ogeleri_ayri_sayar()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a", "b"]);

        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed });
        await Store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 1, Status = JobItemStatus.Failed, Error = "patladi" });

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(1);

        var items = await Store.ListItemsAsync(job.Id);
        items.Single(static i => i.Seq == 1).Error.ShouldBe("patladi");
        items.Single(static i => i.Seq == 1).Status.ShouldBe(JobItemStatus.Failed);
    }

    [Fact]
    public async Task CompleteAsync_kirayi_birakir()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.CompleteAsync(new JobCompletion
        {
            JobId = job.Id,
            Status = JobStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Completed);
        current.LeaseOwner.ShouldBeNull();
        current.LeaseUntil.ShouldBeNull();
        current.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ReleaseForRetryAsync_denemeyi_sifirlamadan_beklemeye_alir()
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);
        await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5));

        await Store.ReleaseForRetryAsync(job.Id, "gecici hata");

        var current = await Store.GetAsync(job.TenantId, job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
        current.Attempt.ShouldBe(1);
        current.ErrorMessage.ShouldBe("gecici hata");

        (await Store.LeaseAsync(Owner(), TimeSpan.FromMinutes(5))).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(JobStatus.Pending, true)]
    [InlineData(JobStatus.Completed, false)]
    [InlineData(JobStatus.Failed, false)]
    public async Task CancelAsync_yalnizca_uygun_durumlarda_calisir(JobStatus status, bool expected)
    {
        var job = await Store.EnqueueAsync(TestData.Job(), ["a"]);

        if (status is JobStatus.Completed or JobStatus.Failed)
        {
            await Store.CompleteAsync(new JobCompletion { JobId = job.Id, Status = status, CompletedAt = DateTimeOffset.UtcNow });
        }

        (await Store.CancelAsync(job.TenantId, job.Id)).ShouldBe(expected);
    }

    [Fact]
    public async Task CancelAsync_baska_kiracinin_isini_etkilemez()
    {
        var job = await Store.EnqueueAsync(TestData.Job(tenantId: "kiraci-a"), ["a"]);

        (await Store.CancelAsync("kiraci-b", job.Id)).ShouldBeFalse();

        var current = await Store.GetAsync("kiraci-a", job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
    }

    [Fact]
    public async Task GetAsync_baska_kiracidan_null_doner()
    {
        var job = await Store.EnqueueAsync(TestData.Job(tenantId: "kiraci-a"), ["a"]);

        (await Store.GetAsync("kiraci-b", job.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task QueryAsync_kiraciya_gore_filtreler()
    {
        await Store.EnqueueAsync(TestData.Job(tenantId: "kiraci-a"), ["a"]);
        await Store.EnqueueAsync(TestData.Job(tenantId: "kiraci-b"), ["a"]);

        var results = await Store.QueryAsync(new JobQuery { TenantId = "kiraci-a" });

        results.ShouldHaveSingleItem();
        results[0].TenantId.ShouldBe("kiraci-a");
    }

    private static string Owner() => $"isci-{Guid.NewGuid():N}";
}
