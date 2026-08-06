namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IToolApprovalRuleStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 41'de eklendi. Kalici onay kurali bir <strong>guvenlik kaydidir</strong>:
/// bir kiracinin kurali digerinin tool cagrisini onaylatmadan gecirmemelidir.
/// </remarks>
public abstract class ToolApprovalRuleStoreContract : TenantIsolationContract<IToolApprovalRuleStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
        => (await Store.AddAsync(Rule(tenantId, name))).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => (await Store.ListAsync(tenantId)).Any(rule => rule.Id == (Guid)key);

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Eklenen_kural_geri_okunur()
    {
        var added = await Store.AddAsync(Rule("kiraci-a", "get_order"));

        added.Id.ShouldNotBe(Guid.Empty);

        var loaded = (await Store.ListAsync("kiraci-a")).ShouldHaveSingleItem();

        loaded.ToolName.ShouldBe("get_order");
        loaded.AgentName.ShouldBe("destek");
        loaded.CreatedBy.ShouldBe("operator@ornek");
    }

    [Fact]
    public async Task Ayni_kapsam_ikinci_kez_eklenirse_tek_kayit_kalir()
    {
        // agent_name ve arguments_hash NULL olabilir; benzersizlik NULL
        // semantigi dogru kurulmazsa kopya satir birikir.
        await Store.AddAsync(Rule("kiraci-a", "get_order") with { AgentName = null, ArgumentsHash = null });
        await Store.AddAsync(Rule("kiraci-a", "get_order") with { AgentName = null, ArgumentsHash = null });

        (await Store.ListAsync("kiraci-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Farkli_agent_ayri_kural_olur()
    {
        await Store.AddAsync(Rule("kiraci-a", "get_order") with { AgentName = null });
        await Store.AddAsync(Rule("kiraci-a", "get_order") with { AgentName = "destek" });

        (await Store.ListAsync("kiraci-a")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Silme_olmayan_kuralda_false_doner()
        => (await Store.DeleteAsync("kiraci-a", Guid.NewGuid())).ShouldBeFalse();

    private static ToolApprovalRule Rule(string tenantId, string toolName)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentName = "destek",
            ToolName = toolName,
            CreatedBy = "operator@ornek",
            CreatedAt = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
        };
}

/// <summary>
/// <see cref="IMcpServerStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 41'de eklendi. MCP sunucu kaydi disaridan tool tanimi kabul eder; bir
/// kiracinin sunucusunun digerinin katalogunda gorunmemesi bir guvenlik
/// sinirdir. Kayit <strong>hicbir zaman sir tasimaz</strong> (K-059) --
/// yalnizca degerin okunacagi yapilandirma anahtarinin adi durur.
/// </remarks>
public abstract class McpServerStoreContract : TenantIsolationContract<IMcpServerStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(Server(tenantId, name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task Kaydedilen_sunucu_geri_okunur()
    {
        await Store.SaveAsync(Server("kiraci-a", "github"));

        var loaded = await Store.GetAsync("kiraci-a", "github");

        loaded.ShouldNotBeNull();
        loaded.Endpoint.ToString().ShouldBe("https://ornek.test/mcp");
        loaded.Enabled.ShouldBeTrue();
        loaded.AuthorizationConfigurationKey.ShouldBe("AgentPrism:Mcp:GithubToken");
    }

    [Fact]
    public async Task Ayni_ad_ikinci_kez_kaydedilince_uzerine_yazilir()
    {
        await Store.SaveAsync(Server("kiraci-a", "github"));
        await Store.SaveAsync(Server("kiraci-a", "github") with { Description = "guncellendi" });

        (await Store.GetAsync("kiraci-a", "github"))!.Description.ShouldBe("guncellendi");
        (await Store.ListAsync("kiraci-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Olmayan_sunucu_null_doner()
        => (await Store.GetAsync("kiraci-a", "yok")).ShouldBeNull();

    [Fact]
    public async Task Silme_olmayan_kayitta_false_doner()
        => (await Store.DeleteAsync("kiraci-a", "yok")).ShouldBeFalse();

    private static McpServerDefinition Server(string tenantId, string name)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = "Ornek sunucu.",
            Endpoint = new Uri("https://ornek.test/mcp"),
            AuthorizationConfigurationKey = "AgentPrism:Mcp:GithubToken",
            Enabled = true,
        };
}
