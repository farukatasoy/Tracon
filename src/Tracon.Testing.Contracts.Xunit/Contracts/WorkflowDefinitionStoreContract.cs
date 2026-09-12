namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IWorkflowDefinitionStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the PostgreSQL store must pass the same
/// scenarios. In particular, version increment and tenant isolation must
/// behave identically in both implementations.
/// </remarks>
public abstract class WorkflowDefinitionStoreContract : TenantIsolationContract<IWorkflowDefinitionStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(tenantId, Definition() with { Name = name });
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
    public async Task Saved_definition_is_read_back()
    {
        var saved = await Store.SaveAsync("tenant-a", Definition());

        saved.Version.ShouldBe(1);
        saved.TenantId.ShouldBe("tenant-a");

        var loaded = await Store.GetAsync("tenant-a", "review");

        loaded.ShouldNotBeNull();
        loaded.Kind.ShouldBe(WorkflowKind.Sequential);
        loaded.AgentNames.ShouldBe(["researcher", "writer", "editor"]);
        loaded.Description.ShouldBe("Three-step review.");
    }

    [Fact]
    public async Task All_fields_survive_the_round_trip()
    {
        // 🚨 The jsonb payload goes through a HAND-WRITTEN DTO. If a new
        // field is added to the definition and the DTO is not updated, the
        // field silently disappears; neither compilation nor any other test
        // catches it. This is the whole reason this test exists.
        var definition = new WorkflowDefinition
        {
            Name = "full",
            DisplayName = "Full Definition",
            Description = "All fields populated.",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["support", "specialist"],
            MaxIterations = 5,
            HandoffInstructions = "Hand off to the specialist for technical questions.",
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = await Store.GetAsync("tenant-a", "full");

        loaded.ShouldNotBeNull();
        loaded.DisplayName.ShouldBe("Full Definition");
        loaded.Description.ShouldBe("All fields populated.");
        loaded.Kind.ShouldBe(WorkflowKind.Handoff);
        loaded.AgentNames.ShouldBe(["support", "specialist"]);
        loaded.MaxIterations.ShouldBe(5);
        loaded.HandoffInstructions.ShouldBe("Hand off to the specialist for technical questions.");

        // Added in phase 16. Because the default is false, a missing DTO
        // field would show up in this test as "returned false" -- true is
        // written deliberately.
        loaded.RequirePlanApproval.ShouldBeFalse();
    }

    [Fact]
    public async Task Node_list_survives_the_round_trip()
    {
        // Added in phase 71. The same hand-written DTO trap the remark above
        // describes: a missing field here would silently vanish on save.
        var definition = new WorkflowDefinition
        {
            Name = "mixed-chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = [],
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ],
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = await Store.GetAsync("tenant-a", "mixed-chain");

        loaded.ShouldNotBeNull();
        loaded.Nodes.Count.ShouldBe(3);
        loaded.Nodes[1].Name.ShouldBe("uppercase");
        loaded.Nodes[1].Kind.ShouldBe(WorkflowNodeKind.Function);
        loaded.AgentNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task Magentic_manager_name_is_preserved()
    {
        var definition = Definition() with
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            RequirePlanApproval = true,
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = (await Store.GetAsync("tenant-a", "magentic"))!;

        loaded.ManagerAgentName.ShouldBe("manager");

        // Plan approval is part of the jsonb payload; if it were missing from
        // the hand-written DTO, it would silently disappear.
        loaded.RequirePlanApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_save_increments_the_version()
    {
        await Store.SaveAsync("tenant-a", Definition());
        var second = await Store.SaveAsync("tenant-a", Definition() with { Description = "Updated." });

        second.Version.ShouldBe(2);

        var loaded = await Store.GetAsync("tenant-a", "review");

        loaded!.Version.ShouldBe(2);
        loaded.Description.ShouldBe("Updated.");
    }

    [Fact]
    public async Task Incoming_version_value_is_ignored()
    {
        // The store decides the version. Trusting the value sent by the
        // client would let two users write the same version number.
        var saved = await Store.SaveAsync("tenant-a", Definition() with { Version = 99 });

        saved.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_definition_is_not_visible()
    {
        await Store.SaveAsync("tenant-a", Definition());

        (await Store.GetAsync("tenant-b", "review")).ShouldBeNull();
        (await Store.ListAsync("tenant-b")).ShouldBeEmpty();
        (await Store.DeleteAsync("tenant-b", "review")).ShouldBeFalse();

        (await Store.GetAsync("tenant-a", "review")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Same_name_is_independent_across_tenants()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Tenant A." });
        await Store.SaveAsync("tenant-b", Definition() with { Description = "Tenant B." });

        (await Store.GetAsync("tenant-a", "review"))!.Description.ShouldBe("Tenant A.");
        (await Store.GetAsync("tenant-b", "review"))!.Description.ShouldBe("Tenant B.");
    }

    [Fact]
    public async Task Listing_is_sorted_by_name()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Name = "zeta" });
        await Store.SaveAsync("tenant-a", Definition() with { Name = "alpha" });
        await Store.SaveAsync("tenant-a", Definition() with { Name = "beta" });

        var names = (await Store.ListAsync("tenant-a")).Select(static definition => definition.Name).ToList();

        names.ShouldBe(["alpha", "beta", "zeta"]);
    }

    [Fact]
    public async Task Deleting_a_nonexistent_definition_returns_false()
        => (await Store.DeleteAsync("tenant-a", "missing")).ShouldBeFalse();

    [Fact]
    public async Task Deleted_definition_is_not_read_back()
    {
        await Store.SaveAsync("tenant-a", Definition());

        (await Store.DeleteAsync("tenant-a", "review")).ShouldBeTrue();
        (await Store.GetAsync("tenant-a", "review")).ShouldBeNull();
    }

    private static WorkflowDefinition Definition()
        => new()
        {
            Name = "review",
            Description = "Three-step review.",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["researcher", "writer", "editor"],
        };
}
