using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// The part of an agent definition that is written to the <c>jsonb</c> column.
/// </summary>
/// <remarks>
/// <para>
/// The name, version, tenant, origin and update time live in <em>columns</em> and
/// that is the single source of truth. Repeating those fields inside the
/// <c>jsonb</c> as well would create two records of the same fact; the jsonb would
/// have to be rewritten after every version increment and inconsistency would
/// become possible.
/// </para>
/// <para>
/// Only the <em>content</em> of the definition is therefore serialized; it is
/// merged with the columns when it is read back.
/// </para>
/// </remarks>
internal sealed record AgentDefinitionPayload
{
    /// <summary>Gets the name shown in the user interface.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the agent description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the system instructions.</summary>
    public string? Instructions { get; init; }

    /// <summary>Gets the provider and model binding.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>Gets the names of the tools that can be used.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Gets the names of the skills that can be loaded at run time.</summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>Gets the names of the other agents this agent can call.</summary>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>Gets the harness settings.</summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>Gets the context compaction settings.</summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>Gets the memory provider settings.</summary>
    public MemorySettings? Memory { get; init; }

    /// <summary>Gets the free-form application-specific metadata.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>Converts the content of a definition into a payload.</summary>
    /// <param name="definition">The source definition.</param>
    /// <returns>The payload to serialize.</returns>
    public static AgentDefinitionPayload FromDefinition(AgentDefinition definition)
        => new()
        {
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Instructions = definition.Instructions,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            CallableAgentNames = definition.CallableAgentNames,
            Harness = definition.Harness,
            Compaction = definition.Compaction,
            Memory = definition.Memory,
            Metadata = definition.Metadata.Count == 0
                ? null
                : new Dictionary<string, JsonElement>(definition.Metadata, StringComparer.Ordinal),
        };

    /// <summary>Builds the full definition by merging the payload with the column values.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="version">The version number.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="updatedAt">The last update time.</param>
    /// <returns>The full definition.</returns>
    public AgentDefinition ToDefinition(string name, int version, string tenantId, DateTimeOffset updatedAt)
        => new()
        {
            Name = name,
            DisplayName = DisplayName,
            Description = Description,
            Instructions = Instructions,
            Model = Model,
            ToolNames = ToolNames,
            SkillNames = SkillNames,
            CallableAgentNames = CallableAgentNames,
            Harness = Harness,
            Compaction = Compaction,
            Memory = Memory,
            Origin = AgentDefinitionOrigin.Database,
            Version = version,
            TenantId = tenantId,
            UpdatedAt = updatedAt,
            Metadata = Metadata is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(Metadata, StringComparer.Ordinal),
        };
}
