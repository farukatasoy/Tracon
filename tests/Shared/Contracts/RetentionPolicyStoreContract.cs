namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IRetentionPolicyStore"/> sozlesmesinin davranis testleri.
/// </summary>
public abstract class RetentionPolicyStoreContract : TenantIsolationContract<IRetentionPolicyStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var policy = await Store.SavePolicyAsync(Policy(tenantId));

        var run = await Store.CreateRunAsync(Run() with { TenantId = tenantId });
        await Store.AppendRunProgressAsync(run.Id, deletedDelta: 1, archivedDelta: 0);
        await Store.CompleteRunAsync(run.Id, DateTimeOffset.UtcNow, errorMessage: null);

        return policy.Target;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var policy = await Store.GetPolicyAsync(tenantId, (string)key);

        // Kosu gecmisi de kiraciya aittir.
        var runs = await Store.ListRunsAsync(tenantId, (string)key, skip: 0, take: 50);
        (runs.Count > 0).ShouldBe(policy is not null);

        return policy is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListPoliciesAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeletePolicyAsync(tenantId, (string)key);

    /// <inheritdoc />
    /// <remarks>
    /// Politikanin benzersizligi <c>(kiraci, hedef)</c> ciftindedir; ad bir
    /// ayirt edici degildir. Ikinci kiraci ayni hedefe kendi politikasini yazar.
    /// </remarks>
    protected override async ValueTask<bool> TryOverwriteAsync(string tenantId, string name)
    {
        await SeedAsync(tenantId, name);
        return true;
    }

    private const string Tenant = "test";

    [Fact]
    public async Task Kaydedilen_politika_geri_okunur()
    {
        await Store.SavePolicyAsync(Policy(maxAgeDays: 30, archive: true));

        var loaded = await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents);

        loaded.ShouldNotBeNull();
        loaded.MaxAgeDays.ShouldBe(30);
        loaded.Archive.ShouldBeTrue();
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Ayni_hedef_ikinci_kez_kaydedilince_uzerine_yazilir()
    {
        await Store.SavePolicyAsync(Policy(maxAgeDays: 30));
        await Store.SavePolicyAsync(Policy(maxAgeDays: 7));

        var all = await Store.ListPoliciesAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].MaxAgeDays.ShouldBe(7);
    }

    [Fact]
    public async Task Kiraciya_ozel_kayit_yoksa_yildiz_genelinine_duser()
    {
        await Store.SavePolicyAsync(Policy(tenantId: "*", maxAgeDays: 14));

        var resolved = await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.TenantId.ShouldBe("*");
        resolved.MaxAgeDays.ShouldBe(14);
    }

    [Fact]
    public async Task Kiraciya_ozel_kayit_yildizdan_once_gelir()
    {
        await Store.SavePolicyAsync(Policy(tenantId: "*", maxAgeDays: 30));
        await Store.SavePolicyAsync(Policy(tenantId: Tenant, maxAgeDays: 7));

        var resolved = await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.TenantId.ShouldBe(Tenant);
        resolved.MaxAgeDays.ShouldBe(7);
    }

    [Fact]
    public async Task Baska_kiracinin_politikasi_gorunmez()
    {
        await Store.SavePolicyAsync(Policy(tenantId: Tenant));

        (await Store.GetPolicyAsync("other", RetentionTargets.RunEvents)).ShouldBeNull();
        (await Store.ListPoliciesAsync("other")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Silinen_politika_geri_okunmaz()
    {
        await Store.SavePolicyAsync(Policy());

        (await Store.DeletePolicyAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeTrue();
        (await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeNull();
    }

    [Fact]
    public async Task Kosu_olusturulur_ve_ilerleme_birikir()
    {
        var run = await Store.CreateRunAsync(Run());

        await Store.AppendRunProgressAsync(run.Id, deletedDelta: 100, archivedDelta: 40);
        await Store.AppendRunProgressAsync(run.Id, deletedDelta: 50, archivedDelta: 0);
        await Store.CompleteRunAsync(run.Id, DateTimeOffset.UtcNow, errorMessage: null);

        var history = await Store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);

        history.Count.ShouldBe(1);
        history[0].DeletedRows.ShouldBe(150);
        history[0].ArchivedRows.ShouldBe(40);
        history[0].CompletedAt.ShouldNotBeNull();
        history[0].Error.ShouldBeNull();
    }

    [Fact]
    public async Task Basarisiz_kosu_hata_mesaji_tasir()
    {
        var run = await Store.CreateRunAsync(Run());

        await Store.CompleteRunAsync(run.Id, DateTimeOffset.UtcNow, "baglanti koptu");

        var history = await Store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);

        history[0].Error.ShouldBe("baglanti koptu");
    }

    [Fact]
    public async Task Kosu_gecmisi_en_yeni_basta_doner()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.CreateRunAsync(Run(startedAt: now.AddMinutes(-10)));
        var latest = await Store.CreateRunAsync(Run(startedAt: now));

        var history = await Store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);

        history[0].Id.ShouldBe(latest.Id);
    }

    private static RetentionPolicy Policy(
        string tenantId = Tenant,
        int? maxAgeDays = 30,
        bool archive = false)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = maxAgeDays,
            Archive = archive,
            Enabled = true,
            CreatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
        };

    private static RetentionRun Run(DateTimeOffset? startedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            StartedAt = startedAt ?? new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
        };
}
