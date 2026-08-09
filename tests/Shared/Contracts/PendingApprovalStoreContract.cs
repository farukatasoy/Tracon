namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IPendingApprovalStore"/> sozlesmesinin davranis testleri (Faz 55).
/// </summary>
/// <remarks>
/// Bekleyen bir onay istegi bir <strong>guvenlik kaydidir</strong>: bir kiracinin
/// operatoru baska kiracinin onayini goremez ve VEREMEZ. <see cref="TryDeleteAsync"/>
/// kancasi buradaki mutasyon islemine — <see cref="IPendingApprovalStore.DecideAsync"/> —
/// baglanir; anlami "silme" degil "karar verme"dir, ama iki yonlu yalitim
/// denetiminin ihtiyaci ayni sekle sahiptir.
/// </remarks>
public abstract class PendingApprovalStoreContract : TenantIsolationContract<IPendingApprovalStore>
{
    /// <summary>
    /// Bir onay istegi yazilmadan once, verilen kimlikte bir calistirma satiri acar.
    /// </summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// SQL uygulamalarinda <c>pending_approvals.run_id</c> <c>runs</c> tablosuna
    /// yabanci anahtardir (<c>RunInputStoreContract.PrepareRunAsync</c> ile AYNI
    /// desen); bellek ici uygulamada boyle bir bag yoktur ve kanca hicbir sey yapmaz.
    /// </remarks>
    protected virtual ValueTask PrepareRunAsync(Guid runId, string tenantId) => default;

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var approval = await ApprovalAsync(tenantId, name);

        await Store.CreateAsync(approval);

        return approval.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return await Store.GetAsync((Guid)key) is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;

        return (await Store.ListPendingAsync()).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return await Store.DecideAsync((Guid)key, approved: true, "operator@ornek", Now);
    }

    [Fact]
    public async Task Olusturulan_istek_gecici_okunur()
    {
        var approval = await ApprovalAsync("kiraci-a", "siparis-iptal");

        await Store.CreateAsync(approval);

        AmbientTenant.TenantId = "kiraci-a";

        var loaded = await Store.GetAsync(approval.Id);

        loaded.ShouldNotBeNull();
        loaded.ToolName.ShouldBe("cancel_order");
        loaded.Status.ShouldBe(ApprovalStatus.Pending);
        loaded.SessionId.ShouldBe(approval.SessionId);
    }

    [Fact]
    public async Task Listeleme_yalniz_bekleyen_istekleri_dondurur()
    {
        AmbientTenant.TenantId = "kiraci-a";

        var pending = await ApprovalAsync("kiraci-a", "bekleyen");
        var decided = await ApprovalAsync("kiraci-a", "karara-baglanmis");

        await Store.CreateAsync(pending);
        await Store.CreateAsync(decided);
        await Store.DecideAsync(decided.Id, approved: true, "operator@ornek", Now);

        var listed = (await Store.ListPendingAsync()).ShouldHaveSingleItem();

        listed.Id.ShouldBe(pending.Id);
    }

    [Fact]
    public async Task Karar_durumu_ve_aktoru_gunceller()
    {
        AmbientTenant.TenantId = "kiraci-a";

        var approval = await ApprovalAsync("kiraci-a", "siparis-iptal");
        await Store.CreateAsync(approval);

        var decidedAt = Now;
        var applied = await Store.DecideAsync(approval.Id, approved: true, "operator@ornek", decidedAt);

        applied.ShouldBeTrue();

        var loaded = await Store.GetAsync(approval.Id);

        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(ApprovalStatus.Approved);
        loaded.DecidedBy.ShouldBe("operator@ornek");
        loaded.DecidedAt.ShouldBe(decidedAt);
    }

    [Fact]
    public async Task Ikinci_karar_reddedilir()
    {
        AmbientTenant.TenantId = "kiraci-a";

        var approval = await ApprovalAsync("kiraci-a", "siparis-iptal");
        await Store.CreateAsync(approval);

        (await Store.DecideAsync(approval.Id, approved: true, "operator-1@ornek", Now)).ShouldBeTrue();
        (await Store.DecideAsync(approval.Id, approved: false, "operator-2@ornek", Now)).ShouldBeFalse();

        // Ilk karar korunur; ikinci deneme UZERINE YAZMAZ.
        (await Store.GetAsync(approval.Id))!.DecidedBy.ShouldBe("operator-1@ornek");
    }

    [Fact]
    public async Task Suresi_dolan_istek_kapatilir_ve_dondurulur()
    {
        AmbientTenant.TenantId = "kiraci-a";

        var expired = (await ApprovalAsync("kiraci-a", "suresi-dolan")) with { ExpiresAt = Now - TimeSpan.FromMinutes(1) };
        var fresh = (await ApprovalAsync("kiraci-a", "taze")) with { ExpiresAt = Now + TimeSpan.FromHours(1) };

        await Store.CreateAsync(expired);
        await Store.CreateAsync(fresh);

        var closed = (await Store.ExpireAsync(Now, max: 100)).ShouldHaveSingleItem();

        closed.Id.ShouldBe(expired.Id);
        closed.Status.ShouldBe(ApprovalStatus.Expired);

        (await Store.ListPendingAsync()).ShouldHaveSingleItem().Id.ShouldBe(fresh.Id);
    }

    [Fact]
    public async Task Sure_sonu_taramasi_max_sinirini_asmaz()
    {
        AmbientTenant.TenantId = "kiraci-a";

        for (var i = 0; i < 3; i++)
        {
            var approval = (await ApprovalAsync("kiraci-a", $"suresi-dolan-{i}")) with
            {
                ExpiresAt = Now - TimeSpan.FromMinutes(1),
            };

            await Store.CreateAsync(approval);
        }

        (await Store.ExpireAsync(Now, max: 2)).Count.ShouldBe(2);
    }

    private static DateTimeOffset Now => new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private async ValueTask<PendingApproval> ApprovalAsync(string tenantId, string name)
    {
        var runId = Guid.NewGuid();

        await PrepareRunAsync(runId, tenantId);

        return new PendingApproval
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RunId = runId,
            SessionId = $"session-{name}",
            RequestId = $"request-{name}",
            ToolName = "cancel_order",
            Status = ApprovalStatus.Pending,
            ExpiresAt = Now + TimeSpan.FromHours(24),
            CreatedAt = Now,
        };
    }
}
