namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="ISkillScriptGrantStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 11'de eklendi. Bellek ici depo ile PostgreSQL deposu ayni senaryolari
/// gecmelidir; ozellikle "dar izin genis olani yener" kurali ve iptal edilen
/// iznin geri gelmemesi iki uygulamada da ayni davranmalidir.
/// </remarks>
public abstract class SkillScriptGrantContract : TenantIsolationContract<ISkillScriptGrantStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.GrantAsync(Grant(tenantId, name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.FindActiveAsync(tenantId, (string)key, "herhangi", DateTimeOffset.UtcNow) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    /// <remarks>Izin silinmez, iptal edilir (K-092).</remarks>
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.RevokeAsync(tenantId, (string)key, scriptName: null);

    [Fact]
    public async Task Skill_genelinde_verilen_izin_her_scripti_kapsar()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        var found = await Store.FindActiveAsync("tenant-a", "invoice", "any-script", DateTimeOffset.UtcNow);

        found.ShouldNotBeNull();
        found.ScriptName.ShouldBeNull();
    }

    [Fact]
    public async Task Scripte_ozgu_izin_skill_iznini_yener()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));
        await Store.GrantAsync(Grant("tenant-a", "invoice", "total"));

        var found = await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow);

        found.ShouldNotBeNull();
        string.Equals(found.ScriptName, "total", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Izin_kiracilar_arasinda_sizmaz()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        var found = await Store.FindActiveAsync("tenant-b", "invoice", "total", DateTimeOffset.UtcNow);

        found.ShouldBeNull();
    }

    [Fact]
    public async Task Suresi_dolmus_izin_dondurulmez()
    {
        var now = DateTimeOffset.UtcNow;
        await Store.GrantAsync(Grant("tenant-a", "invoice") with { ExpiresAt = now.AddMinutes(5) });

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", now)).ShouldNotBeNull();
        (await Store.FindActiveAsync("tenant-a", "invoice", "total", now.AddMinutes(10))).ShouldBeNull();
    }

    [Fact]
    public async Task Iptal_edilen_izin_gecersizdir()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        (await Store.RevokeAsync("tenant-a", "invoice", null)).ShouldBeTrue();

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldBeNull();
        (await Store.RevokeAsync("tenant-a", "invoice", null)).ShouldBeFalse();
    }

    [Fact]
    public async Task Ayni_izin_iki_kez_verilirse_tek_kayit_kalir()
    {
        // script_name NULL olabildigi icin benzersizlik COALESCE ile kurulur;
        // aksi halde PostgreSQL NULL'lari farkli sayar ve kopya satir birikirdi.
        await Store.GrantAsync(Grant("tenant-a", "invoice"));
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        var all = await Store.ListAsync("tenant-a");

        all.Count(grant => grant.ScriptName is null).ShouldBe(1);
    }

    [Fact]
    public async Task Iptal_edilen_izin_yeniden_verilebilir()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));
        await Store.RevokeAsync("tenant-a", "invoice", null);

        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull();
    }

    private static SkillScriptGrant Grant(string tenantId, string skillName, string? scriptName = null) => new()
    {
        TenantId = tenantId,
        SkillName = skillName,
        ScriptName = scriptName,
        GrantedBy = "admin@example.com",
        GrantedAt = DateTimeOffset.UtcNow,
    };
}
