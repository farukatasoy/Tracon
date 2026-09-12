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
                AllowConcurrentToolCalls = true,
            },
            ToolNames = ["echo-tool"],
            SkillNames = ["echo-skill"],
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
