namespace Tracon;

/// <summary>
/// The machine-readable codes of the findings that validating an agent definition returns
/// (<c>POST /api/agents/validate</c>, <c>ValidationMessage.Code</c>) and of an agent call that is refused for its
/// parameters.
/// </summary>
/// <remarks>
/// <para>
/// A client matches on a code: the console picks its heading from it, and the OpenAPI document calls it stable.
/// A value never changes inside a major version; a new code is a minor change. A test lists the values
/// and the release rehearsal compares that list with the one at the last released tag.
/// </para>
/// <para>
/// Internal on purpose: a .NET consumer receives these as text over HTTP and has no use for a constant. The three
/// parameter codes are also public constants of <c>AgentParameterValidator</c>; they bind to the values here.
/// </para>
/// </remarks>
internal static class ValidationCodes
{
    /// <summary>The definition names a model the catalog does not list.</summary>
    public const string UnknownModel = "unknown_model";

    /// <summary>A setting of the definition is out of range.</summary>
    public const string InvalidSetting = "invalid_setting";

    /// <summary>An MCP server of the definition cannot be reached.</summary>
    public const string McpUnreachable = "mcp_unreachable";

    /// <summary>The definition names a tool that is not registered.</summary>
    public const string UnknownTool = "unknown_tool";

    /// <summary>The definition names a skill that does not exist.</summary>
    public const string UnknownSkill = "unknown_skill";

    /// <summary>The definition does not compile.</summary>
    public const string CompilationError = "compilation_error";

    /// <summary>The model's provider does not run the hosted tool.</summary>
    public const string HostedToolUnsupported = "hosted_tool_unsupported";

    /// <summary>Validation finding <c>conversation_memory_settings_without_memory</c>.</summary>
    public const string ConversationMemorySettingsWithoutMemory = "conversation_memory_settings_without_memory";

    /// <summary>Validation finding <c>invalid_conversation_memory_limit</c>.</summary>
    public const string ConversationMemoryLimit = "invalid_conversation_memory_limit";

    /// <summary>Validation finding <c>skill_settings_without_skills</c>.</summary>
    public const string SkillSettingsWithoutSkills = "skill_settings_without_skills";

    /// <summary>Validation finding <c>workspace_settings_without_workspace</c>.</summary>
    public const string WorkspaceSettingsWithoutWorkspace = "workspace_settings_without_workspace";

    /// <summary>Validation finding <c>workspace_agent_name</c>.</summary>
    public const string WorkspaceAgentName = "workspace_agent_name";

    /// <summary>Validation finding <c>sampling_unsupported</c>.</summary>
    public const string SamplingUnsupported = "sampling_unsupported";

    /// <summary>Validation finding <c>sampling_field_unsupported</c>.</summary>
    public const string SamplingFieldUnsupported = "sampling_field_unsupported";

    /// <summary>Validation finding <c>sampling_field_rejected_by_model</c>.</summary>
    public const string SamplingFieldRejectedByModel = "sampling_field_rejected_by_model";

    /// <summary>Validation finding <c>sampling_value_out_of_range</c>.</summary>
    public const string SamplingValueOutOfRange = "sampling_value_out_of_range";

    /// <summary>Validation finding <c>output_truncation_risk</c>.</summary>
    public const string OutputTruncationRisk = "output_truncation_risk";

    /// <summary>Validation finding <c>response_schema_optional_required</c>.</summary>
    public const string ResponseSchemaOptionalRequired = "response_schema_optional_required";

    /// <summary>Validation finding <c>response_schema_keyword_not_enforced</c>.</summary>
    public const string ResponseSchemaKeywordNotEnforced = "response_schema_keyword_not_enforced";

    /// <summary>Validation finding <c>response_schema_strict_unsupported</c>.</summary>
    public const string ResponseSchemaStrictUnsupported = "response_schema_strict_unsupported";

    /// <summary>Validation finding <c>loop_judge_unavailable</c>.</summary>
    public const string LoopJudgeUnavailable = "loop_judge_unavailable";

    /// <summary>Validation finding <c>loop_criteria_empty</c>.</summary>
    public const string LoopCriteriaEmpty = "loop_criteria_empty";

