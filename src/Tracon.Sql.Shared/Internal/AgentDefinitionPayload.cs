using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon;

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

    /// <summary>Gets the culture-keyed instructions.</summary>
    public Dictionary<string, string>? InstructionsByCulture { get; init; }

    /// <summary>Gets the provider and model binding.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>Gets the names of the tools that can be used.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Gets the names of the skills that can be loaded at run time.</summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>Gets the skill settings, or <see langword="null"/> in a row that predates the member.</summary>
    public SkillSettings? Skills { get; init; }

    /// <summary>Gets the session workspace settings, or <see langword="null"/> in a row that predates the member.</summary>
    public WorkspaceSettings? Workspace { get; init; }

    /// <summary>Gets the names of the agent extensions the agent is compiled with, or <see langword="null"/> when there are none.</summary>
    /// <remarks>
    /// Nullable on purpose. A row written before this member existed has no such key, and the
    /// source-generated reader leaves an absent list <see langword="null"/> instead of running the
    /// initializer (measured: the type has a required member). A non-null property read back as
    /// <see langword="null"/> and broke the catalog listing of every agent. An empty list is not
    /// written, so a definition that uses no extension keeps the bytes it always had.
    /// </remarks>
    public IReadOnlyList<string>? ExtensionNames { get; init; }

    /// <summary>Gets the names of the other agents this agent can call.</summary>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>Gets how long this agent waits for the agents it calls.</summary>
    public SubAgentSettings? SubAgents { get; init; }

    /// <summary>Gets the MCP resources added to the run context, or <see langword="null"/> in a row that predates the member.</summary>
    public IReadOnlyList<string>? McpResourceUris { get; init; }

    /// <summary>Gets the harness settings.</summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>Gets the context compaction settings.</summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>Gets the memory provider settings.</summary>
    public MemorySettings? Memory { get; init; }

    /// <summary>Gets the free-form application-specific metadata.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>Gets the parameter schema, or <see langword="null"/> in a row that predates the member.</summary>
    public IReadOnlyList<AgentParameter>? Parameters { get; init; }

    /// <summary>Gets the referenced shared instructions block.</summary>
    public string? SharedInstructionsName { get; init; }

    /// <summary>Reads a stored payload, upgrading a row written before the tool loop settings moved.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The payload, or <see langword="null"/> when the JSON is the literal <c>null</c>.</returns>
    public static AgentDefinitionPayload? Read(string json)
        => JsonSerializer.Deserialize(UpgradeLegacyKeys(json), TraconJsonContext.Default.AgentDefinitionPayload);

    /// <summary>Moves a key a row still carries from before it moved, and returns the JSON unchanged when there is none.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The JSON with every legacy key in its current place.</returns>
    /// <remarks>
    /// <para>
    /// <c>model.allowConcurrentToolCalls</c> became <c>model.toolLoop.allowConcurrentToolCalls</c>. A row
    /// from before the move still carries the old key; the reader would drop it silently and an agent
    /// that ran its tool calls at the same time would run them one after another. The key is moved
    /// while the row is read; the next save writes the new place only. A rollback writes an old version
    /// as a new one, so it moves the key too, and a row written by the rollback never holds the old key.
    /// </para>
    /// <para>
    /// A row written since the move always names <c>toolLoop</c> (the loop settings are written even when they
    /// are all default), and it is then trusted as current. Only a row that has the old key and no
    /// <c>toolLoop</c> is parsed, so the common read costs two text searches and no parse.
    /// </para>
    /// </remarks>
    public static string UpgradeLegacyKeys(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return json.Contains(LegacyConcurrentKey, StringComparison.OrdinalIgnoreCase)
            && !json.Contains(CurrentLoopKey, StringComparison.OrdinalIgnoreCase)
            && MoveLegacyConcurrentKey(json) is { } upgraded
                ? upgraded
                : json;
    }

    private const string LegacyConcurrentKey = "allowConcurrentToolCalls";

    private const string CurrentLoopKey = "\"toolLoop\"";

    private static string? MoveLegacyConcurrentKey(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root
            || root["model"] is not JsonObject model
            || model[LegacyConcurrentKey] is not { } legacy)
        {
            return null;
        }

        model.Remove(LegacyConcurrentKey);
        model["toolLoop"] = new JsonObject { [LegacyConcurrentKey] = legacy.DeepClone() };

        return root.ToJsonString();
    }

    /// <summary>Converts the content of a definition into a payload.</summary>
    /// <param name="definition">The source definition.</param>
    /// <returns>The payload to serialize.</returns>
    public static AgentDefinitionPayload FromDefinition(AgentDefinition definition)
        => new()
        {
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Instructions = definition.Instructions,
            InstructionsByCulture = definition.InstructionsByCulture is { Count: > 0 } byCulture
                ? new Dictionary<string, string>(byCulture, StringComparer.Ordinal)
                : null,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            Skills = definition.Skills,
            Workspace = definition.Workspace,
            ExtensionNames = definition.ExtensionNames is { Count: > 0 } extensionNames ? extensionNames : null,
            CallableAgentNames = definition.CallableAgentNames,
            SubAgents = definition.SubAgents,
            McpResourceUris = definition.McpResourceUris,
            Harness = definition.Harness,
            Compaction = definition.Compaction,
            Memory = definition.Memory,
            Metadata = definition.Metadata.Count == 0
                ? null
                : new Dictionary<string, JsonElement>(definition.Metadata, StringComparer.Ordinal),
            Parameters = definition.Parameters,
            SharedInstructionsName = definition.SharedInstructionsName,
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
            InstructionsByCulture = InstructionsByCulture,
            Model = Model,
            ToolNames = ToolNames,
            SkillNames = SkillNames,
            Skills = Skills,
            Workspace = Workspace,
            ExtensionNames = ExtensionNames ?? [],
            CallableAgentNames = CallableAgentNames,
            SubAgents = SubAgents,
            McpResourceUris = McpResourceUris ?? [],
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
            Parameters = Parameters ?? [],
            SharedInstructionsName = SharedInstructionsName,
        };
}
