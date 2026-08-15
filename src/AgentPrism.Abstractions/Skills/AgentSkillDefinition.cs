using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// A markdown-based skill definition that can be loaded into an agent at run time.
/// </summary>
/// <remarks>
/// A skill carries instructions, resources, and <see cref="Scripts"/>. Script
/// execution is a separate security boundary: unless
/// <c>AgentPrismSkillScriptOptions.AllowStoredScripts</c> is enabled, stored
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
    /// The skill's scripts stored in the database.
    /// </summary>
    /// <remarks>
    /// These scripts run on the server. They are visible to the model only
    /// when <c>AgentPrismSkillScriptOptions.AllowStoredScripts</c> is on;
    /// while off, the record is stored but cannot be executed.
    /// </remarks>
    public IReadOnlyList<AgentSkillScriptDefinition> Scripts { get; init; } = [];

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
/// <c>AgentPrismSkillScriptOptions.AllowStoredScripts</c> to be enabled on
/// the code side, and a <see cref="SkillScriptGrant"/> record to exist.
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
}
