namespace Tracon;

/// <summary>The outcome of a code-defined approval policy (<see cref="ToolApprovalContext"/>).</summary>
public enum ToolApprovalPolicyDecision
{
    /// <summary>
    /// The policy does not decide; the data rules (<see cref="ToolApprovalRule"/>)
    /// decide instead.
    /// </summary>
    Undecided = 0,

    /// <summary>The call is approved without asking the user. Overrides the data rules.</summary>
    NotRequired = 1,

    /// <summary>The call must ask the user for approval. Overrides the data rules.</summary>
    Required = 2,
}
