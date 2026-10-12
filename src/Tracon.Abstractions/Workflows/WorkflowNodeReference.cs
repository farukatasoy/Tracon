using System.Text.Json;

namespace Tracon;

/// <summary>
/// Points to a single node entering a workflow's graph: an agent from the
/// catalog, or a function registered in code.
/// </summary>
/// <remarks>
/// <para>
/// Used only by <see cref="WorkflowKind.Sequential"/> definitions that mix
/// agent and function nodes. A definition that uses only agents may keep using
/// <see cref="WorkflowDefinition.AgentNames"/>; the two fields are mutually
/// exclusive (the validator in <c>Tracon.Core</c> enforces this). Both forms
/// are valid and both are accepted on save; the console writes
/// <see cref="WorkflowDefinition.Nodes"/> only when the chain holds at least one
/// function node.
/// </para>
/// <para>
/// <see cref="Kind"/> reuses <see cref="WorkflowNodeKind"/>, the same enum
/// the compiled graph reports. Only <see cref="WorkflowNodeKind.Agent"/> and
/// <see cref="WorkflowNodeKind.Function"/> are valid here — the other members
/// (<c>Orchestration</c>, <c>RequestPort</c>, <c>Output</c>) describe nodes
/// the compiler adds on its own; a definition never names one directly.
/// </para>
/// </remarks>
public sealed record WorkflowNodeReference
{
    /// <summary>
    /// Gets the referenced name: an agent name from the catalog when
    /// <see cref="Kind"/> is <see cref="WorkflowNodeKind.Agent"/>, or a
    /// function name from the code registry when it is
    /// <see cref="WorkflowNodeKind.Function"/>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Gets which registry <c>Name</c> is looked up in.</summary>
    public required WorkflowNodeKind Kind { get; init; }

    /// <summary>
    /// Gets the function node's settings: a JSON object checked against the
    /// settings type the function declares. <see langword="null"/> means the node
    /// carries no settings. Only for <see cref="WorkflowNodeKind.Function"/>; an
    /// agent node that carries settings is refused when the definition is saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Settings are values, not expressions.</strong> The function's code
    /// declares the shape (the <c>TSettings</c> type of
    /// <c>AddWorkflowFunction&lt;TInput, TOutput, TSettings&gt;</c>); an operator
    /// only writes values for it. A value is not substituted from a run
    /// parameter - a function that needs a run's value reads it itself.
    /// </para>
    /// <para>
    /// <strong>Never write a secret here.</strong> The settings are stored in the
    /// definition, in the audit trail, and are returned to every caller who can
    /// read the workflow. Where a function needs a credential, the
    /// settings carry the <em>name</em> of the configuration key and the function
    /// reads the value from <c>IConfiguration</c> itself.
    /// </para>
    /// <para>
    /// A value of <see cref="JsonValueKind.Undefined"/> or
    /// <see cref="JsonValueKind.Null"/> is read as <see langword="null"/>. An
    /// unset <see cref="JsonElement"/> cannot be written as JSON, and one such
    /// node would fail the whole workflow list.
    /// </para>
    /// </remarks>
    public JsonElement? Settings
    {
        get;
        init => field = value is { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } ? null : value;
    }
}
