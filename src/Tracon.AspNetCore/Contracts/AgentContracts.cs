using System.ComponentModel;

namespace Tracon;

/// <summary>
/// Request to create or update an agent definition.
/// </summary>
/// <remarks>
/// Does not bind directly to <see cref="AgentDefinition"/>. The definition's
/// <c>Origin</c>, <c>Version</c>, <c>TenantId</c>, and <c>UpdatedAt</c> fields
/// belong to the server; letting the client set these would break version
/// history and tenant isolation.
/// </remarks>
public sealed record AgentDefinitionRequest
{
    /// <summary>Agent name. Unique within the catalog.</summary>
    public required string Name { get; init; }

    /// <summary>Name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Short description.</summary>
    public string? Description { get; init; }

    /// <summary>System instructions.</summary>
    public string? Instructions { get; init; }

    /// <summary>Culture-keyed instructions. See <see cref="AgentDefinition.InstructionsByCulture"/>.</summary>
    public IReadOnlyDictionary<string, string>? InstructionsByCulture { get; init; }

    /// <summary>Model binding: provider, model, and sampling settings.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>
    /// Names of tools to use. Tools are defined only in code; only the name of
    /// an already registered tool may be given here.
    /// </summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Names of skills that can be loaded at runtime.</summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>
    /// Names of other agents this agent may call.
    /// </summary>
    /// <remarks>
    /// Checked at the moment the call graph is saved: an unknown name,
    /// self-calling, and indirect cycles are rejected with <c>400 Bad Request</c>.
    /// </remarks>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>
    /// How long this agent waits for the agents it calls. See
    /// <c>AgentDefinition.SubAgents</c>. Ignored when
    /// <see cref="CallableAgentNames"/> is empty.
    /// </summary>
    public SubAgentSettings? SubAgents { get; init; }

    /// <summary>
    /// MCP resources added to the run context, each of the form
    /// <c>"{server}:{uri}"</c>. See <c>AgentDefinition.McpResourceUris</c>.
    /// </summary>
    public IReadOnlyList<string> McpResourceUris { get; init; } = [];

    /// <summary>Harness settings. If left empty, a plain chat agent is compiled.</summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>Context compaction settings. If left empty, no compaction is applied.</summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>Memory provider settings. If left empty, no memory provider is added.</summary>
    public MemorySettings? Memory { get; init; }

    /// <summary>
    /// Free-form, application-specific metadata. See <c>AgentDefinition.Metadata</c>.
    /// </summary>
    /// <remarks>
    /// Owned by the application, not the server: unlike <c>Origin</c>, <c>Version</c>,
    /// <c>TenantId</c> and <c>UpdatedAt</c>, this travels on the request so that an
    /// update does not erase it. Tracon itself never reads these keys.
    /// </remarks>
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Metadata { get; init; }
        = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

    /// <summary>The parameter schema. See <see cref="AgentDefinition.Parameters"/>.</summary>
    public IReadOnlyList<AgentParameter> Parameters { get; init; } = [];

    /// <summary>The referenced shared instructions block. See <c>AgentDefinition.SharedInstructionsName</c>.</summary>
    public string? SharedInstructionsName { get; init; }

    /// <summary>Converts the request into a persistable definition.</summary>
    /// <returns>A definition ready to be written to the database.</returns>
    public AgentDefinition ToDefinition()
        => new()
        {
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            Instructions = Instructions,
            InstructionsByCulture = InstructionsByCulture,
            Model = Model,
            ToolNames = ToolNames,
            SkillNames = SkillNames,
            CallableAgentNames = CallableAgentNames,
            SubAgents = SubAgents,
            McpResourceUris = McpResourceUris,
            Harness = Harness,
            Compaction = Compaction,
            Memory = Memory,
            Metadata = Metadata,
            Parameters = Parameters,
            SharedInstructionsName = SharedInstructionsName,
            Origin = AgentDefinitionOrigin.Database,
        };
}

[Description("Request to create or update a skill.")]
internal sealed record AgentSkillRequest
{
    [Description("Skill name.")]
    public required string Name { get; init; }

