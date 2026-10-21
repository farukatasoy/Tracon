using System.ComponentModel;
using System.Text.Json;

namespace Tracon;

/// <remarks>
/// <see cref="WorkflowFunctionDescriptor"/> carries <see cref="Type"/>
/// properties, and <c>System.Text.Json</c> refuses to serialize a
/// <see cref="Type"/> instance (measured: <c>NotSupportedException</c>) - the
/// HTTP layer projects the CLR type down to its display name instead.
/// </remarks>
[Description("Wire-safe view of a registered function node.")]
internal sealed record WorkflowFunctionResponse
{
    [Description("The function's unique name.")]
    public required string Name { get; init; }

    [Description("The short description of what the function does.")]
    public string? Description { get; init; }

    [Description("The display name of the CLR type the function accepts.")]
    public required string InputType { get; init; }

    [Description("The display name of the CLR type the function returns.")]
    public required string OutputType { get; init; }

    /// <remarks>
    /// For display: it lists the member names, their types and which are required,
    /// so a form or an operator can write a valid object. The save endpoint does the
    /// checking, by reading the object into the function's settings type. A settings
    /// type whose schema could not be produced is the bare <c>{"type":"object"}</c>,
    /// never <see langword="null"/>.
    /// </remarks>
    [Description(
        "The JSON schema of the settings a node of this function takes, or\n" +
        "`null` when the function takes none (a node of it must carry\n" +
        "no `settings`).")]
    public JsonElement? SettingsSchema { get; init; }

    [Description(
        "The timeout the function was registered with: the longest one call may take when\n" +
        "its node sets no `timeout` of its own. `null` when the\n" +
        "function has no time limit of its own.")]
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

/// <remarks>
/// The name comes from the <em>path</em>, not the body. Having two sources
/// would raise the question of which one wins when they conflict.
/// </remarks>
[Description("Request to save a workflow definition.")]
internal sealed record WorkflowSaveRequest
{
    [Description("Name shown in the UI.")]
    public string? DisplayName { get; init; }

    [Description("Short description of what the workflow does.")]
    public string? Description { get; init; }

    [Description("Built-in pattern to use.")]
    public WorkflowKind Kind { get; init; }

    [Description("Names of agents to add to the graph.")]
    public IReadOnlyList<string>? AgentNames { get; init; }

    [Description(
        "Ordered agent/function node list for a Sequential workflow that mixes\n" +
        "function nodes in with agents. Mutually exclusive with\n" +
        "AgentNames; leave both empty or set only one.")]
    public IReadOnlyList<WorkflowNodeReference>? Nodes { get; init; }

    [Description("Name of the manager agent. Only for WorkflowKind.Magentic.")]
    public string? ManagerAgentName { get; init; }

    [Description("Maximum number of turns.")]
    public int? MaxIterations { get; init; }

    [Description("Handoff instructions. Only for WorkflowKind.Handoff.")]
    public string? HandoffInstructions { get; init; }

    [Description(
        "Whether the manager agent's plan must be approved by a human. Only for\n" +
        "WorkflowKind.Magentic.")]
    public bool RequirePlanApproval { get; init; }

    [Description(
        "The run parameters the workflow accepts. Leave empty for a workflow that\n" +
        "takes none. Replaced as a whole on every save, like every other field.")]
    public IReadOnlyList<AgentParameter>? Parameters { get; init; }

    /// <remarks>
    /// A value above the installation's run timeout is refused unless the host sets a
    /// higher ceiling (<c>Tracon:Workflows:MaxRunTimeout</c>); see
    /// <see cref="WorkflowDefinition.RunTimeout"/>.
    /// </remarks>
    [Description(
        "The longest time one run of this workflow may take, as `hh:mm:ss`. Leave it\n" +
        "out to use the installation's `Tracon:Workflows:RunTimeout`.")]
    public TimeSpan? RunTimeout { get; init; }

    /// <remarks>
    /// Replaced on every save like every other field, so a client that does not send it resets
    /// the workflow to <see cref="WorkflowResponseMode.Branch"/>. A run that is waiting keeps the
    /// mode of the version it recorded.
    /// </remarks>
    [Description(
        "How the workflow treats more than one answer to the same pending human request.\n" +
        "Leave it out for WorkflowResponseMode.Branch, where every answer opens a\n" +
        "new run; WorkflowResponseMode.SingleAnswer allows one answer per request.")]
    public WorkflowResponseMode ResponseMode { get; init; }

