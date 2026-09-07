namespace AgentPrism;

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
    /// update does not erase it. AgentPrism itself never reads these keys.
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

/// <summary>Request to create or update a skill.</summary>
public sealed record AgentSkillRequest
{
    /// <summary>Skill name.</summary>
    public required string Name { get; init; }

    /// <summary>Skill description.</summary>
    public required string Description { get; init; }

    /// <summary>Markdown instructions.</summary>
    public required string Instructions { get; init; }

    /// <summary>Compatibility statement.</summary>
    public string? Compatibility { get; init; }

    /// <summary>Skill license.</summary>
    public string? License { get; init; }

    /// <summary>Allowed-tools declaration from the MAF frontmatter.</summary>
    public string? AllowedTools { get; init; }

    /// <summary>Application-specific metadata.</summary>
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Metadata { get; init; }
        = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

    /// <summary>Whether the skill is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Skill resources.</summary>
    public IReadOnlyList<AgentSkillResourceDefinition> Resources { get; init; } = [];

    /// <summary>
    /// Skill scripts.
    /// </summary>
    /// <remarks>
    /// Content written here <strong>can be executed on the server</strong>.
    /// Writing it is not enough by itself: a script runs only when
    /// <c>AgentPrismSkillScriptOptions</c> has both <c>Enabled</c> and
    /// <c>AllowStoredScripts</c> turned on, and a valid <c>SkillScriptGrant</c>
    /// exists for the tenant.
    /// </remarks>
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

/// <summary>Request to grant script execution permission.</summary>
/// <remarks>
/// Granting permission means authorizing code to run on the server on behalf
/// of this tenant. Because of this, the corresponding endpoint is open only to
/// the admin role and every request is written to the audit log.
/// </remarks>
public sealed record SkillScriptGrantRequest
{
    /// <summary>Name of the skill being granted permission.</summary>
    public required string SkillName { get; init; }

    /// <summary>
    /// Name of the script being granted permission. If <see langword="null"/>,
    /// every script of the skill is covered.
    /// </summary>
    public string? ScriptName { get; init; }

    /// <summary>Expiration time of the grant. If <see langword="null"/>, it is unlimited.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Detailed view of a single agent.
/// </summary>
/// <remarks>
/// The catalog shows agents defined both in code and in the database. Those
/// defined in code <strong>cannot be edited</strong>: on a name collision,
/// code wins, so a definition written to the database would
/// never resolve. <see cref="IsEditable"/> lets the UI know this in advance.
/// A code-defined agent still exposes its instructions where they can be read
/// without ambiguity — see <c>Definition</c> and <c>FactoryInstructions</c> —
/// even though it cannot be edited here.
/// </remarks>
public sealed record AgentDetailResponse
{
    /// <summary>Catalog summary.</summary>
    public required AgentDescriptor Descriptor { get; init; }

    /// <summary>
    /// The agent's definition. <see langword="null"/> only for a code agent built
    /// from a factory (<c>AddAgent(name, factory)</c>), which carries no
    /// <c>AgentDefinition</c> at all. A code agent declared with
    /// <c>AddAgent(AgentDefinition)</c> returns its in-memory definition here
    /// despite never being written to the database.
    /// </summary>
    public AgentDefinition? Definition { get; init; }

    /// <summary>
    /// Best-effort instructions read directly from the resolved agent when
    /// <c>Definition</c> is <see langword="null"/> because the agent is
    /// built from a factory (<c>AddAgent(name, factory)</c>) and its concrete
    /// type exposes them (currently only <c>Microsoft.Agents.AI.ChatClientAgent</c>).
    /// <see langword="null"/> when <c>Definition</c> is populated instead
    /// (read <c>Definition.Instructions</c> there), when the factory's concrete
    /// type does not expose instructions, or when invoking the factory to check
    /// failed — this field is diagnostic only and never blocks the response.
    /// </summary>
    public string? FactoryInstructions { get; init; }

    /// <summary>Whether this agent can be modified through the management API.</summary>
    public required bool IsEditable { get; init; }
}

/// <summary>Request to roll back a definition to a previous version.</summary>
public sealed record AgentRollbackRequest
{
    /// <summary>Version number to roll back to.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Raw JSON response for comparing two definition versions. The
/// diff is not computed on the server; the client compares the two raw
/// definitions field by field.
/// </summary>
public sealed record AgentVersionDiffResponse
{
    /// <summary>Left (usually older) side of the comparison.</summary>
    public required AgentDefinition Left { get; init; }

    /// <summary>Right (usually newer) side of the comparison.</summary>
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
    /// <c>AgentPrismSchedulingOptions.LaneByHandlerKey</c> maps
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
    /// A value for a name the schema does not declare is rejected, not
    /// silently dropped - a typo in a parameter name would otherwise
    /// disappear without a trace. A required parameter missing both a value
    /// here and a default in the schema keeps the run from starting at all.
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
/// <see cref="AgentPrismClientToolExtensions.AddClientTool"/>'s sibling on
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

/// <summary>
/// <c>202 Accepted</c> response for a queued run.
/// </summary>
/// <remarks>
/// Returned for a run started with the <c>Prefer: respond-async</c> header.
/// The same information is also carried in the <c>Location</c> header; the
/// body spares the client from having to also construct an event stream
/// address (<see cref="EventsLocation"/>).
/// </remarks>
public sealed record AcceptedRunResponse
{
    /// <summary>Run identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Identifier of the queue record carrying the work.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Address of the run record. Same as the <c>Location</c> header.</summary>
    public required string Location { get; init; }

    /// <summary>Address of the event stream.</summary>
    public required string EventsLocation { get; init; }
}
