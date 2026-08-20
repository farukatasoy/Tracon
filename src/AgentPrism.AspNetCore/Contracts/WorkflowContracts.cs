using System.Text.Json;

namespace AgentPrism;

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

    /// <summary>Projects a descriptor into its wire-safe view.</summary>
    /// <param name="descriptor">The descriptor to project.</param>
    public static WorkflowFunctionResponse From(WorkflowFunctionDescriptor descriptor)
        => new()
        {
            Name = descriptor.Name,
            Description = descriptor.Description,
            InputType = descriptor.InputType.FullName ?? descriptor.InputType.Name,
            OutputType = descriptor.OutputType.FullName ?? descriptor.OutputType.Name,
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
}

/// <summary>Request to resume a workflow from a checkpoint.</summary>
public sealed record WorkflowResumeHttpRequest
{
    /// <summary>
    /// Identifier of the checkpoint to resume from. If left empty, the run's
    /// most recent checkpoint is used.
    /// </summary>
    public string? CheckpointId { get; init; }
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
}