    /// <remarks>
    /// When the time passes the run that waits is closed and its stored rows can be released by
    /// retention; see <see cref="WorkflowDefinition.RequestTimeout"/>. Replaced on every save like
    /// every other field, so a client that does not send it removes the limit. A run that is
    /// waiting keeps the limit of the version it recorded.
    /// </remarks>
    [Description(
        "How long a human request of this workflow may wait for an answer, as `d.hh:mm:ss`,\n" +
        "from one minute to 365 days. Leave it out and a request waits without limit.")]
    public TimeSpan? RequestTimeout { get; init; }
}

[Description("Request to run a workflow.")]
internal sealed record WorkflowRunHttpRequest
{
    [Description("User message to feed into the graph.")]
    public string? Message { get; init; }

    [Description(
        "Identifier of the execution session. If left empty, one is generated.\n" +
        "Checkpoints are grouped under this value.")]
    public string? SessionId { get; init; }

    [Description(
        "Values for the parameters the workflow declares, by name. A missing\n" +
        "required value, an undeclared name or an over-long value is refused with\n" +
        "`400` before the stream starts. Every agent step receives the values\n" +
        "its own schema declares.")]
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}

[Description("Request to resume a workflow from a checkpoint.")]
internal sealed record WorkflowResumeHttpRequest
{
    [Description(
        "Identifier of the checkpoint to resume from. If left empty, the run's\n" +
        "most recent checkpoint is used.")]
    public string? CheckpointId { get; init; }

    /// <remarks>
    /// A version the workflow does not have is refused with <c>400</c>; the run never
    /// falls back to the current definition. A version whose graph differs from the one the
    /// checkpoint was written with fails the run with "the graph's structure differs".
    /// </remarks>
    [Description(
        "The version of the workflow's stored definition to resume with. Leave it out to\n" +
        "resume with the version the run recorded, so an edit made while the run waited does\n" +
        "not change what resumes.")]
    public int? DefinitionVersion { get; init; }
}

/// <remarks>
/// Which field is read is determined by the port's response type; the server
/// reports this in the <see cref="WorkflowPendingRequest.Form"/> field. A
/// response that cannot be translated is rejected with <c>400</c> — silently
/// accepting a response of the wrong type would break execution at a point
/// that is hard to understand.
/// </remarks>
[Description("Response to a pending human input request.")]
internal sealed record WorkflowRespondHttpRequest
{
    [Description("Identifier of the request being answered.")]
    public required string RequestId { get; init; }

    [Description(
        "Yes/no response. In a plan approval, `true` approves\n" +
        "the plan; `false` sends it back with the correction in\n" +
        "the `Text` field. In a tool approval it approves or refuses the tool call.")]
    public bool? Approved { get; init; }

    [Description(
        "Text response; in a plan approval this is the correction instruction, in a tool approval the\n" +
        "reason the model reads.")]
    public string? Text { get; init; }

    [Description("Free-form response body. Resolved against the port's response type. A tool approval takes none.")]
    public JsonElement? Data { get; init; }

    [Description(
        "Only for a tool approval answered with `approved: true`: also saves the approval as a rule, so the\n" +
        "same agent is not asked again for this tool. Any other use is refused with `400`.")]
    public bool? Remember { get; init; }

    [Description(
        "Identifier of the checkpoint to resume. If left empty, the run's most\n" +
        "recent checkpoint is used.")]
    public string? CheckpointId { get; init; }

    [Description(
        "The version of the workflow's stored definition to continue with. Leave it out to\n" +
        "continue with the version the waiting run recorded. It behaves like\n" +
        "`definitionVersion` on a resume.")]
    public int? DefinitionVersion { get; init; }
}

[Description("Request to roll a workflow definition back to a previous version.")]
internal sealed record WorkflowRollbackRequest
{
    [Description("Version number to roll back to.")]
    public required int Version { get; init; }
}

[Description(
    "Raw JSON response for comparing two workflow definition versions. The diff is not\n" +
    "computed on the server; the client compares the two definitions field by field.")]
internal sealed record WorkflowVersionDiffResponse
{
    [Description("Left (usually older) side of the comparison.")]
    public required WorkflowDefinition Left { get; init; }

    [Description("Right (usually newer) side of the comparison.")]
    public required WorkflowDefinition Right { get; init; }
}
