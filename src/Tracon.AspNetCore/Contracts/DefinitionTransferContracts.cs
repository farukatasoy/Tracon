using System.ComponentModel;
using System.Text.Json;

namespace Tracon;

/// <summary>The file kind and version of a definition bundle.</summary>
/// <remarks>
/// <c>formatVersion</c> is a long-lived contract: a file written today must still be read by a later Tracon.
/// A change that adds a field a v1 reader would ignore is not allowed to stay <c>1</c>, because the reader
/// refuses unknown fields rather than drop them (phase 269).
/// </remarks>
internal static class DefinitionBundleFormat
{
    /// <summary>The value of <c>format</c> in every bundle.</summary>
    public const string Name = "tracon.definitions";

    /// <summary>The only <c>formatVersion</c> this Tracon reads and writes.</summary>
    public const int CurrentVersion = 1;
}

[Description(
    "A `tracon.definitions` file: the agents, workflows, skills and MCP servers one Tracon installation holds in\n" +
    "its database, ready to be imported into another one. It carries no tenant, no version history, no run, no\n" +
    "session, no `secret`, no OAuth token, no plain MCP header value and no script permission.")]
internal sealed record DefinitionBundle
{
    [Description("The file kind. Always `tracon.definitions`.")]
    public required string Format { get; init; }

    [Description("The file format version. Always `1` today; a larger number is refused with `400`.")]
    public required int FormatVersion { get; init; }

    [Description("When the file was written.")]
    public DateTimeOffset ExportedAt { get; init; }

    [Description("The agents in the file.")]
    public IReadOnlyList<ExportedAgent> Agents { get; init; } = [];

    [Description("The workflows in the file.")]
    public IReadOnlyList<ExportedWorkflow> Workflows { get; init; } = [];

    [Description("The stored skills in the file. A skill defined in code or on disk is not exported.")]
    public IReadOnlyList<ExportedSkill> Skills { get; init; } = [];

    [Description("The remote MCP servers in the file.")]
    public IReadOnlyList<ExportedMcpServer> McpServers { get; init; } = [];

    [Description(
        "What the export could not carry or translate, for example a version pin that points at a version\n" +
        "the file does not hold. The import ignores this field.")]
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

[Description("One agent of a bundle.")]
internal sealed record ExportedAgent
{
    [Description(
        "The version the agent had on the host that wrote the file. Only the translation of version pins reads\n" +
        "it; the importing host numbers its own versions.")]
    public required int SourceVersion { get; init; }

    [Description("The definition, in the shape `POST /api/agents` takes.")]
    public required AgentDefinitionRequest Definition { get; init; }
}

[Description("One workflow of a bundle.")]
internal sealed record ExportedWorkflow
{
    [Description("The workflow name. A workflow takes its name from the path of `PUT /api/workflows/{name}`.")]
    public required string Name { get; init; }

    [Description("The version the workflow had on the host that wrote the file.")]
    public required int SourceVersion { get; init; }

    [Description("The definition, in the shape `PUT /api/workflows/{name}` takes.")]
    public required WorkflowSaveRequest Definition { get; init; }
}

[Description("One stored skill of a bundle.")]
internal sealed record ExportedSkill
{
    [Description("The version the skill had on the host that wrote the file.")]
    public required int SourceVersion { get; init; }

    [Description(
        "The skill, in the shape `PUT /api/skills/{name}` takes. A script's permission to run is not part of it:\n" +
        "the importing host grants it.")]
    public required AgentSkillRequest Definition { get; init; }
}

[Description("One remote MCP server of a bundle.")]
internal sealed record ExportedMcpServer
{
    [Description("The server name. A server takes its name from the path of `PUT /api/mcp-servers/{name}`.")]
    public required string Name { get; init; }

    [Description(
        "The server, in the shape `PUT /api/mcp-servers/{name}` takes, without `headers`: a plain header value is\n" +
        "never written to a file, so the importing host keeps the values it already holds. The credential header\n" +
        "keys (`headerConfigurationKeys`) carry names only.")]
    public required McpServerRequest Definition { get; init; }

    [Description("The names of the plain headers the server sends. Their values are not in the file.")]
    public IReadOnlyList<string> HeaderNames { get; init; } = [];
}

/// <summary>The kinds of definition a bundle carries.</summary>
internal static class DefinitionKinds
{
    /// <summary>An agent.</summary>
    public const string Agent = "agent";

    /// <summary>A workflow.</summary>
    public const string Workflow = "workflow";

    /// <summary>A stored skill.</summary>
    public const string Skill = "skill";

    /// <summary>A remote MCP server.</summary>
    public const string McpServer = "mcpServer";
}

/// <summary>The actions an import reports for one item.</summary>
internal static class DefinitionImportActions
{
    /// <summary>The target has no such definition; the import creates it.</summary>
    public const string Create = "create";

    /// <summary>The target has the agent or workflow with other content; the import writes a new version.</summary>
    public const string NewVersion = "newVersion";

