namespace Tracon;

/// <summary>The stable codes of the refusals the definition write rules make.</summary>
/// <remarks>
/// A refusal that the agent validator already names keeps the validator's own code
/// (<see cref="ValidationCodes"/>); these are the refusals the save endpoints made without one.
/// </remarks>
internal static class WriteRuleCodes
{
    /// <summary>The agent name is empty.</summary>
    public const string AgentNameEmpty = "agent_name_empty";

    /// <summary>The agent has no model binding.</summary>
    public const string AgentModelMissing = "agent_model_missing";

    /// <summary>The name belongs to an agent defined in code.</summary>
    public const string AgentCodeDefined = "agent_code_defined";

    /// <summary>The name belongs to an agent of a custom agent source.</summary>
    public const string AgentCustomSource = "agent_custom_source";

    /// <summary>The name is taken by a stored agent and the caller may not update it.</summary>
    public const string AgentNameInUse = "agent_name_in_use";

    /// <summary>The agent definition is invalid (the entity validation found an error).</summary>
    public const string AgentDefinitionInvalid = "agent_definition_invalid";

    /// <summary>The workflow definition is invalid.</summary>
    public const string WorkflowDefinitionInvalid = "workflow_definition_invalid";

    /// <summary>The name belongs to a skill defined in code.</summary>
    public const string SkillCodeDefined = "skill_code_defined";

    /// <summary>The skill is invalid.</summary>
    public const string SkillInvalid = "skill_invalid";

    /// <summary>A script of the skill is invalid.</summary>
    public const string SkillScriptInvalid = "skill_script_invalid";

    /// <summary>The MCP server is invalid.</summary>
    public const string McpServerInvalid = "mcp_server_invalid";

    /// <summary>The MCP server waives approval and the caller lacks the scope for it.</summary>
    public const string McpApprovalWaiverDenied = "mcp_approval_waiver_denied";
}
