using System.Text.Json;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IAgentDefinitionStore"/> contract.
/// </summary>
/// <remarks>
/// These tests run against <strong>every implementation</strong>. A behavior
/// difference between the in-memory store and the PostgreSQL store is a bug;
/// this class catches that difference.
/// </remarks>
public abstract class AgentDefinitionStoreContract : TenantIsolationContract<IAgentDefinitionStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// The tenant is not a parameter on the interface; it is read from
    /// <see cref="ITenantContext"/>. Each hook therefore sets the active
    /// tenant first.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;
        await Store.SaveAsync(TestData.Definition(name), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        var name = (string)key;

        // All three read paths must carry the same isolation: the single
        // read, the version read, and the version history.
        if (await Store.GetAsync(name) is null)
        {
            (await Store.ListVersionsAsync(name)).ShouldBeEmpty();
            (await Store.GetVersionAsync(name, 1)).ShouldBeNull();
            return false;
        }

        (await Store.ListVersionsAsync(name)).ShouldNotBeEmpty();
        return true;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.ListAsync(cancellationToken)).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.DeleteAsync((string)key);
    }

    [Fact]
    public async Task SaveAsync_increments_version_and_keeps_history()
    {
        var first = await Store.SaveAsync(TestData.Definition("a") with { Instructions = "first" });
        var second = await Store.SaveAsync(TestData.Definition("a") with { Instructions = "second" });

        first.Version.ShouldBe(1);
        second.Version.ShouldBe(2);

        (await Store.GetAsync("a"))!.Instructions.ShouldBe("second");
        (await Store.ListVersionsAsync("a")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveAsync_marks_origin_as_database()
    {
        var saved = await Store.SaveAsync(TestData.Definition("a") with { Origin = AgentDefinitionOrigin.Code });

        saved.Origin.ShouldBe(AgentDefinitionOrigin.Database);
    }

    [Fact]
    public async Task ListVersionsAsync_orders_newest_to_oldest()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));

        var versions = await Store.ListVersionsAsync("a");

        versions.Select(static v => v.Version).ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task RollbackAsync_saves_the_old_version_as_a_new_version()
    {
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "first" });
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "second" });

        var restored = await Store.RollbackAsync("a", version: 1);

        restored.Version.ShouldBe(3);
        restored.Instructions.ShouldBe("first");

        // Rollback does not delete history.
        (await Store.ListVersionsAsync("a")).Count.ShouldBe(3);
        (await Store.GetAsync("a"))!.Instructions.ShouldBe("first");
    }

    [Fact]
    public async Task RollbackAsync_to_a_missing_version_throws()
    {
        await Store.SaveAsync(TestData.Definition("a"));

        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("a", version: 99));
    }

    [Fact]
    public async Task RollbackAsync_for_a_missing_agent_throws()
        => await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("missing", version: 1));

    [Fact]
    public async Task DeleteAsync_removes_all_versions()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));

        (await Store.DeleteAsync("a")).ShouldBeTrue();
        (await Store.GetAsync("a")).ShouldBeNull();
        (await Store.ListVersionsAsync("a")).ShouldBeEmpty();
        (await Store.DeleteAsync("a")).ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_after_DeleteAsync_continues_the_numbering()
    {
        // A version number is an identity: a run, a workflow node, an approval or an experiment arm
        // that recorded version 2 must never be handed another content under that number.
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "first era 1" });
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "first era 2" });
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "first era 3" });

        (await Store.DeleteAsync("a")).ShouldBeTrue();

        var recreated = await Store.SaveAsync(TestData.Definition("a") with { Instructions = "second era" });

        recreated.Version.ShouldBe(4);

        // The old numbers name nothing: the history went with the delete, the numbers did not come back.
        (await Store.GetVersionAsync("a", 1)).ShouldBeNull();
        (await Store.GetVersionAsync("a", 2)).ShouldBeNull();
        (await Store.GetVersionAsync("a", 3)).ShouldBeNull();
        (await Store.GetVersionAsync("a", 4))!.Instructions.ShouldBe("second era");
        (await Store.ListVersionsAsync("a")).Select(static v => v.Version).ShouldBe([4]);
        (await Store.GetAsync("a"))!.Version.ShouldBe(4);
    }

    [Fact]
    public async Task The_numbering_survives_every_delete_not_only_the_first()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.DeleteAsync("a");
        (await Store.SaveAsync(TestData.Definition("a"))).Version.ShouldBe(3);
        await Store.DeleteAsync("a");

        // A second delete of a name that is already gone changes nothing.
        (await Store.DeleteAsync("a")).ShouldBeFalse();

        (await Store.SaveAsync(TestData.Definition("a"))).Version.ShouldBe(4);
    }

    [Fact]
    public async Task DeleteAsync_of_a_name_that_never_existed_does_not_start_a_numbering()
    {
        (await Store.DeleteAsync("never")).ShouldBeFalse();

        (await Store.SaveAsync(TestData.Definition("never"))).Version.ShouldBe(1);
    }

    [Fact]
    public async Task RollbackAsync_after_a_recreate_continues_the_numbering()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.DeleteAsync("a");
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "recreated" });

        // Version 3 is the recreated one; the rollback is a new version, version 4.
        var restored = await Store.RollbackAsync("a", version: 3);

        restored.Version.ShouldBe(4);
        restored.Instructions.ShouldBe("recreated");

        // A version of the deleted era cannot be rolled back to.
        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("a", version: 1));
    }

    [Fact]
    public async Task RollbackAsync_for_a_deleted_agent_throws_and_keeps_the_numbering()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.DeleteAsync("a");

        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("a", version: 2));

        (await Store.GetAsync("a")).ShouldBeNull();
        (await Store.SaveAsync(TestData.Definition("a"))).Version.ShouldBe(3);
    }

    [Fact]
    public async Task One_tenants_delete_does_not_move_another_tenants_numbering()
    {
        AmbientTenant.TenantId = TenantA;
        await Store.SaveAsync(TestData.Definition("support"));
        await Store.SaveAsync(TestData.Definition("support"));

        AmbientTenant.TenantId = TenantB;
        await Store.SaveAsync(TestData.Definition("support"));
        await Store.DeleteAsync("support");

        // Tenant A's name keeps its own counter and tenant B's counter is its own.
        AmbientTenant.TenantId = TenantA;
        (await Store.SaveAsync(TestData.Definition("support"))).Version.ShouldBe(3);

        AmbientTenant.TenantId = TenantB;
        (await Store.SaveAsync(TestData.Definition("support"))).Version.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_saves_of_one_name_get_distinct_versions_across_a_delete()
    {
        // The first save of a name goes through the INSERT branch of the upsert, every other through the
        // UPDATE branch: both are in the run, before and after the delete.
        const int Writers = 8;

        await Store.SaveAsync(TestData.Definition("a"));
        await Store.DeleteAsync("a");

        var saved = await Task.WhenAll(Enumerable.Range(1, Writers).Select(
            index => Task.Run(async () => await Store.SaveAsync(TestData.Definition("a") with { Instructions = $"Writer {index}." }))));

        saved.Select(static definition => definition.Version).Order().ShouldBe(Enumerable.Range(2, Writers));

        // A number names one content: what each caller was told it wrote is what that version holds.
        foreach (var definition in saved)
        {
            (await Store.GetVersionAsync("a", definition.Version))!.Instructions.ShouldBe(definition.Instructions);
        }

        (await Store.GetAsync("a"))!.Version.ShouldBe(Writers + 1);
    }

    [Fact]
    public async Task Concurrent_first_saves_of_a_new_name_get_distinct_versions()
    {
        // No row of this name exists, neither the definition nor its counter: every writer races on the
        // INSERT branch of both upserts, and only one of them may create the rows.
        const int Writers = 8;

        var saved = await Task.WhenAll(Enumerable.Range(1, Writers).Select(
            index => Task.Run(async () => await Store.SaveAsync(TestData.Definition("fresh") with { Instructions = $"Writer {index}." }))));

        saved.Select(static definition => definition.Version).Order().ShouldBe(Enumerable.Range(1, Writers));

        foreach (var definition in saved)
        {
            (await Store.GetVersionAsync("fresh", definition.Version))!.Instructions.ShouldBe(definition.Instructions);
        }

        (await Store.ListVersionsAsync("fresh")).Count.ShouldBe(Writers);
        (await Store.GetAsync("fresh"))!.Version.ShouldBe(Writers);
    }

    [Fact]
    public async Task GetAsync_for_a_missing_definition_returns_null()
        => (await Store.GetAsync("missing")).ShouldBeNull();

    [Fact]
    public async Task GetVersionAsync_returns_the_requested_version()
    {
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "first" });
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "second" });

        var first = await Store.GetVersionAsync("a", 1);
        var second = await Store.GetVersionAsync("a", 2);

        first!.Instructions.ShouldBe("first");
        second!.Instructions.ShouldBe("second");
    }

    [Fact]
    public async Task GetVersionAsync_for_a_missing_version_returns_null()
    {
        await Store.SaveAsync(TestData.Definition("a"));

        (await Store.GetVersionAsync("a", 99)).ShouldBeNull();
    }

    [Fact]
    public async Task GetVersionAsync_for_a_missing_agent_returns_null()
        => (await Store.GetVersionAsync("missing", 1)).ShouldBeNull();

    [Fact]
    public async Task ListAsync_orders_by_name()
    {
        await Store.SaveAsync(TestData.Definition("gamma"));
        await Store.SaveAsync(TestData.Definition("alpha"));
        await Store.SaveAsync(TestData.Definition("beta"));

        var all = await Store.ListAsync();

        all.Select(static definition => definition.Name).ShouldBe(["alpha", "beta", "gamma"]);
    }

    [Fact]
    public async Task SaveAsync_round_trips_all_definition_fields()
    {
        var original = TestData.Definition("full") with
        {
            InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["en"] = "Reply briefly.",
                ["fr"] = "Answer using the fr culture text.",
            },
            Model = new ModelBinding
            {
                Provider = "echo",
                Model = "echo-1",
                Temperature = 0.5f,

                // Provider-specific settings also travel inside jsonb (phase 26).
                ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anthropic.promptCaching"] = TestData.State("true"),
                    ["anthropic.thinking.budgetTokens"] = TestData.State("2048"),
                },

                // The structured output schema also travels inside jsonb (phase 38).
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = TestData.State("""{"type":"object","properties":{"total":{"type":"number"}}}"""),
                    SchemaName = "invoice",
                    SchemaDescription = "Schema of an invoice summary.",
                },
            },
            SkillNames = ["house-skill"],
            Skills = new SkillSettings { DisableReadApproval = true },
            Workspace = new WorkspaceSettings { Enabled = true, ReadOnly = true, DisableReadApproval = true },

            // One callee as it is now and one pinned: a store that dropped the pin would run the pinned
            // callee's current version, with nothing to say so.
            CallableAgents = ["researcher", new CallableAgentReference { Name = "writer", Version = 2 }],
            Harness = new HarnessSettings
            {
                MaxContextWindowTokens = 4096,
                DisableWebSearch = true,

                // Both outcomes away from their defaults: a store that dropped them
                // would load a loop that returns an unsatisfied response as success.
                Loop = new LoopSettings
                {
                    Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "DONE" }],
                    MaxIterations = 4,
                    OnExhausted = LoopOutcome.FailRun,
                    OnCriterionError = LoopOutcome.FailRun,
                },
            },
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.Summarization,
                TriggerTokens = 8_000,
                MinimumPreservedGroups = 4,
                SummarizationPrompt = "summarize briefly and concisely",
                SummarizationModel = new ModelBinding { Provider = "echo", Model = "echo-summarizer" },
            },
            Memory = new MemorySettings { EnableFileMemory = true, EnableTodo = true, EnableTextSearch = true },
            Metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["owner"] = TestData.State("\"platform-team\""),
                ["priority"] = TestData.State("3"),
            },
            Parameters =
            [
                new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "formal" },
            ],
            SharedInstructionsName = "house-rules",
        };

        await Store.SaveAsync(original);
        var loaded = await Store.GetAsync("full");

        loaded.ShouldNotBeNull();
        loaded.DisplayName.ShouldBe(original.DisplayName);
        loaded.Description.ShouldBe(original.Description);
        loaded.Instructions.ShouldBe(original.Instructions);
        loaded.InstructionsByCulture.ShouldNotBeNull();
        loaded.InstructionsByCulture!.ShouldBe(original.InstructionsByCulture);
        loaded.Model.Provider.ShouldBe("echo");
        loaded.Model.Model.ShouldBe("echo-1");
        loaded.Model.Temperature.ShouldBe(0.5f);
        loaded.Model.ProviderSettings.Count.ShouldBe(2);
        loaded.Model.ProviderSettings["anthropic.promptCaching"].GetBoolean().ShouldBeTrue();
        loaded.Model.ProviderSettings["anthropic.thinking.budgetTokens"].GetInt32().ShouldBe(2048);
        loaded.Model.ResponseFormat.ShouldNotBeNull();
        loaded.Model.ResponseFormat!.Kind.ShouldBe(AgentResponseFormatKind.JsonSchema);
        loaded.Model.ResponseFormat.Schema.ShouldNotBeNull();
        loaded.Model.ResponseFormat.Schema!.Value.GetProperty("type").GetString().ShouldBe("object");
        loaded.Model.ResponseFormat.SchemaName.ShouldBe("invoice");
        loaded.Model.ResponseFormat.SchemaDescription.ShouldBe("Schema of an invoice summary.");
        loaded.ToolNames.ShouldBe(["alpha", "beta"]);
        loaded.SkillNames.ShouldBe(["house-skill"]);
        loaded.Skills.ShouldNotBeNull().DisableReadApproval.ShouldBeTrue();
        loaded.Workspace.ShouldBe(new WorkspaceSettings { Enabled = true, ReadOnly = true, DisableReadApproval = true });
        loaded.CallableAgents.ShouldBe([new CallableAgentReference { Name = "researcher" }, new CallableAgentReference { Name = "writer", Version = 2 }]);
        loaded.Harness.ShouldNotBeNull();
        loaded.Harness.MaxContextWindowTokens.ShouldBe(4096);
        loaded.Harness.DisableWebSearch.ShouldBeTrue();
        loaded.Harness.Loop.ShouldNotBeNull();
        loaded.Harness.Loop.Criteria.ShouldHaveSingleItem().Marker.ShouldBe("DONE");
        loaded.Harness.Loop.MaxIterations.ShouldBe(4);
        loaded.Harness.Loop.OnExhausted.ShouldBe(LoopOutcome.FailRun);
        loaded.Harness.Loop.OnCriterionError.ShouldBe(LoopOutcome.FailRun);
        loaded.Compaction.ShouldNotBeNull();
        loaded.Compaction.Strategy.ShouldBe(CompactionStrategyKind.Summarization);
        loaded.Compaction.TriggerTokens.ShouldBe(8_000);
        loaded.Compaction.MinimumPreservedGroups.ShouldBe(4);
        loaded.Compaction.SummarizationPrompt.ShouldBe("summarize briefly and concisely");
        loaded.Compaction.SummarizationModel.ShouldNotBeNull();
        loaded.Compaction.SummarizationModel!.Provider.ShouldBe("echo");
        loaded.Compaction.SummarizationModel.Model.ShouldBe("echo-summarizer");
        loaded.Memory.ShouldNotBeNull();
        loaded.Memory.EnableFileMemory.ShouldBeTrue();
        loaded.Memory.EnableTodo.ShouldBeTrue();
        loaded.Memory.EnableTextSearch.ShouldBeTrue();
        loaded.Metadata["owner"].GetString().ShouldBe("platform-team");
        loaded.Metadata["priority"].GetInt32().ShouldBe(3);
        loaded.Parameters.Count.ShouldBe(2);
        loaded.Parameters[0].Name.ShouldBe("customer");
        loaded.Parameters[0].Kind.ShouldBe(AgentParameterKind.Text);
        loaded.Parameters[0].Required.ShouldBeTrue();
        loaded.Parameters[1].Name.ShouldBe("tone");
        loaded.Parameters[1].DefaultValue.ShouldBe("formal");
        loaded.SharedInstructionsName.ShouldBe("house-rules");
    }

    [Fact]
    public async Task Tenant_cannot_roll_back_another_tenants_definition()
    {
        AmbientTenant.TenantId = TenantA;
        await Store.SaveAsync(TestData.Definition("secret"));
        await Store.SaveAsync(TestData.Definition("secret") with { Instructions = "second" });

        AmbientTenant.TenantId = TenantB;
        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("secret", 1));

        AmbientTenant.TenantId = TenantA;
        (await Store.RollbackAsync("secret", 1)).Version.ShouldBe(3);
    }

    [Fact]
    public async Task Each_tenants_version_counter_is_its_own()
    {
        // Moved from IsolationTests.cs (Phase 41).
        AmbientTenant.TenantId = TenantA;
        await Store.SaveAsync(TestData.Definition("support") with { Instructions = "tenant a instructions" });

        AmbientTenant.TenantId = TenantB;
        await Store.SaveAsync(TestData.Definition("support") with { Instructions = "tenant b instructions" });

        AmbientTenant.TenantId = TenantA;
        var first = (await Store.GetAsync("support")).ShouldNotBeNull();
        first.Instructions.ShouldBe("tenant a instructions");
        first.Version.ShouldBe(1);

        AmbientTenant.TenantId = TenantB;
        var second = (await Store.GetAsync("support")).ShouldNotBeNull();
        second.Instructions.ShouldBe("tenant b instructions");
        second.Version.ShouldBe(1);
    }
}