    [Description("Skill description.")]
    public required string Description { get; init; }

    [Description("Markdown instructions.")]
    public required string Instructions { get; init; }

    [Description("Compatibility statement.")]
    public string? Compatibility { get; init; }

    [Description("Skill license.")]
    public string? License { get; init; }

    [Description("Allowed-tools declaration from the MAF frontmatter.")]
    public string? AllowedTools { get; init; }

    [Description("Application-specific metadata.")]
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Metadata { get; init; }
        = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

    [Description("Whether the skill is enabled.")]
    public bool Enabled { get; init; } = true;

    [Description("Skill resources.")]
    public IReadOnlyList<AgentSkillResourceDefinition> Resources { get; init; } = [];

    /// <remarks>
    /// Content written here <strong>can be executed on the server</strong>.
    /// Writing it is not enough by itself: a script runs only when
    /// <c>TraconSkillScriptOptions</c> has both <c>Enabled</c> and
    /// <c>AllowStoredScripts</c> turned on, and the tenant has a valid
    /// <c>SkillScriptGrant</c> pinned to this exact content. Changing a granted
    /// script stops it until it is granted again.
    /// </remarks>
    [Description("Skill scripts.")]
    public IReadOnlyList<AgentSkillScriptDefinition> Scripts { get; init; } = [];

    /// <summary>Converts the request into a persistable skill definition.</summary>
    /// <param name="tenantId">The current tenant identifier.</param>
    /// <returns>A skill ready to be saved.</returns>
    public AgentSkillDefinition ToDefinition(string tenantId)
        => new()
        {
            TenantId = tenantId,
            Name = Name,
            Description = Description,
            Instructions = Instructions,
            Compatibility = Compatibility,
            License = License,
            AllowedTools = AllowedTools,
            Metadata = Metadata,
            Enabled = Enabled,
            Resources = Resources,
            Scripts = Scripts,
        };
}

/// <remarks>
/// Granting permission means authorizing code to run on the server on behalf
/// of this tenant. Because of this, the endpoint is open only to the admin role
/// (an API key needs <c>SecurityAdmin</c>), and in a multi-tenant host a grant for
/// a stored script also needs platform authority. Every grant is written to the
/// audit trail; a refused request writes nothing.
/// </remarks>
[Description("Request to grant script execution permission.")]
internal sealed record SkillScriptGrantRequest
{
    [Description("Name of the skill being granted permission.")]
    public required string SkillName { get; init; }

    [Description(
        "Name of the script being granted permission. If `null`,\n" +
        "every script of the skill is covered.")]
    public string? ScriptName { get; init; }

    [Description("Expiration time of the grant. If `null`, it is unlimited.")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <remarks>
    /// <para>
    /// Required for a skill stored in the database or registered in code (<c>400</c>
    /// without it). A value that no longer matches the content returns <c>409</c>:
    /// someone changed the scripts after they were read. Not needed for a skill that
    /// exists only on disk.
    /// </para>
    /// <para>
    /// A <c>POST /tracon/api/skill-script-grants</c> body, with the hash shortened
    /// (a real one is 64 hexadecimal characters):
    /// </para>
    /// <code language="json">
    /// {
    ///   "skillName": "invoice-analysis",
    ///   "scriptName": "total",
    ///   "expectedContentHash": "3F1C9A0B"
    /// }
    /// </code>
    /// </remarks>
    [Description(
        "The content the grant pins, read from `GET /api/skills/{name}`: the\n" +
        "script's `contentHash` when `scriptName` is set, the skill's\n" +
        "`scriptSetHash` when it is not.")]
    public string? ExpectedContentHash { get; init; }
}

/// <remarks>
/// The catalog shows agents defined both in code and in the database. Those
/// defined in code <strong>cannot be edited</strong>: on a name collision,
/// code wins, so a definition written to the database would
/// never resolve. <see cref="IsEditable"/> lets the UI know this in advance.
/// A code-defined agent still exposes its instructions where they can be read
/// without ambiguity — see <c>Definition</c> and <c>FactoryInstructions</c> —
/// even though it cannot be edited here.
/// </remarks>
[Description("Detailed view of a single agent.")]
internal sealed record AgentDetailResponse
{
    [Description("Catalog summary.")]
    public required AgentDescriptor Descriptor { get; init; }

