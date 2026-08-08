namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IApiKeyStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// 🚨 Bu sozlesmede ham anahtar deger yalnizca <see cref="IApiKeyStore.CreateAsync"/>'in
/// dondurdugu <see cref="ApiKeyCreationResult.PlaintextKey"/>'de gorunur; hicbir
/// okuma yolu (<see cref="IApiKeyStore.ListAsync"/>, <see cref="IApiKeyStore.FindByHashAsync"/>)
/// ham degeri veya ozeti dondurmez (bolum 53.2).
/// </remarks>
public abstract class ApiKeyStoreContract : TenantIsolationContract<IApiKeyStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var created = await Store.CreateAsync(Draft(tenantId, name));

        return created.Record.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var keys = await Store.ListAsync(tenantId);

        return keys.Any(record => record.Id == (Guid)key);
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.RevokeAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Kaydedilen_anahtar_geri_okunur()
    {
        await Store.CreateAsync(Draft(Tenant, "ci", [ApiKeyScope.RunsRead, ApiKeyScope.AgentsRead]));

        var keys = await Store.ListAsync(Tenant);

        keys.Count.ShouldBe(1);
        keys[0].Name.ShouldBe("ci");
        keys[0].TenantId.ShouldBe(Tenant);
        keys[0].Scopes.ShouldBe([ApiKeyScope.RunsRead, ApiKeyScope.AgentsRead]);
        keys[0].KeyPrefix.ShouldStartWith("ap_");
        keys[0].RevokedAt.ShouldBeNull();
        keys[0].IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Ham_deger_ozetiyle_bulunur()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));
        var hash = ApiKeyGenerator.ComputeHash(created.PlaintextKey);

        var found = await Store.FindByHashAsync(hash);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(created.Record.Id);
    }

    [Fact]
    public async Task Bilinmeyen_ozet_bulunamaz()
    {
        await Store.CreateAsync(Draft(Tenant, "ci"));

        var randomHash = ApiKeyGenerator.ComputeHash("ap_baska_deger_hicbir_zaman_kaydedilmedi");

        (await Store.FindByHashAsync(randomHash)).ShouldBeNull();
    }

    [Fact]
    public async Task Iptal_edilen_anahtar_revoked_at_tasir_ve_pasif_gorunur()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));

        (await Store.RevokeAsync(Tenant, created.Record.Id)).ShouldBeTrue();

        var loaded = (await Store.ListAsync(Tenant)).Single(record => record.Id == created.Record.Id);

        loaded.RevokedAt.ShouldNotBeNull();
        loaded.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Ayni_anahtar_ikinci_kez_iptal_edilemez()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));

        (await Store.RevokeAsync(Tenant, created.Record.Id)).ShouldBeTrue();
        (await Store.RevokeAsync(Tenant, created.Record.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Baska_kiracinin_anahtarini_iptal_edemez()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));

        (await Store.RevokeAsync("other", created.Record.Id)).ShouldBeFalse();

        var loaded = (await Store.ListAsync(Tenant)).Single(record => record.Id == created.Record.Id);
        loaded.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Son_kullanim_damgasi_guncellenir()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));
        var usedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        await Store.TouchLastUsedAsync(created.Record.Id, usedAt);

        var loaded = (await Store.ListAsync(Tenant)).Single(record => record.Id == created.Record.Id);
        loaded.LastUsedAt.ShouldBe(usedAt);
    }

    [Fact]
    public async Task Sure_sonu_geri_okunur()
    {
        var expiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await Store.CreateAsync(Draft(Tenant, "ci") with { ExpiresAt = expiresAt });

        var loaded = (await Store.ListAsync(Tenant)).Single();
        loaded.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public async Task Aktif_kapsam_sistem_genelinde_bulunur()
    {
        (await Store.HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)).ShouldBeFalse();

        await Store.CreateAsync(Draft(Tenant, "mcp", [ApiKeyScope.ExternalInvoke]));

        (await Store.HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)).ShouldBeTrue();
        (await Store.HasActiveScopeAsync(ApiKeyScope.AgentsAdmin)).ShouldBeFalse();
    }

    [Fact]
    public async Task Iptal_edilen_anahtarin_kapsami_artik_aktif_sayilmaz()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "mcp", [ApiKeyScope.ExternalInvoke]));
        await Store.RevokeAsync(Tenant, created.Record.Id);

        (await Store.HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)).ShouldBeFalse();
    }

    [Fact]
    public async Task Baska_kiracinin_anahtari_ozetle_bulununca_dogru_kiraciyi_verir()
    {
        var created = await Store.CreateAsync(Draft("other", "ci"));
        var hash = ApiKeyGenerator.ComputeHash(created.PlaintextKey);

        var found = await Store.FindByHashAsync(hash);

        found.ShouldNotBeNull();
        found.TenantId.ShouldBe("other");
    }

    private static ApiKeyDraft Draft(string tenantId, string name, IReadOnlyList<ApiKeyScope>? scopes = null)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Scopes = scopes ?? [ApiKeyScope.RunsRead],
        };
}
