namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IAgentSkillStore"/> contract.
/// </summary>
/// <remarks>
/// Protects both behavior parity across implementations and tenant
/// isolation.
/// </remarks>
public abstract class AgentSkillStoreContract : TenantIsolationContract<IAgentSkillStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(Skill(tenantId, name));
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
    public async Task Saved_skill_is_read_back()
    {
        var saved = await Store.SaveAsync(Skill("tenant-a", "invoice"));

        saved.Id.ShouldNotBe(Guid.Empty);

        var loaded = await Store.GetAsync("tenant-a", "invoice");

        loaded.ShouldNotBeNull();
        loaded.Description.ShouldBe("Invoice review.");
        loaded.Instructions.ShouldBe("Check every invoice.");
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_the_same_name_again_increments_version()
    {
        var first = await Store.SaveAsync(Skill("tenant-a", "invoice"));
        var second = await Store.SaveAsync(Skill("tenant-a", "invoice") with { Instructions = "updated" });

        second.Id.ShouldBe(first.Id);
        second.Version.ShouldBeGreaterThan(first.Version);

        (await Store.GetAsync("tenant-a", "invoice"))!.Instructions.ShouldBe("updated");
        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Missing_skill_returns_null()
        => (await Store.GetAsync("tenant-a", "missing")).ShouldBeNull();

    [Fact]
    public async Task Deleting_a_missing_record_returns_false()
        => (await Store.DeleteAsync("tenant-a", "missing")).ShouldBeFalse();

    [Fact]
    public async Task List_is_sorted_by_name()
    {
        await Store.SaveAsync(Skill("tenant-a", "zeta"));
        await Store.SaveAsync(Skill("tenant-a", "alpha"));

        var all = await Store.ListAsync("tenant-a");

        all.Count.ShouldBe(2);
        all[0].Name.ShouldBe("alpha");
    }

    [Fact]
    public async Task Disabled_skill_reads_back_disabled()
    {
        await Store.SaveAsync(Skill("tenant-a", "invoice") with { Enabled = false });

        (await Store.GetAsync("tenant-a", "invoice"))!.Enabled.ShouldBeFalse();
    }

    private static AgentSkillDefinition Skill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Invoice review.",
            Instructions = "Check every invoice.",
        };
}