    /// <summary>The target has the skill or MCP server with other content; the import overwrites it in place.</summary>
    public const string Update = "update";

    /// <summary>The target already holds the same content; the import writes nothing.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>A write rule refuses the item.</summary>
    public const string Rejected = "rejected";

    /// <summary>The item waits for an earlier step (a new MCP server whose tool an agent uses).</summary>
    public const string Blocked = "blocked";

    /// <summary>The caller left the item's kind out of <c>types</c>.</summary>
    public const string Skipped = "skipped";
}

[Description(
    "What an import found out about a bundle and, when it was applied, what it wrote. A bundle with one refused\n" +
    "item writes nothing.")]
internal sealed record DefinitionImportReport
{
    [Description("Whether the call was a dry run. A dry run writes nothing.")]
    public required bool DryRun { get; init; }

    [Description("Whether every item can be written. `false` when any item is `rejected` or `blocked`.")]
    public required bool Valid { get; init; }

    [Description("Whether the import wrote the definitions.")]
    public required bool Applied { get; init; }

    [Description(
        "Whether a write that started did not finish: a store failed after earlier items were written. The\n" +
        "items in `written` are in the target; running the import again writes the rest.")]
    public bool Partial { get; init; }

    [Description("One entry for each item of the bundle, in the order the import writes them.")]
    public required IReadOnlyList<DefinitionImportItem> Items { get; init; }

    [Description("The items that were written, as `kind:name`.")]
    public IReadOnlyList<string> Written { get; init; } = [];
}

[Description("What an import does, or did, with one item of a bundle.")]
internal sealed record DefinitionImportItem
{
    [Description("The kind: `agent`, `workflow`, `skill` or `mcpServer`.")]
    public required string Type { get; init; }

    [Description("The name of the definition.")]
    public required string Name { get; init; }

    [Description("The action: `create`, `newVersion`, `update`, `unchanged`, `rejected`, `blocked` or `skipped`.")]
    public required string Action { get; init; }

    [Description("The version the item had on the host that wrote the file.")]
    public int? SourceVersion { get; init; }

    [Description(
        "The version the item has, or will have, in the target. After a dry run of a new version it is the\n" +
        "number the store will most likely assign.")]
    public int? TargetVersion { get; init; }

    [Description("The JSON paths whose values differ between `current` and `incoming`.")]
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];

    [Description("The definition the target holds now, in the shape of the file. Absent for a new item.")]
    public JsonElement? Current { get; init; }

    [Description("The definition the import would write, in the shape of the file, with its version pins translated.")]
    public JsonElement? Incoming { get; init; }

    [Description("The findings for this item.")]
    public IReadOnlyList<DefinitionImportMessage> Messages { get; init; } = [];
}

[Description("One finding of an import about one item.")]
internal sealed record DefinitionImportMessage
{
    [Description("`Error` stops the import; `Warning` does not.")]
    public required ValidationSeverity Severity { get; init; }

    [Description("The stable, machine-readable code, for example `import_pin_untranslatable` or `unknown_skill`.")]
    public required string Code { get; init; }

    [Description("The human-readable description. It is not translated.")]
    public required string Message { get; init; }

    [Description("The path of the offending field inside the definition, or `null` when it points at no field.")]
    public string? Path { get; init; }

    /// <summary>Creates a message from a validation message.</summary>
    /// <param name="message">The validation message.</param>
    /// <returns>The import message.</returns>
    public static DefinitionImportMessage From(ValidationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new DefinitionImportMessage
        {
            Severity = message.Severity,
            Code = message.Code,
            Message = message.Message,
            Path = message.Path,
        };
    }
}

/// <summary>The stable codes an import report uses for the findings only an import makes.</summary>
internal static class DefinitionImportCodes
{
    /// <summary>A version pin points at a version the bundle does not hold.</summary>
    public const string PinUntranslatable = "import_pin_untranslatable";

    /// <summary>The same name appears twice in a bundle.</summary>
    public const string DuplicateName = "import_duplicate_name";

    /// <summary>An agent uses a tool of an MCP server the bundle would create or change.</summary>
    public const string McpStageRequired = "import_mcp_stage_required";

    /// <summary>A new MCP server's plain headers have to be entered in the target.</summary>
    public const string McpHeaderValuesNeeded = "import_mcp_header_values_needed";

    /// <summary>A server that uses OAuth has to be authorized again in the target.</summary>
    public const string McpOAuthReauthorize = "import_mcp_oauth_reauthorize";

    /// <summary>A skill script changed, so its existing permission no longer holds.</summary>
    public const string ScriptPermissionReset = "import_script_permission_reset";

    /// <summary>A server moves to another host and keeps stored headers.</summary>
    public const string McpHeadersKeptAcrossHosts = "import_mcp_headers_kept_across_hosts";

    /// <summary>A store failed while the import wrote an item.</summary>
    public const string StoreFailed = "import_store_failed";
}
