namespace Tracon.Testing.Contracts.Storage;

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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.SaveAsync(Skill(tenantId, name), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    /// <summary>
    /// The name is the client's: names that differ only in letter case are two records. A store that folds
    /// case merges them, and the second save replaces the first without a sign.
    /// </summary>
    [Fact]
    public Task Names_that_differ_only_in_case_are_two_skills()
        => AssertNamesThatDifferOnlyInCaseAreSeparateRecordsAsync();

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

    /// <summary>
    /// What <c>SaveAsync</c> hands back is the skill as the store holds it, the same record <c>GetAsync</c> reports an
    /// instant later: a SQL store writes U+0000 as U+FFFD, and a <c>jsonb</c> parameter schema loses its spaces.
    /// </summary>
    [Fact]
    public async Task The_saved_skill_is_the_skill_a_read_reports()
    {
        var saved = await Store.SaveAsync(Skill("tenant-a", "invoice") with
        {
            Resources =
            [
                new AgentSkillResourceDefinition { Name = "notes.txt", Content = "a\0b" },
            ],
            Scripts =
            [
                new AgentSkillScriptDefinition
                {
                    Name = "total",
                    Extension = "py",
                    Content = "print(1)\0",
                    ParametersSchema = "{ \"type\" :  \"object\" }",
                },
            ],
        });
        var read = (await Store.GetAsync("tenant-a", "invoice")).ShouldNotBeNull();

        saved.Resources.Select(static resource => resource.Content).ShouldBe(read.Resources.Select(static resource => resource.Content));
        saved.Scripts.Select(static script => script.Content).ShouldBe(read.Scripts.Select(static script => script.Content));
        saved.Scripts.Select(static script => script.ParametersSchema).ShouldBe(read.Scripts.Select(static script => script.ParametersSchema));
        saved.UpdatedAt.ShouldBe(read.UpdatedAt);
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
