using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary><see cref="IJobScheduleStore"/> sozlesmesinin davranis testleri.</summary>
public abstract class JobScheduleStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected IJobScheduleStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    protected abstract ValueTask<IJobScheduleStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task SaveAsync_yeni_zamanlamaya_kimlik_atar()
    {
        var saved = await Store.SaveAsync(TestData.Schedule());

        saved.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task SaveAsync_ayni_ad_icin_kimligi_korur_ve_gunceller()
    {
        var first = await Store.SaveAsync(TestData.Schedule());
        var second = await Store.SaveAsync(TestData.Schedule() with { TargetName = "yeni-hedef" });

        second.Id.ShouldBe(first.Id);

        var fetched = await Store.GetAsync("default", "gece-raporu");
        fetched!.TargetName.ShouldBe("yeni-hedef");
    }

    [Fact]
    public async Task GetAsync_baska_kiracidan_null_doner()
    {
        await Store.SaveAsync(TestData.Schedule(tenantId: "kiraci-a"));

        (await Store.GetAsync("kiraci-b", "gece-raporu")).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_var_olani_siler()
    {
        await Store.SaveAsync(TestData.Schedule());

        (await Store.DeleteAsync("default", "gece-raporu")).ShouldBeTrue();
        (await Store.GetAsync("default", "gece-raporu")).ShouldBeNull();
    }

    [Fact]
    public async Task ListAsync_yalnizca_o_kiraciyi_getirir()
    {
        await Store.SaveAsync(TestData.Schedule(tenantId: "kiraci-a", name: "s1"));
        await Store.SaveAsync(TestData.Schedule(tenantId: "kiraci-b", name: "s2"));

        var list = await Store.ListAsync("kiraci-a");

        list.ShouldHaveSingleItem();
        list[0].Name.ShouldBe("s1");
    }

    [Fact]
    public async Task ListDueAsync_yalnizca_etkin_ve_zamani_gelmis_olanlari_getirir()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Schedule(name: "due") with { NextRunAt = now.AddMinutes(-1) });
        await Store.SaveAsync(TestData.Schedule(name: "not-due") with { NextRunAt = now.AddMinutes(5) });
        await Store.SaveAsync(TestData.Schedule(name: "disabled") with { NextRunAt = now.AddMinutes(-1), Enabled = false });

        var due = await Store.ListDueAsync(now);

        due.ShouldHaveSingleItem();
        due[0].Name.ShouldBe("due");
    }

    [Fact]
    public async Task TryClaimNextRunAsync_beklenen_deger_uyusmazsa_basarisiz_olur()
    {
        var now = DateTimeOffset.UtcNow;
        var saved = await Store.SaveAsync(TestData.Schedule() with { NextRunAt = now });

        var claimed = await Store.TryClaimNextRunAsync(saved.Id, now.AddMinutes(-1), now.AddHours(1), now);

        claimed.ShouldBeFalse();
        (await Store.GetAsync("default", "gece-raporu"))!.NextRunAt.ShouldBe(now);
    }

    [Fact]
    public async Task TryClaimNextRunAsync_ikinci_iddia_cakismayi_engeller()
    {
        var now = DateTimeOffset.UtcNow;
        var saved = await Store.SaveAsync(TestData.Schedule() with { NextRunAt = now });

        var first = await Store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(1), now);
        var second = await Store.TryClaimNextRunAsync(saved.Id, now, now.AddDays(2), now);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }
}
