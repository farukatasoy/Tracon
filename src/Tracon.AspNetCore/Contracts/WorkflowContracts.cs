using System.Text.Json;

namespace Tracon;

/// <summary>Wire-safe view of a registered function node.</summary>
/// <remarks>
/// <see cref="WorkflowFunctionDescriptor"/> carries <see cref="Type"/>
/// properties, and <c>System.Text.Json</c> refuses to serialize a
/// <see cref="Type"/> instance (measured: <c>NotSupportedException</c>) - the
/// HTTP layer projects the CLR type down to its display name instead.
/// </remarks>
public sealed record WorkflowFunctionResponse
{
    /// <summary>The function's unique name.</summary>
    public required string Name { get; init; }

    /// <summary>The short description of what the function does.</summary>
    public string? Description { get; init; }

    /// <summary>The display name of the CLR type the function accepts.</summary>
    public required string InputType { get; init; }

    /// <summary>The display name of the CLR type the function returns.</summary>
    public required string OutputType { get; init; }

    /// <summary>
    /// The JSON schema of the settings a node of this function takes, or
    /// <see langword="null"/> when the function takes none (a node of it must carry
    /// no <c>settings</c>).
    /// </summary>
    /// <remarks>
    /// For display: it lists the member names, their types and which are required,
    /// so a form or an operator can write a valid object. The save endpoint does the
    /// checking, by reading the object into the function's settings type. A settings
    /// type whose schema could not be produced is the bare <c>{"type":"object"}</c>,
    /// never <see langword="null"/>.
    /// </remarks>
    public JsonElement? SettingsSchema { get; init; }

    /// <summary>
    /// The timeout the function was registered with: the longest one call may take when
    /// its node sets no <c>timeout</c> of its own. <see langword="null"/> when the
    /// function has no time limit of its own.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Projects a descriptor into its wire-safe view.</summary>
    /// <param name="descriptor">The descriptor to project.</param>
    public static WorkflowFunctionResponse From(WorkflowFunctionDescriptor descriptor)
        => new()
        {
            Name = descriptor.Name,
            Description = descriptor.Description,
            InputType = descriptor.InputType.FullName ?? descriptor.InputType.Name,
            OutputType = descriptor.OutputType.FullName ?? descriptor.OutputType.Name,

            // An unset element is the one kind System.Text.Json cannot write, and one
            // descriptor carrying it would fail the whole function list.
            SettingsSchema = descriptor.SettingsSchema is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } schema
                ? schema
                : null,
            Timeout = descriptor.Timeout,
        };
}

/// <summary>Request to save a workflow definition.</summary>
/// <remarks>
/// The name comes from the <em>path</em>, not the body. Having two sources
/// would raise the question of which one wins when they conflict.
/// </remarks>
public sealed record WorkflowSaveRequest
{
    /// <summary>Name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Short description of what the workflow does.</summary>
    public string? Description { get; init; }

    /// <summary>Built-in pattern to use.</summary>
    public WorkflowKind Kind { get; init; }

    /// <summary>Names of agents to add to the graph.</summary>
    public IReadOnlyList<string>? AgentNames { get; init; }

    /// <summary>
    /// Ordered agent/function node list for a Sequential workflow that mixes
    /// function nodes in with agents. Mutually exclusive with
    /// <see cref="AgentNames"/>; leave both empty or set only one.
    /// </summary>
    public IReadOnlyList<WorkflowNodeReference>? Nodes { get; init; }

    /// <summary>Name of the manager agent. Only for <see cref="WorkflowKind.Magentic"/>.</summary>
    public string? ManagerAgentName { get; init; }

    /// <summary>Maximum number of turns.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>Handoff instructions. Only for <see cref="WorkflowKind.Handoff"/>.</summary>
    public string? HandoffInstructions { get; init; }

    /// <summary>
    /// Whether the manager agent's plan must be approved by a human. Only for
    /// <see cref="WorkflowKind.Magentic"/>.
    /// </summary>
    public bool RequirePlanApproval { get; init; }

