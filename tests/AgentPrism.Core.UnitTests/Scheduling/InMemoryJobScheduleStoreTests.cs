namespace AgentPrism.Core.UnitTests.Scheduling;

public sealed class InMemoryJobScheduleStoreTests
{
    [Fact]
    public async Task SaveAsync_yeni_zamanlamaya_kimlik_atar()
    {
        var store = new InMemoryJobScheduleStore();

        var saved = await store.SaveAsync(NewSchedule());

        saved.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_ayni_ad_icin_kimligi_korur()
    {
        var store = new InMemoryJobScheduleStore();

        var first = await store.SaveAsync(NewSchedule());
        var second = await store.SaveAsync(NewSchedule() with { TargetName = "yeni-hedef" });

        second.Id.ShouldBe(first.Id);

        var fetched = await store.GetAsync("kiraci", "gece-raporu");
        fetched!.TargetName.ShouldBe("yeni-hedef");
    }

    [Fact]
    public async Task DeleteAsync_var_olmayan_zamanlamada_false_doner()
    {
        var store = new InMemoryJobScheduleStore();

        (await store.DeleteAsync("kiraci", "yok")).ShouldBeFalse();
    }

    [Fact]
    public async Task ListAsync_yalnizca_o_kiraciyi_getirir()
    {
        var store = new InMemoryJobScheduleStore();
        await store.SaveAsync(NewSchedule() with { TenantId = "a", Name = "s1" });
        await store.SaveAsync(NewSchedule() with { TenantId = "b", Name = "s2" });

        var list = await store.ListAsync("a");

        list.ShouldHaveSingleItem();
        list[0].Name.ShouldBe("s1");
    }

    [Fact]
    public async Task ListDueAsync_yalnizca_etkin_cronlu_ve_zamani_gelmis_olanlari_getirir()
    {
        var store = new InMemoryJobScheduleStore();
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(NewSchedule() with { Name = "due", NextRunAt = now.AddMinutes(-1) });
        await store.SaveAsync(NewSchedule() with { Name = "not-due", NextRunAt = now.AddMinutes(1) });
        await store.SaveAsync(NewSchedule() with { Name = "disabled", NextRunAt = now.AddMinutes(-1), Enabled = false });
        await store.SaveAsync(NewSchedule() with { Name = "manual-only", Cron = null, NextRunAt = null });

        var due = await store.ListDueAsync(now);

        due.ShouldHaveSingleItem();
        due[0].Name.ShouldBe("due");
    }

    [Fact]
    public async Task TryClaimNextRunAsync_beklenen_deger_uyusmazsa_basarisiz_olur()
    {
        var store = new InMemoryJobScheduleStore();
        var now = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(NewSchedule() with { NextRunAt = now });

        // Baska bir ornek zaten ilerletmis gibi: beklenen deger artik gecerli degil.
        var claimed = await store.TryClaimNextRunAsync(saved.Id, now.AddMinutes(-1), now.AddHours(1), now);

        claimed.ShouldBeFalse();

        var current = await store.GetAsync("kiraci", "gece-raporu");
        current!.NextRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_basarili_olunca_bir_sonraki_calismayi_ilerletir()
    {
        var store = new InMemoryJobScheduleStore();
        var now = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(NewSchedule() with { NextRunAt = now });

        var claimed = await store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);

        claimed.ShouldBeTrue();

        var current = await store.GetAsync("kiraci", "gece-raporu");
        current!.NextRunAt.ShouldBe(now.AddDays(1));
        current.LastRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_ikinci_iddia_cakismayi_engeller()
    {
        // Iki es zamanli "isci" ayni zamanlamayi tetiklemeye calisiyor; yalnizca
        // ilki basarili olmalidir. Bellek ici depoda tam eslerlik kaniti degildir
        // (bkz. JobStoreContract'taki gercek Postgres testi) ama CAS mantigini
        // dogrular.
        var store = new InMemoryJobScheduleStore();
        var now = DateTimeOffset.UtcNow;
        var saved = await store.SaveAsync(NewSchedule() with { NextRunAt = now });

        var first = await store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);
        var second = await store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(2), now);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    private static JobSchedule NewSchedule()
        => new()
        {
            TenantId = "kiraci",
            Name = "gece-raporu",
            Kind = JobKind.AgentBatch,
            TargetName = "ozetleyici",
            Cron = "0 3 * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