    /// <summary>Validation finding <c>loop_max_iterations_invalid</c>.</summary>
    public const string LoopMaxIterationsInvalid = "loop_max_iterations_invalid";

    /// <summary>Validation finding <c>loop_outcome_invalid</c>.</summary>
    public const string LoopOutcomeInvalid = "loop_outcome_invalid";

    /// <summary>Validation finding <c>loop_criterion_kind_missing</c>.</summary>
    public const string LoopCriterionKindMissing = "loop_criterion_kind_missing";

    /// <summary>Validation finding <c>loop_criterion_unknown</c>.</summary>
    public const string LoopCriterionUnknown = "loop_criterion_unknown";

    /// <summary>Validation finding <c>loop_marker_missing</c>.</summary>
    public const string LoopMarkerMissing = "loop_marker_missing";

    /// <summary>Validation finding <c>loop_judge_criteria_missing</c>.</summary>
    public const string LoopJudgeCriteriaMissing = "loop_judge_criteria_missing";

    /// <summary>Validation finding <c>loop_judge_unconfigured</c>.</summary>
    public const string LoopJudgeUnconfigured = "loop_judge_unconfigured";

    /// <summary>Validation finding <c>loop_reviewer_missing</c>.</summary>
    public const string LoopReviewerMissing = "loop_reviewer_missing";

    /// <summary>Validation finding <c>loop_reviewer_self</c>.</summary>
    public const string LoopReviewerSelf = "loop_reviewer_self";

    /// <summary>Validation finding <c>loop_reviewer_version_invalid</c>.</summary>
    public const string LoopReviewerVersionInvalid = "loop_reviewer_version_invalid";

    /// <summary>Validation finding <c>loop_reviewer_not_found</c>.</summary>
    public const string LoopReviewerNotFound = "loop_reviewer_not_found";

    /// <summary>Validation finding <c>loop_reviewer_verdict_unknown</c>.</summary>
    public const string LoopReviewerVerdictUnknown = "loop_reviewer_verdict_unknown";

    /// <summary>Validation finding <c>sub_agent_tool_name_conflict</c>.</summary>
    public const string SubAgentToolNameConflict = "sub_agent_tool_name_conflict";

    /// <summary>Validation finding <c>callable_agent_duplicate</c>.</summary>
    public const string CallableAgentDuplicate = "callable_agent_duplicate";

    /// <summary>Validation finding <c>callable_agent_version_invalid</c>.</summary>
    public const string CallableAgentVersionInvalid = "callable_agent_version_invalid";

    /// <summary>Validation finding <c>callable_agent_version_not_found</c>.</summary>
    public const string CallableAgentVersionNotFound = "callable_agent_version_not_found";

    /// <summary>Validation finding <c>missing_parameter</c>.</summary>
    public const string MissingParameter = "missing_parameter";

    /// <summary>Validation finding <c>unknown_parameter</c>.</summary>
    public const string UnknownParameter = "unknown_parameter";

    /// <summary>Validation finding <c>value_too_long</c>.</summary>
    public const string ValueTooLong = "value_too_long";

    /// <summary>Validation finding <c>duplicate_hosted_tool_kind</c>.</summary>
    public const string DuplicateHostedToolKind = "duplicate_hosted_tool_kind";

    /// <summary>Validation finding <c>hosted_tool_conflict</c>.</summary>
    public const string HostedToolConflict = "hosted_tool_conflict";

    /// <summary>Validation finding <c>hosted_tool_not_callable</c>.</summary>
    public const string HostedToolNotCallable = "hosted_tool_not_callable";

    /// <summary>Validation finding <c>unknown_extension</c>.</summary>
    public const string UnknownExtension = "unknown_extension";

    /// <summary>Validation finding <c>duplicate_extension</c>.</summary>
    public const string DuplicateExtension = "duplicate_extension";

    /// <summary>Validation finding <c>cycle</c>.</summary>
    public const string Cycle = "cycle";

    /// <summary>Validation finding <c>unknown_agent</c>.</summary>
    public const string UnknownAgent = "unknown_agent";
}