    [Description(
        "The agent's definition. `null` only for a code agent built\n" +
        "from a factory (`AddAgent(name, factory)`), which carries no\n" +
        "`AgentDefinition` at all. A code agent declared with\n" +
        "`AddAgent(AgentDefinition)` returns its in-memory definition here\n" +
        "despite never being written to the database.")]
    public AgentDefinition? Definition { get; init; }

    [Description(
        "Best-effort instructions read directly from the resolved agent when\n" +
        "`Definition` is `null` because the agent is\n" +
        "built from a factory (`AddAgent(name, factory)`) and its concrete\n" +
        "type exposes them (currently only `Microsoft.Agents.AI.ChatClientAgent`).\n" +
        "`null` when `Definition` is populated instead\n" +
        "(read `Definition.Instructions` there), when the factory's concrete\n" +
        "type does not expose instructions, or when invoking the factory to check\n" +
        "failed — this field is diagnostic only and never blocks the response.")]
    public string? FactoryInstructions { get; init; }

    [Description("Whether this agent can be modified through the management API.")]
    public required bool IsEditable { get; init; }
}

[Description("Request to roll back a definition to a previous version.")]
internal sealed record AgentRollbackRequest
{
    [Description("Version number to roll back to.")]
    public required int Version { get; init; }
}

[Description(
    "Raw JSON response for comparing two definition versions. The\n" +
    "diff is not computed on the server; the client compares the two raw\n" +
    "definitions field by field.")]
internal sealed record AgentVersionDiffResponse
{
    [Description("Left (usually older) side of the comparison.")]
    public required AgentDefinition Left { get; init; }

    [Description("Right (usually newer) side of the comparison.")]
    public required AgentDefinition Right { get; init; }
}

/// <summary>Request for a trial run made from the UI.</summary>
public sealed record AgentRunRequest
{
    /// <summary>
    /// User message. May be left empty only if <see cref="Approvals"/> is sent.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Session identifier. If not given, the run is sessionless and no history
    /// is carried.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// The lane a queued run (<c>Prefer: respond-async</c>) is queued under.
    /// See <c>JobLanes</c>. Ignored for a synchronous run — nothing is
    /// queued. Left empty, the run uses <c>JobLanes.Default</c> (or whatever
    /// <c>TraconSchedulingOptions.LaneByHandlerKey</c> maps
    /// <c>JobHandlerKeys.AgentRun</c> to).
    /// </summary>
    public string? Lane { get; init; }

    /// <summary>
    /// The culture to resolve the agent's instructions with (see
    /// <c>AgentDefinition.InstructionsByCulture</c>). <see langword="null"/> uses the
    /// agent's default instructions.
    /// </summary>
    /// <remarks>
    /// Read only from this field. The <c>Accept-Language</c> HTTP header is
    /// deliberately <strong>not</strong> consulted: a browser header silently
    /// changing the content sent to the model would be a surprise, and it
    /// conflicts with's line that server-facing content is not
    /// translated from ambient request state.
    /// </remarks>
    public string? Culture { get; init; }

    /// <summary>
    /// Approval decisions for pending tool calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Approval is <strong>the input to the next turn</strong>: Microsoft
    /// Agent Framework returns a pending call as
    /// <c>ToolApprovalRequestContent</c> in the response, and the decision is
    /// expected in the messages of the next run. There is therefore no
    /// separate "continue" endpoint.
    /// </para>
    /// <para>
    /// Decisions are processed only when <see cref="SessionId"/> is given: the
    /// pending request lives in the session history and cannot be found in a
    /// sessionless run.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ToolApprovalDecision> Approvals { get; init; } = [];