    /// <summary>
    /// The run parameters the workflow accepts. Leave empty for a workflow that
    /// takes none. Replaced as a whole on every save, like every other field.
    /// </summary>
    public IReadOnlyList<AgentParameter>? Parameters { get; init; }

    /// <summary>
    /// The longest time one run of this workflow may take, as <c>hh:mm:ss</c>. Leave it
    /// out to use the installation's <c>Tracon:Workflows:RunTimeout</c>.
    /// </summary>
    /// <remarks>
    /// A value above the installation's run timeout is refused unless the host sets a
    /// higher ceiling (<c>Tracon:Workflows:MaxRunTimeout</c>); see
    /// <see cref="WorkflowDefinition.RunTimeout"/>.
    /// </remarks>
    public TimeSpan? RunTimeout { get; init; }
}

/// <summary>Request to run a workflow.</summary>
public sealed record WorkflowRunHttpRequest
{
    /// <summary>User message to feed into the graph.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Identifier of the execution session. If left empty, one is generated.
    /// Checkpoints are grouped under this value.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Values for the parameters the workflow declares, by name. A missing
    /// required value, an undeclared name or an over-long value is refused with
    /// <c>400</c> before the stream starts. Every agent step receives the values
    /// its own schema declares.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}

/// <summary>Request to resume a workflow from a checkpoint.</summary>
public sealed record WorkflowResumeHttpRequest
{
    /// <summary>
    /// Identifier of the checkpoint to resume from. If left empty, the run's
    /// most recent checkpoint is used.
    /// </summary>
    public string? CheckpointId { get; init; }

    /// <summary>
    /// The version of the workflow's stored definition to resume with. Leave it out to
    /// resume with the version the run recorded, so an edit made while the run waited does
    /// not change what resumes.
    /// </summary>
    /// <remarks>
    /// A version the workflow does not have is refused with <c>400</c>; the run never
    /// falls back to the current definition. A version whose graph differs from the one the
    /// checkpoint was written with fails the run with "the graph's structure differs".
    /// </remarks>
    public int? DefinitionVersion { get; init; }
}

/// <summary>Response to a pending human input request.</summary>
/// <remarks>
/// Which field is read is determined by the port's response type; the server
/// reports this in the <see cref="WorkflowPendingRequest.Form"/> field. A
/// response that cannot be translated is rejected with <c>400</c> — silently
/// accepting a response of the wrong type would break execution at a point
/// that is hard to understand.
/// </remarks>
public sealed record WorkflowRespondHttpRequest
{
    /// <summary>Identifier of the request being answered.</summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Yes/no response. In a plan approval, <see langword="true"/> approves
    /// the plan; <see langword="false"/> sends it back with the correction in
    /// the <c>Text</c> field.
    /// </summary>
    public bool? Approved { get; init; }

    /// <summary>Text response; in a plan approval this is the correction instruction.</summary>
    public string? Text { get; init; }

    /// <summary>Free-form response body. Resolved against the port's response type.</summary>
    public JsonElement? Data { get; init; }

    /// <summary>
    /// Identifier of the checkpoint to resume. If left empty, the run's most
    /// recent checkpoint is used.
    /// </summary>
    public string? CheckpointId { get; init; }

    /// <summary>
    /// The version of the workflow's stored definition to continue with. Leave it out to
    /// continue with the version the waiting run recorded. See
    /// <see cref="WorkflowResumeHttpRequest.DefinitionVersion"/>.
    /// </summary>
    public int? DefinitionVersion { get; init; }
}

/// <summary>Request to roll a workflow definition back to a previous version.</summary>
public sealed record WorkflowRollbackRequest
{
    /// <summary>Version number to roll back to.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Raw JSON response for comparing two workflow definition versions. The diff is not
/// computed on the server; the client compares the two definitions field by field.
/// </summary>
public sealed record WorkflowVersionDiffResponse
{
    /// <summary>Left (usually older) side of the comparison.</summary>
    public required WorkflowDefinition Left { get; init; }

    /// <summary>Right (usually newer) side of the comparison.</summary>
    public required WorkflowDefinition Right { get; init; }
}
