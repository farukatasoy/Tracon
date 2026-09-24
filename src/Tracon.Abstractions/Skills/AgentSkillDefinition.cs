using System.Text.Json;

namespace Tracon;

/// <summary>
/// A markdown-based skill definition that can be loaded into an agent at run time.
/// </summary>
/// <remarks>
/// A skill carries instructions, resources, and <see cref="Scripts"/>. Script
/// execution is a separate security boundary: unless
/// <c>TraconSkillScriptOptions.AllowStoredScripts</c> is enabled, stored
/// scripts are only kept in storage and never executed.
/// </remarks>
public sealed record AgentSkillDefinition
{
    /// <summary>The skill's unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The tenant the skill belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The skill name. Agent definitions bind to the skill with this name.</summary>
    public required string Name { get; init; }

    /// <summary>The skill's short description.</summary>
    public required string Description { get; init; }

    /// <summary>The markdown instructions given to the model.</summary>
    public required string Instructions { get; init; }

    /// <summary>The skill's compatibility statement.</summary>
    public string? Compatibility { get; init; }

    /// <summary>The skill license.</summary>
    public string? License { get; init; }

    /// <summary>The allowed-tools declaration in the MAF frontmatter.</summary>
    public string? AllowedTools { get; init; }

    /// <summary>Application-specific free-form metadata.</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>Whether the skill is included in compilation.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The skill record's version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The skill's resources.</summary>
    public IReadOnlyList<AgentSkillResourceDefinition> Resources { get; init; } = [];

    /// <summary>
    /// The skill's scripts, stored in the database or registered in code.
    /// </summary>
    /// <remarks>
    /// These scripts run on the server. They are visible to the model only
    /// when <c>TraconSkillScriptOptions.AllowStoredScripts</c> is on;
    /// while off, the record is stored but cannot be executed. Each one runs only
    /// under a grant pinned to its content.
    /// </remarks>
    public IReadOnlyList<AgentSkillScriptDefinition> Scripts { get; init; } = [];

    /// <summary>
    /// The fingerprint of the whole script set: every script's name and content
    /// hash. Computed, never stored; a value sent in a request is ignored.
    /// </summary>
    /// <remarks>
    /// A grant without a script name (<see cref="SkillScriptGrant.ScriptName"/> is
    /// <see langword="null"/>) pins this value. Adding, removing, renaming, or
    /// changing any script changes it, and the grant stops authorizing every script
    /// of the skill until it is granted again. A skill without scripts has a
    /// fingerprint too, so its first script changes it. 64 upper-case hexadecimal
    /// characters (SHA-256).
    /// </remarks>
    public string ScriptSetHash => SkillScriptHashing.ComputeSetHash(Scripts);

    /// <summary>
    /// Where the definition comes from: <see cref="AgentDefinitionOrigin.Code"/>
    /// for a skill registered with <c>AddSkill</c>, <see cref="AgentDefinitionOrigin.Database"/>
    /// for a stored one.
    /// </summary>
    /// <remarks>
    /// A code skill wins over a stored skill with the same name: it is the one the
    /// runtime loads and the one whose scripts run. <c>GET /api/skills/{name}</c>
    /// resolves the name the same way and reports which one it returned.
    /// </remarks>
    public AgentDefinitionOrigin Origin { get; init; } = AgentDefinitionOrigin.Database;

    /// <summary>The skill's creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The skill's last-updated time.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A readable resource carried with an <see cref="AgentSkillDefinition"/>.</summary>
public sealed record AgentSkillResourceDefinition
{
    /// <summary>The resource name, unique within the skill.</summary>
    public required string Name { get; init; }

    /// <summary>The resource description.</summary>
    public string? Description { get; init; }

    /// <summary>The resource media type.</summary>
    public string MediaType { get; init; } = "text/plain";

    /// <summary>The resource's text content.</summary>
    public required string Content { get; init; }
}

/// <summary>
/// A server-executable script stored together with an <see cref="AgentSkillDefinition"/>.
/// </summary>
/// <remarks>
/// <strong>This content runs on the server.</strong> Creating the record does
/// not grant execution permission: execution requires
/// <c>TraconSkillScriptOptions.AllowStoredScripts</c> to be enabled on
/// the code side, and a <see cref="SkillScriptGrant"/> pinned to this content
/// (<see cref="ContentHash"/>). Changing the content stops the script until it
/// is granted again.
/// </remarks>
public sealed record AgentSkillScriptDefinition
{
    /// <summary>The script name, unique within the skill.</summary>
    public required string Name { get; init; }

    /// <summary>A description telling the model what the script does.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The file extension (without the dot, for example <c>py</c>). The
    /// interpreter is selected from the allowlist through this value.
    /// </summary>
    public required string Extension { get; init; }

    /// <summary>The script's source text.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The argument schema reported to the model. Must be a valid JSON Schema
    /// object; if <see langword="null"/>, the script is called with no arguments.
    /// </summary>
    public string? ParametersSchema { get; init; }

    /// <summary>
    /// The hash of what runs: the extension, the content, and the argument schema.
    /// Computed, never stored; a value sent in a request is ignored.
    /// </summary>
    /// <remarks>
    /// A grant for this script (<see cref="SkillScriptGrant.ScriptName"/> set) pins
    /// this value; the script runs only while its current hash equals the pinned
    /// one. <see cref="Description"/> and <see cref="Name"/> are not part of it: the
    /// description reaches only the model, and the name is the grant's key.
    /// 64 upper-case hexadecimal characters (SHA-256).
    /// </remarks>
    public string ContentHash => SkillScriptHashing.ComputeScriptHash(Extension, Content, ParametersSchema);
}
