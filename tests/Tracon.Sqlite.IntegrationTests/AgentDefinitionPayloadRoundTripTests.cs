using System.Reflection;
using System.Text.Json;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Guards the <c>jsonb</c> payload against silent field loss (B01).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentDefinitionPayload"/> is the second place a definition field
/// can vanish, independently of the HTTP DTO: <c>SubAgents</c> and
/// <c>McpResourceUris</c> were dropped here, so a definition created through
/// the .NET API with those fields set lost them the moment it was written to
/// SQL — before any editor was involved. The in-memory store takes a
/// <c>definition with</c> copy and kept them, so the same record survived in
/// one backend and not the other.
/// </para>
/// <para>
/// SQLite stands in for all three providers: the payload type and both mapper
/// bodies live in the shared layer and are dialect-independent.
/// </para>
/// </remarks>
public sealed class AgentDefinitionPayloadRoundTripTests(SqliteFixture fixture)
{
    /// <summary>
    /// Fields that live in <em>columns</em> rather than the payload, so that a
    /// version bump does not have to rewrite the JSON. Absent from the payload
    /// by design — see <see cref="AgentDefinitionPayload"/>.
    /// </summary>
    private static readonly HashSet<string> StoredInColumns = new(StringComparer.Ordinal)
    {
        nameof(AgentDefinition.Name),
        nameof(AgentDefinition.Origin),
        nameof(AgentDefinition.Version),
        nameof(AgentDefinition.TenantId),
        nameof(AgentDefinition.UpdatedAt),
    };

    private static IEnumerable<PropertyInfo> PayloadCarriedProperties()
        => typeof(AgentDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => !StoredInColumns.Contains(property.Name));

    [Fact]
    public void Every_field_not_stored_in_a_column_is_expressible_on_the_payload()
    {
        var missing = PayloadCarriedProperties()
            .Where(static property => typeof(AgentDefinitionPayload).GetProperty(property.Name) is null)
            .Select(static property => property.Name)
            .ToList();

        missing.ShouldBeEmpty(
            $"AgentDefinitionPayload cannot express: {string.Join(", ", missing)}. " +
            "The payload IS the stored definition apart from the columns, so a field missing here is " +
            "dropped on save. Add the property and map it in BOTH FromDefinition and ToDefinition, " +
            "or add it to StoredInColumns here if it really did become a column.");
    }

    [Fact]
    public async Task A_definition_saved_with_every_field_set_reads_back_unchanged()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        await context.AgentDefinitions.SaveAsync(FullyPopulatedDefinition());

        var read = await context.AgentDefinitions.GetAsync("payload-round-trip");

        read.ShouldNotBeNull();

        var bare = new AgentDefinition { Name = "payload-round-trip", Model = Model() };
        var dropped = new List<string>();

        foreach (var property in PayloadCarriedProperties())
        {
            if (IsIndistinguishableFromUnset(property.GetValue(read), property.GetValue(bare)))
            {
                dropped.Add(property.Name);
            }
        }

        dropped.ShouldBeEmpty(
            $"These came back at their unset value after a SQL round trip: {string.Join(", ", dropped)}. " +
            "Either FullyPopulatedDefinition() below does not set the field, or one of the two payload " +
            "mapper bodies declares it but never assigns it.");

        // Spot-check the two fields the payload actually lost, by value rather
        // than by "is it non-default" — a mapper that assigned the wrong source
        // property would still pass the loop above.
        read.SubAgents!.WaitTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        read.McpResourceUris.ShouldBe(["docs:file:///handbook.md"]);

