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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;
        await Store.SaveAsync(TestData.Definition(name));
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
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.ListAsync()).Count;
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
            CallableAgentNames = ["researcher"],
            Harness = new HarnessSettings { MaxContextWindowTokens = 4096, DisableWebSearch = true },
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
        loaded.CallableAgentNames.ShouldBe(["researcher"]);
        loaded.Harness.ShouldNotBeNull();
        loaded.Harness.MaxContextWindowTokens.ShouldBe(4096);
        loaded.Harness.DisableWebSearch.ShouldBeTrue();
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
