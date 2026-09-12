namespace Tracon.Core.UnitTests.Storage;

public sealed class InMemoryAgentSkillStoreTests
{
    [Fact]
    public async Task Saving_assigns_an_id_timestamps_and_a_version()
    {
        var store = new InMemoryAgentSkillStore();

        var saved = await store.SaveAsync(CreateSkill("tenant-a", "invoicing"));

        saved.Id.ShouldNotBe(Guid.Empty);
        saved.Version.ShouldBe(1);
        saved.CreatedAt.ShouldNotBe(default);
        saved.UpdatedAt.ShouldBe(saved.CreatedAt);
    }

    [Fact]
    public async Task Updating_preserves_the_id_and_creation_time()
    {
        var store = new InMemoryAgentSkillStore();
        var first = await store.SaveAsync(CreateSkill("tenant-a", "invoicing"));

        var updated = await store.SaveAsync(first with { Description = "New description" });

        updated.Id.ShouldBe(first.Id);
        updated.CreatedAt.ShouldBe(first.CreatedAt);
        updated.Version.ShouldBe(2);
        updated.Description.ShouldBe("New description");
    }

    [Fact]
    public async Task Listing_is_scoped_to_the_tenant_and_sorted_by_name()
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(CreateSkill("tenant-a", "zeta"));
        await store.SaveAsync(CreateSkill("tenant-a", "alpha"));
        await store.SaveAsync(CreateSkill("tenant-b", "hidden"));

        var skills = await store.ListAsync("tenant-a");

        skills.Select(static skill => skill.Name).ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public async Task Deleting_removes_only_the_target_tenants_skill()
    {
        var store = new InMemoryAgentSkillStore();
        await store.SaveAsync(CreateSkill("tenant-a", "invoicing"));
        await store.SaveAsync(CreateSkill("tenant-b", "invoicing"));

        (await store.DeleteAsync("tenant-a", "invoicing")).ShouldBeTrue();

        (await store.GetAsync("tenant-a", "invoicing")).ShouldBeNull();
        (await store.GetAsync("tenant-b", "invoicing")).ShouldNotBeNull();
    }

    private static AgentSkillDefinition CreateSkill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Analyzes invoices.",
            Instructions = "Review invoices carefully.",
        };
}
