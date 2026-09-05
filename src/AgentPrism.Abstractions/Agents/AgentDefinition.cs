using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// The full definition of an agent. The same type is used whether the agent is
/// declared in code or stored in the database; <see cref="Origin"/> tells the two apart.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToolNames"/> is a list of <em>names</em> only, never code. A definition
/// can point only at a tool registered in <c>IToolRegistry</c> in code. This is one of
/// the security boundaries of AgentPrism: the user interface can create an agent, but
/// it cannot define executable code.
/// </para>
/// <para>
/// A definition never carries credentials such as an API key. Provider credentials come
/// from configuration and are not written to the database.
/// </para>
/// </remarks>
public sealed record AgentDefinition
{
    /// <summary>Gets the unique name of the agent, used as the key in the catalog and in API routes.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the name shown in the user interface. <c>Name</c> is used when it is empty.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets a short description of what the agent does.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the system instructions passed to the model.</summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Gets culture-keyed instructions. The key is a BCP-47 tag (<c>"en"</c>, <c>"tr"</c>);
    /// a region subtag (<c>"tr-TR"</c>) falls back to its parent (<c>"tr"</c>). A run's
    /// requested culture that matches neither falls back to <c>Instructions</c> -
    /// resolution never fails.
    /// </summary>
    public IReadOnlyDictionary<string, string>? InstructionsByCulture { get; init; }

    /// <summary>Gets the provider and model binding to use.</summary>
    public required ModelBinding Model { get; init; }

    /// <summary>
    /// Gets the names of the tools this agent may use. Every name must match a tool
    /// registered in code; otherwise building the agent fails.
    /// </summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>
    /// Gets the names of the skills this agent may load at run time. Every name must
    /// match an enabled skill found in code or in the skill store.
    /// </summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>
    /// Gets the names of the other agents this agent may call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every name must resolve to an agent in the catalog. The call graph is checked
    /// <strong>when the definition is saved</strong>: self calls and indirect cycles
    /// are rejected.
    /// </para>
    /// <para>
    /// The static check alone is not enough - a factory agent on the code side carries
    /// no graph. A depth counter therefore also runs at run time
    /// (<see cref="AgentRunBudget.MaxDepth"/>).
    /// </para>
    /// <para>
    /// A child agent runs in the <strong>same tenant</strong> and cannot change the tenant.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>
    /// Gets how long this agent waits for the agents it calls. When
    /// <see langword="null"/>, <c>AgentPrismAgentGraphOptions.ChildDeadline</c>
    /// and <c>WaitTimeout</c> apply instead. Ignored when
    /// <see cref="CallableAgentNames"/> is empty.
    /// </summary>
    public SubAgentSettings? SubAgents { get; init; }

    /// <summary>
    /// Gets the MCP resources added to the run context (mode A). Each item has the
    /// form <c>"{server}:{uri}"</c>. They are read at the start of the run and are
    /// predictable; every run receives the same resources.
    /// </summary>
    /// <remarks>
    /// Reading resources requires the <c>AgentPrism.Mcp</c> package to be registered
    /// (<c>UseMcp()</c>); otherwise building the agent fails. A size limit applies:
    /// 64 KB per resource, 256 KB in total.
    /// </remarks>
    public IReadOnlyList<string> McpResourceUris { get; init; } = [];

    /// <summary>
    /// Gets the harness settings. When <see langword="null"/> a plain chat agent is
    /// produced; when populated, harness capabilities such as context compaction and
    /// todo tracking are enabled.
    /// </summary>
    public HarnessSettings? Harness { get; init; }

    /// <summary>
    /// Gets the context compaction settings. When <see langword="null"/> no compaction
    /// is applied.
    /// </summary>
    public CompactionSettings? Compaction { get; init; }

    /// <summary>
    /// Gets the memory provider settings. When <see langword="null"/> no memory provider
    /// is added.
    /// </summary>
    public MemorySettings? Memory { get; init; }

    /// <summary>Gets the origin of the definition: code or database.</summary>
    public AgentDefinitionOrigin Origin { get; init; } = AgentDefinitionOrigin.Database;

    /// <summary>
    /// Gets the definition version. Every save increments this value and so naturally
    /// invalidates the compiled agent cache.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the tenant this definition belongs to. A single-tenant setup uses the default value.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the time the definition last changed (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Gets free-form, application-specific metadata.</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the schema of named placeholders (<c>{{name}}</c>) this definition's
    /// instructions may reference.
    /// </summary>
    /// <remarks>
    /// Validated at compile time: see <see cref="AgentParameter"/>. A run
    /// supplies concrete values through <c>AgentRunRequest.Parameters</c>; the
    /// definition itself carries only the schema, never a value.
    /// </remarks>
    public IReadOnlyList<AgentParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Gets the name of another definition whose <c>Instructions</c> text is
    /// prepended to this definition's own resolved instructions at compile time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shared instructions block is an ordinary <see cref="AgentDefinition"/> -
    /// there is no separate type or table for it. It gets versioning, tenancy,
    /// and the audit trail for free because it is saved through the same
    /// <c>IAgentDefinitionStore</c>.
    /// </para>
    /// <para>
    /// The reference cannot be recursive: a definition whose own
    /// <c>SharedInstructionsName</c> is set cannot be referenced by
    /// another definition. This is checked at compile time and rejected
    /// outright rather than resolved through a cycle-detecting walk - a single
    /// disallowed hop is enough for the "shared block" use case and keeps the
    /// feature far from being a template engine.
    /// </para>
    /// </remarks>
    public string? SharedInstructionsName { get; init; }
}
