using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Scheduling;

public sealed class InMemoryJobStoreTests
{
    [Fact]
    public async Task Kuyruga_eklenen_is_beklemede_baslar()
    {
        var store = new InMemoryJobStore();

        var job = await store.EnqueueAsync(NewJob(), ["a", "b", "c"]);

        job.Status.ShouldBe(JobStatus.Pending);
        job.TotalItems.ShouldBe(3);
        job.Attempt.ShouldBe(0);

        var items = await store.ListItemsAsync(job.Id);
        items.Count.ShouldBe(3);
        items.Select(static i => i.Seq).ShouldBe([0, 1, 2]);
        items.ShouldAllBe(static i => i.Status == JobItemStatus.Pending);
    }

    [Fact]
    public async Task Zamani_gelmemis_is_kiralanamaz()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryJobStore(clock);

        await store.EnqueueAsync(
            NewJob() with { ScheduledFor = clock.GetUtcNow() + TimeSpan.FromMinutes(5) },
            ["a"]);

        (await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Kiralama_durumu_leased_yapar_ve_denemeyi_artirir()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);

        var leased = await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));

        leased.ShouldNotBeNull();
        leased.Id.ShouldBe(job.Id);
        leased.Status.ShouldBe(JobStatus.Leased);
        leased.LeaseOwner.ShouldBe("isci-1");
        leased.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Kirali_is_suresi_dolmadan_baska_isciye_verilmez()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(NewJob(), ["a"]);

        await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));

        (await store.LeaseAsync("isci-2", TimeSpan.FromMinutes(5))).ShouldBeNull();
    }

    [Fact]
    public async Task Kira_suresi_dolunca_is_yeniden_kiralanabilir()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryJobStore(clock);
        var job = await store.EnqueueAsync(NewJob() with { ScheduledFor = clock.GetUtcNow() }, ["a"]);

        await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(6));

        var reclaimed = await store.LeaseAsync("isci-2", TimeSpan.FromMinutes(5));

        reclaimed.ShouldNotBeNull();
        reclaimed.Id.ShouldBe(job.Id);
        reclaimed.LeaseOwner.ShouldBe("isci-2");
        reclaimed.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task MarkRunningAsync_yalnizca_gercek_sahibi_icin_calisir()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);
        await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));

        (await store.MarkRunningAsync(job.Id, "yanlis-isci")).ShouldBeFalse();
        (await store.MarkRunningAsync(job.Id, "isci-1")).ShouldBeTrue();

        var current = await store.GetAsync("kiraci", job.Id);
        current!.Status.ShouldBe(JobStatus.Running);
    }

    [Fact]
    public async Task ReportItemAsync_idempotenttir()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a", "b"]);

        var result = new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed, RunId = Guid.NewGuid() };

        await store.ReportItemAsync(result);
        await store.ReportItemAsync(result); // kira suresi dolup yeniden raporlama senaryosu

        var current = await store.GetAsync("kiraci", job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(0);
    }

    [Fact]
    public async Task ReportItemAsync_basarisiz_ogeyi_ayri_sayar()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a", "b"]);

        await store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed });
        await store.ReportItemAsync(new JobItemResult { JobId = job.Id, Seq = 1, Status = JobItemStatus.Failed, Error = "patladi" });

        var current = await store.GetAsync("kiraci", job.Id);
        current!.DoneItems.ShouldBe(1);
        current.FailedItems.ShouldBe(1);

        var items = await store.ListItemsAsync(job.Id);
        items.Single(static i => i.Seq == 1).Error.ShouldBe("patladi");
    }

    [Fact]
    public async Task CompleteAsync_kirayi_birakir()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryJobStore(clock);
        var job = await store.EnqueueAsync(NewJob(), ["a"]);
        await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));

        await store.CompleteAsync(new JobCompletion
        {
            JobId = job.Id,
            Status = JobStatus.Completed,
            CompletedAt = clock.GetUtcNow(),
        });

        var current = await store.GetAsync("kiraci", job.Id);
        current!.Status.ShouldBe(JobStatus.Completed);
        current.LeaseOwner.ShouldBeNull();
        current.LeaseUntil.ShouldBeNull();
    }

    [Fact]
    public async Task ReleaseForRetryAsync_denemeyi_sifirlamadan_beklemeye_alir()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);
        await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));

        await store.ReleaseForRetryAsync(job.Id, "gecici hata");

        var current = await store.GetAsync("kiraci", job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
        current.Attempt.ShouldBe(1); // LeaseAsync zaten artirmisti; burada degismez.
        current.ErrorMessage.ShouldBe("gecici hata");

        (await store.LeaseAsync("isci-2", TimeSpan.FromMinutes(5))).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(JobStatus.Pending, true)]
    [InlineData(JobStatus.Leased, true)]
    [InlineData(JobStatus.Running, true)]
    [InlineData(JobStatus.Completed, false)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Cancelled, false)]
    public async Task CancelAsync_yalnizca_uygun_durumlarda_calisir(JobStatus status, bool expected)
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(), ["a"]);

        // Durumu dolayli olarak kurmak icin CompleteAsync/LeaseAsync kullanilir.
        if (status is JobStatus.Completed or JobStatus.Failed)
        {
            await store.CompleteAsync(new JobCompletion { JobId = job.Id, Status = status, CompletedAt = DateTimeOffset.UtcNow });
        }
        else if (status == JobStatus.Cancelled)
        {
            await store.CancelAsync("kiraci", job.Id);
        }
        else if (status is JobStatus.Leased or JobStatus.Running)
        {
            await store.LeaseAsync("isci-1", TimeSpan.FromMinutes(5));

            if (status == JobStatus.Running)
            {
                await store.MarkRunningAsync(job.Id, "isci-1");
            }
        }

        (await store.CancelAsync("kiraci", job.Id)).ShouldBe(expected);
    }

    [Fact]
    public async Task CancelAsync_baska_kiracinin_isini_etkilemez()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob() with { TenantId = "kiraci-a" }, ["a"]);

        (await store.CancelAsync("kiraci-b", job.Id)).ShouldBeFalse();

        var current = await store.GetAsync("kiraci-a", job.Id);
        current!.Status.ShouldBe(JobStatus.Pending);
    }

    [Fact]
    public async Task QueryAsync_kiraciya_gore_filtreler()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(NewJob() with { TenantId = "a" }, ["x"]);
        await store.EnqueueAsync(NewJob() with { TenantId = "b" }, ["x"]);

        var results = await store.QueryAsync(new JobQuery { TenantId = "a" });

        results.ShouldHaveSingleItem();
        results[0].TenantId.ShouldBe("a");
    }

    private static JobRecord NewJob()
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = "kiraci",
            Kind = JobKind.AgentBatch,
            TargetName = "ozetleyici",
            Status = JobStatus.Pending,
            ScheduledFor = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