        // The list is ordered data: the order the extensions run is the order stored.
        read.ExtensionNames.ShouldBe(["echo-extension", "second-extension"]);
    }

    /// <summary>
    /// A row written before <c>ExtensionNames</c> existed has no such key. It must read back as an
    /// empty list, not as <see langword="null"/>: a null list threw in the catalog and took the
    /// listing of every agent with it. <c>McpResourceUris</c> and <c>Parameters</c> were added later
    /// than the first rows and had the same defect.
    /// </summary>
    [Fact]
    public void A_payload_written_before_a_list_member_existed_reads_back_with_an_empty_list()
    {
        const string legacy = """{"model":{"provider":"echo","model":"echo-1"},"toolNames":["echo-tool"]}""";

        var payload = JsonSerializer.Deserialize(legacy, TraconJsonContext.Default.AgentDefinitionPayload);

        payload.ShouldNotBeNull();

        // The reader leaves every absent list null. The definition built from it never does:
        // every consumer reads these lists without a null check.
        var definition = payload.ToDefinition("legacy", 1, "default", DateTimeOffset.UtcNow);

        definition.ExtensionNames.ShouldBeEmpty();
        definition.McpResourceUris.ShouldBeEmpty();
        definition.Parameters.ShouldBeEmpty();
        definition.ToolNames.ShouldBe(["echo-tool"]);
    }

    /// <summary>
    /// A row written before <c>ToolLoop</c> existed has no such key. The source-generated reader does
    /// not run the initializer of a type with <c>required</c> members for an absent key (measured on
    /// the lists in K-1031), so the setting must still come back as an object.
    /// </summary>
    [Fact]
    public void A_payload_written_before_the_tool_loop_existed_reads_back_with_default_loop_settings()
    {
        const string legacy = """{"model":{"provider":"echo","model":"echo-1"}}""";

        var payload = JsonSerializer.Deserialize(legacy, TraconJsonContext.Default.AgentDefinitionPayload);

        payload.ShouldNotBeNull("the payload itself must read");
        (payload.Model.ToolLoop is null).ShouldBeFalse("the reader left the absent loop settings null");
        payload.Model.ToolLoop!.AllowConcurrentToolCalls.ShouldBeFalse();
    }

    private const string LegacyModel = """{"provider":"echo","model":"echo-1","fallbacks":[],"providerSettings":{},"allowConcurrentToolCalls":true}""";

    /// <summary>
    /// <c>model.allowConcurrentToolCalls</c> moved to <c>model.toolLoop.allowConcurrentToolCalls</c>. A row written
    /// before the move still carries the old key, and the reader drops an unknown key without a word: the agent
    /// would have run its tool calls one after another from the day it was upgraded.
    /// </summary>
    [Fact]
    public void A_payload_written_before_the_concurrency_setting_moved_keeps_its_value()
    {
        var payload = AgentDefinitionPayload.Read($$"""{"model":{{LegacyModel}},"toolNames":["echo-tool"]}""");

        payload.ShouldNotBeNull();
        payload.Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeTrue();
        payload.ToolNames.ShouldBe(["echo-tool"]);
    }

    /// <summary>A row that names <c>toolLoop</c> is current: the old key beside it is ignored, as any unknown key is.</summary>
    [Fact]
    public void A_row_that_holds_both_keys_keeps_the_new_place()
    {
        var payload = AgentDefinitionPayload.Read(
            """{"model":{"provider":"echo","model":"echo-1","allowConcurrentToolCalls":true,"toolLoop":{"allowConcurrentToolCalls":false}}}""");

        payload.ShouldNotBeNull();
        payload.Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeFalse();
    }

    [Fact]
    public void A_current_row_is_not_touched()
    {
        const string current = """{"model":{"provider":"echo","model":"echo-1","toolLoop":{"allowConcurrentToolCalls":true}}}""";

        AgentDefinitionPayload.UpgradeLegacyKeys(current).ShouldBeSameAs(current);
        AgentDefinitionPayload.Read(current)!.Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeTrue();
    }

    /// <summary>
    /// The row goes through the store, not only through the payload type: a reader that forgets to call the
    /// upgrade passes every test of the type and loses the setting in production.
    /// </summary>
    [Fact]
    public async Task A_stored_row_with_the_old_key_reads_back_through_every_store_read_with_its_value()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        await context.AgentDefinitions.SaveAsync(new AgentDefinition { Name = "legacy-row", Model = Model() });
        await context.AgentDefinitions.SaveAsync(new AgentDefinition { Name = "legacy-row", Model = Model(), Description = "second" });

        // Rewrite what the first version and the current row hold to the shape that was stored before the move.
        var prefix = context.Options.TablePrefix;
        var legacy = $$"""{"model":{{LegacyModel}},"description":"second"}""";

        (await context.ExecuteAsync($"UPDATE {prefix}agent_definitions SET definition = '{legacy}' WHERE name = 'legacy-row';")).ShouldBe(1);
        (await context.ExecuteAsync($"UPDATE {prefix}agent_definition_versions SET definition = '{legacy}';")).ShouldBe(2);

        (await context.AgentDefinitions.GetAsync("legacy-row"))!.Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeTrue();
        (await context.AgentDefinitions.GetVersionAsync("legacy-row", 1))!.Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeTrue();
        (await context.AgentDefinitions.ListAsync()).ShouldHaveSingleItem().Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeTrue();
        (await context.AgentDefinitions.ListVersionsAsync("legacy-row")).ShouldAllBe(static version => version.Model.ToolLoop.AllowConcurrentToolCalls);

        // A rollback writes the old version as a new one. The new row holds the new place only.
        var rolledBack = await context.AgentDefinitions.RollbackAsync("legacy-row", 1);

        rolledBack.Model.ToolLoop.AllowConcurrentToolCalls.ShouldBeTrue();
        var stored = await context.ScalarAsync<string>($"SELECT definition FROM {prefix}agent_definitions WHERE name = 'legacy-row';");

        using var document = JsonDocument.Parse(stored.ShouldNotBeNull());
        var model = document.RootElement.GetProperty("model");

        model.TryGetProperty("allowConcurrentToolCalls", out _).ShouldBeFalse("the rolled-back row must not carry the old key");
        model.GetProperty("toolLoop").GetProperty("allowConcurrentToolCalls").GetBoolean().ShouldBeTrue();
    }

    private static ModelBinding Model() => new() { Provider = "echo", Model = "echo-1" };

    /// <summary>
    /// A definition with every payload-carried field set to something
    /// distinguishable from its unset value. A new definition field must be
    /// added here too — that is what keeps the round trip a ratchet.
    /// </summary>
    private static AgentDefinition FullyPopulatedDefinition()
        => new()
        {
            Name = "payload-round-trip",
            DisplayName = "Payload Round Trip",
            Description = "Saved with everything set.",
            Instructions = "Reply briefly.",
            InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal) { ["fr"] = "Reply briefly, in French." },
            Model = Model() with
            {
                ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anthropic.promptCaching"] = JsonSerializer.SerializeToElement(true),
                },
                ToolLoop = new ToolLoopSettings { AllowConcurrentToolCalls = true },
            },
            ToolNames = ["echo-tool"],
            SkillNames = ["echo-skill"],
            ExtensionNames = ["echo-extension", "second-extension"],
            CallableAgentNames = ["child-agent"],
            SubAgents = new SubAgentSettings { WaitTimeout = TimeSpan.FromSeconds(30) },
            McpResourceUris = ["docs:file:///handbook.md"],
            Harness = new HarnessSettings(),
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Truncation },
            Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "internal-documents" },
            Metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["owner"] = JsonSerializer.SerializeToElement("platform-team"),
            },
            Parameters = [new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text }],
            SharedInstructionsName = "shared-block",
        };

    private static bool IsIndistinguishableFromUnset(object? read, object? bare)
    {
        if (read is null)
        {
            return true;
        }

        if (read is System.Collections.ICollection collection)
        {
            return collection.Count == 0;
        }

        return Equals(read, bare);
    }
}