    /// <summary>
    /// Results of client-side tool calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client-side tool (<c>AddClientTool</c>) is registered as a
    /// declaration only; the server never runs it. It produces a pending
    /// call the model is waiting on, and the result is <strong>the next
    /// turn's input</strong> — the same pattern <see cref="Approvals"/>
    /// uses for <c>ToolApprovalRequestContent</c>. There is no separate
    /// "continue" endpoint.
    /// </para>
    /// <para>
    /// Results are processed only when <see cref="SessionId"/> is given: the
    /// pending call lives in the session history and cannot be found in a
    /// sessionless run.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ClientToolResult> ToolResults { get; init; } = [];

    /// <summary>
    /// Identifiers of attachments previously uploaded via <c>POST /api/attachments</c>.
    /// </summary>
    /// <remarks>
    /// Each identifier must belong to the calling tenant; otherwise the
    /// request is rejected with <c>400</c>. Binary content is not carried in
    /// the message, only a small reference
    /// (<see cref="Microsoft.Extensions.AI.UriContent"/>) is added.
    /// </remarks>
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];

    /// <summary>
    /// Values for the target agent's <see cref="AgentDefinition.Parameters"/> schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value for a name the schema does not declare is rejected, not
    /// silently dropped - a typo in a parameter name would otherwise
    /// disappear without a trace. A required parameter missing both a value
    /// here and a default in the schema keeps the run from starting at all.
    /// </para>
    /// <para>
    /// A queued run (<c>Prefer: respond-async</c>) carries them too: they are
    /// checked here against the schema of the version the worker will run,
    /// travel in the job's payload, and are checked again where the run executes.
    /// Note that the job payload is not a protected column: the values are
    /// stored there in plain form, beside the message text.
    /// </para>
    /// <para>
    /// The run records the values it was bound with, defaults included. A replay
    /// and a continuation repeat them.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// Reference text attached to this run, kept apart from the agent's instructions.
    /// </summary>
    /// <remarks>
    /// This is a convention and an audit trail, not a security guarantee -
    /// see <see cref="AgentRunDocument"/>.
    /// </remarks>
    public IReadOnlyList<AgentRunDocument> Documents { get; init; } = [];
}

/// <summary>
/// The result of a single client-side tool call, sent back so the run can
/// continue.
/// </summary>
/// <remarks>
/// <see cref="TraconClientToolExtensions.AddClientTool"/>'s sibling on
/// the run request: the model calls a client-side tool, the server returns
/// the pending call to the caller instead of running it, and the caller
/// sends the outcome back through this contract.
/// </remarks>
public sealed record ClientToolResult
{
    /// <summary>
    /// Identifier of the pending call this result answers. Matches the
    /// <c>FunctionCallContent.CallId</c> the run response carried.
    /// </summary>
    public required string CallId { get; init; }

    /// <summary>
    /// The tool's result, given to the model as plain text. Required unless
    /// <c>ErrorMessage</c> is given.
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// A message describing why the client-side call failed, given to the
    /// model instead of <c>Result</c>.
    /// </summary>
    /// <remarks>
    /// A client-side tool can fail for reasons the server never sees — the
    /// user denied a browser permission, a DOM element was not found. The
    /// failure is reported to the model as ordinary tool output, not as an
    /// HTTP error: the run continues and the model can recover (retry,
    /// explain, ask a follow-up).
    /// </remarks>
    public string? ErrorMessage { get; init; }
}

/// <remarks>
/// Returned for a run started with the <c>Prefer: respond-async</c> header.
/// The same information is also carried in the <c>Location</c> header; the
/// body spares the client from having to also construct an event stream
/// address (<see cref="EventsLocation"/>).
/// </remarks>
[Description("`202 Accepted` response for a queued run.")]
internal sealed record AcceptedRunResponse
{
    [Description("Run identifier.")]
    public required Guid RunId { get; init; }

    [Description("Identifier of the queue record carrying the work.")]
    public required Guid JobId { get; init; }

    [Description("Address of the run record. Same as the `Location` header.")]
    public required string Location { get; init; }

    [Description("Address of the event stream.")]
    public required string EventsLocation { get; init; }
}
