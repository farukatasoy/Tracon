namespace Tracon;

/// <summary>The rows of <see cref="ClientTextLimits"/>: one for each narrow text column of the SQL Server schema.</summary>
/// <remarks>
/// The rows are in the order of the columns in the schema (table, then column). A row for a column a client fills names
/// every input that carries the text; the other rows say why no input needs a limit of its own.
/// </remarks>
internal static class ClientTextColumns
{
    private const int Id = ClientTextLimits.Identifier;
    private const int Setting = ClientTextLimits.Setting;
    private const int Address = ClientTextLimits.Address;

    private const string SessionRule = "A session identifier is limited to 200 characters at every entry that carries it (SessionIdLimits).";
    private const string TenantRule = "A tenant identifier is limited to its alphabet and 64 characters where the tenant is resolved.";
    private const string PrincipalRule = "The name of the authenticated caller, read from the identity and not from a request field.";
    private const string ProviderToolRule = "Chosen by a model provider or a tool while a run goes on, not by a client of the API.";

    /// <summary>Gets every row.</summary>
    internal static IReadOnlyList<ClientTextColumn> All { get; } =
    [
        Elsewhere("*.tenant_id", TenantRule),

        Follows("agent_definition_version_counters.name", "agent_definitions.name"),
        Principal("agent_definition_versions.created_by"),
        Client(
            "agent_definitions.name",
            Id,
            route: ["name"],
            body: [Prop<AgentDefinitionRequest>(nameof(AgentDefinitionRequest.Name))]),
        Follows("agent_files.agent_name", "agent_definitions.name"),
        Elsewhere("agent_files.path", "A path in a session workspace is limited to the width of the column (SessionWorkspacePaths.MaxPathLength) and a longer one is refused to the tool that wrote it."),
        Client(
            "agent_skill_resources.media_type",
            Id,
            body: [Prop<AgentSkillResourceDefinition>(nameof(AgentSkillResourceDefinition.MediaType))]),
        Client(
            "agent_skill_resources.name",
            Id,
            body: [Prop<AgentSkillResourceDefinition>(nameof(AgentSkillResourceDefinition.Name))]),
        Client(
            "agent_skill_scripts.extension",
            Id,
            body: [Prop<AgentSkillScriptDefinition>(nameof(AgentSkillScriptDefinition.Extension))]),
        Client(
            "agent_skill_scripts.name",
            Id,
            body: [Prop<AgentSkillScriptDefinition>(nameof(AgentSkillScriptDefinition.Name))]),
        Client(
            "agent_skills.name",
            Id,
            route: ["name"],
            body: [Prop<AgentSkillRequest>(nameof(AgentSkillRequest.Name))]),
        Generated("api_keys.key_prefix", "The first characters of a key Tracon generated."),
        Client("api_keys.name", Id, body: [Prop<ApiKeyCreateRequest>(nameof(ApiKeyCreateRequest.Name))]),
        Principal("attachments.created_by"),
        Generated("attachments.external_uri", "The address the blob store returned for the bytes."),
        Client("attachments.file_name", Setting, form: ["file"]),
        Generated("attachments.media_type", "The type Tracon detected from the bytes, one of a fixed list."),
        Elsewhere("attachments.session_id", SessionRule),
        Generated("attachments.sha256", "A hash of the bytes."),
        Elsewhere("audit_log.action", "The audit trail shortens a label to the column's width before it writes it (AuditLabelLimit)."),
        Elsewhere("audit_log.actor", "The audit trail shortens a label to the column's width before it writes it (AuditLabelLimit)."),
        Elsewhere("audit_log.entity", "The audit trail shortens a label to the column's width before it writes it (AuditLabelLimit)."),
        Generated("audit_log.hash", "A hash of the row."),
        Operator("audit_log.key_id", "The identifier of a chain key the operator configured; the key ring accepts at most 64 characters."),
        Generated("audit_log.prev_hash", "A hash of the row before."),
        Follows("conversations.agent_name", "agent_definitions.name"),
        Follows("eval_runs.model_id", "runs.model_id"),
        Client(
            "eval_suites.agent_name",
            Id,
            body: [Prop<EvalSuiteSaveRequest>(nameof(EvalSuiteSaveRequest.AgentName))]),
        Client("eval_suites.name", Id, route: ["name"]),
        Client(
            "experiments.agent_name",
            Id,
            body: [Prop<ExperimentSaveRequest>(nameof(ExperimentSaveRequest.AgentName))]),
        Generated("experiments.assignment_key", "Reserved: no request carries it and the runtime assigns by the session identifier."),
        Client("experiments.name", Id, route: ["name"]),
        Generated("idempotency_keys.content_type", "The content type of the response Tracon stored for replay."),
        Generated("idempotency_keys.fingerprint", "A hash of the request."),
        Elsewhere("idempotency_keys.key", "The key is limited to 200 characters (Idempotency:MaxKeyLength) on every store."),
        Client("inbound_triggers.name", Id, route: ["name"]),
        Client(
            "inbound_triggers.payload_path",
            Setting,
            body: [Prop<InboundTriggerSaveRequest>(nameof(InboundTriggerSaveRequest.PayloadPath))]),
        Client(
            "inbound_triggers.signing_secret_configuration_name",
            Setting,
            body: [Prop<InboundTriggerSaveRequest>(nameof(InboundTriggerSaveRequest.SigningSecretConfigurationName))]),
        Client(
            "inbound_triggers.target_name",
            Id,
            body: [Prop<InboundTriggerSaveRequest>(nameof(InboundTriggerSaveRequest.TargetName))]),
        Principal("job_schedules.created_by"),
        Client(
            "job_schedules.cron",
            Id,
            body: [Prop<JobScheduleSaveRequest>(nameof(JobScheduleSaveRequest.Cron))]),
        Elsewhere("job_schedules.handler_key", "A handler key is a lower-case name of at most 128 characters (JobHandlerKeys)."),
        Elsewhere("job_schedules.lane", "A lane is a lower-case name of at most 64 characters (JobLanes)."),
        Client("job_schedules.name", Id, route: ["name"]),
        Client(
            "job_schedules.target_name",
            Id,
            body: [Prop<JobScheduleSaveRequest>(nameof(JobScheduleSaveRequest.TargetName))]),
        Client(
            "job_schedules.time_zone",
            Id,
            body: [Prop<JobScheduleSaveRequest>(nameof(JobScheduleSaveRequest.TimeZone))]),
        Elsewhere("jobs.handler_key", "A handler key is a lower-case name of at most 128 characters (JobHandlerKeys)."),
        Elsewhere("jobs.lane", "A lane is a lower-case name of at most 64 characters (JobLanes)."),
        Generated("jobs.lease_owner", "The identity Tracon gives a worker instance."),
        Follows("jobs.target_name", "job_schedules.target_name"),
        Client(
            "mcp_servers.endpoint",
            Address,
            body: [Prop<McpServerRequest>(nameof(McpServerRequest.Endpoint))]),
        Client(
            "mcp_servers.name",
            Id,
            route: ["name"],
            body: [Prop<ExportedMcpServer>(nameof(ExportedMcpServer.Name))]),
        Client(
            "mcp_servers.oauth_client_id",
            Id,
            body: [Prop<McpServerRequest>(nameof(McpServerRequest.OAuthClientId))]),
        Client(
            "mcp_servers.oauth_client_secret_configuration_key",
            Id,
            body: [Prop<McpServerRequest>(nameof(McpServerRequest.OAuthClientSecretConfigurationKey))]),
        Principal("pending_approvals.decided_by"),
        External("pending_approvals.request_id", ProviderToolRule),
        Elsewhere("pending_approvals.session_id", SessionRule),
        External("pending_approvals.tool_name", ProviderToolRule),
        Follows("quota_usage.agent_name", "quotas.agent_name"),
        Client(
            "quotas.agent_name",
            Id,
            body: [Prop<QuotaSaveRequest>(nameof(QuotaSaveRequest.AgentName))]),
        Generated("rate_limit_counters.partition_key", "A fixed group, a tenant identifier and a word: well inside the column."),
        Elsewhere("responses.session_id", SessionRule),
        Client("retention_policies.target", Id, route: ["target"]),
        Follows("retention_runs.target", "retention_policies.target"),
        Elsewhere("run_events.custom_type", "A custom event type is a lower-case name of at most 128 characters (RunEventCustomTypes)."),
        External("run_events.tool_call_id", ProviderToolRule),
        External("run_events.tool_name", ProviderToolRule),
        Operator("run_non_token_usage.currency", "A currency code the operator's price table names."),
        Follows("run_non_token_usage.model", "runs.model_id"),
        Operator("run_non_token_usage.node_name", "The name of a function node, registered in code; a workflow definition only references it and a reference to an unknown function is refused when the definition is saved."),
        Operator("run_non_token_usage.provider", "A provider name the operator registered."),
        Operator("run_non_token_usage.unit", "A unit the operator's price table names."),
        Principal("run_scores.author"),
        Elsewhere("run_scores.evaluator_version", "A score's evaluator version is limited to 128 characters (RunScoreRules)."),
        Client(
            "run_scores.message_id",
            Id,
            body: [Prop<RunFeedbackRequest>(nameof(RunFeedbackRequest.MessageId))]),
        Elsewhere("run_scores.name", "A score's name is limited to 64 characters (RunScoreRules)."),
        Generated("run_scores.source", "One of a fixed list of words."),
        Elsewhere("run_scores.text_value", "A score's text is limited to 256 characters (RunScoreRules)."),
        Follows("runs.agent_name", "agent_definitions.name"),
        Operator("runs.cost_currency", "A currency code the operator's price table names."),
        Generated("runs.error_fingerprint", "A hash of the error."),
        Generated("runs.error_type", "The name of the exception type or a fixed class."),
        Client(
            "runs.model_id",
            Id,
            body: [Prop<ModelBinding>(nameof(ModelBinding.Model))]),
        Client(
            "runs.model_provider",
            Id,
            body: [Prop<ModelBinding>(nameof(ModelBinding.Provider))]),
        Operator("runs.non_token_currency", "A currency code the operator's price table names."),
        Elsewhere("runs.session_id", SessionRule),
        Elsewhere("runs.user_id", "The user identifier of a run is checked against the limits of the run labels (RunLabels) where the attribution is read."),
        Client(
            "runs.variant",
            Id,
            body: [Prop<ExperimentVariant>(nameof(ExperimentVariant.Name))]),
        Follows("runs.workflow_name", "workflows.name"),
        Follows("sessions.agent_name", "agent_definitions.name"),
        Elsewhere("sessions.id", SessionRule),
        Principal("sessions.owner_id"),
        Operator("singleton_leases.name", "A lease name the code of a background service chose."),
        Generated("singleton_leases.owner_id", "The identity Tracon gives an instance."),
        Generated("skill_script_grants.content_hash", "A hash of the script."),
        Principal("skill_script_grants.granted_by"),
        Client(
            "skill_script_grants.script_name",
            Id,
            body: [Prop<SkillScriptGrantRequest>(nameof(SkillScriptGrantRequest.ScriptName))]),
        Client(
            "skill_script_grants.skill_name",
            Id,
            body: [Prop<SkillScriptGrantRequest>(nameof(SkillScriptGrantRequest.SkillName))]),
        External("spans.name", ProviderToolRule),
        Generated("spans.span_id", "A trace span identifier."),
        Client(
            "tenant_provider_bindings.api_key_configuration_name",
            Setting,
            body: [Prop<TenantProviderBindingRequest>(nameof(TenantProviderBindingRequest.ApiKeyConfigurationName))]),
        Client(
            "tenant_provider_bindings.endpoint",
            Setting,
            body: [Prop<TenantProviderBindingRequest>(nameof(TenantProviderBindingRequest.Endpoint))]),
        Client("tenant_provider_bindings.provider_name", Id, route: ["provider"]),
        Client(
            "tenants.display_name",
            Id,
            body: [Prop<TenantRequest>(nameof(TenantRequest.DisplayName))]),
        Elsewhere("tenants.slug", TenantRule),
        Client(
            "tool_approval_rules.agent_name",
            Id,
            body: [Prop<ToolApprovalRuleRequest>(nameof(ToolApprovalRuleRequest.AgentName))]),
        Generated("tool_approval_rules.arguments_hash", "A hash of the arguments."),
        Generated("tool_approval_rules.conditions_hash", "A hash of the conditions."),
        Principal("tool_approval_rules.created_by"),
        Generated("tool_approval_rules.source_fingerprint", "A hash of the tool source."),
        Client(
            "tool_approval_rules.tool_name",
            Id,
            body: [Prop<ToolApprovalRuleRequest>(nameof(ToolApprovalRuleRequest.ToolName))]),
        Operator("tool_invocations.cost_currency", "A currency code the operator's price table names."),
        Generated("tool_invocations.source", "One of a fixed list of words that names the kind of tool source."),
        External("tool_invocations.tool_call_id", ProviderToolRule),
        External("tool_invocations.tool_name", ProviderToolRule),
        Operator("tool_invocations.usage_model", "A model name the tool's own code reported."),
        Operator("tool_invocations.usage_provider", "A provider name the tool's own code reported."),
        Operator("tool_invocations.usage_unit", "A unit the tool's own code reported."),
        Generated("traces.trace_id", "A trace identifier."),
        Follows("voice_sessions.agent_name", "agent_definitions.name"),
        Principal("voice_sessions.created_by"),
        Operator("voice_sessions.currency", "A currency code the operator's price table names."),
        Operator("voice_sessions.model", "A model name the operator configured for live voice."),
        Operator("voice_sessions.provider", "A provider name the operator registered."),
        Elsewhere("voice_sessions.session_id", SessionRule),
        Generated("webhook_deliveries.event_type", "One of the event names Tracon raises."),
        Client("webhook_subscriptions.name", Id, route: ["name"]),
        Client(
            "webhook_subscriptions.secret_configuration_key",
            Id,
            body: [Prop<WebhookSaveRequest>(nameof(WebhookSaveRequest.SecretConfigurationKey))]),
        Client(
            "webhook_subscriptions.url",
            Address,
            body: [Prop<WebhookSaveRequest>(nameof(WebhookSaveRequest.Url))]),
        External("workflow_checkpoints.checkpoint_id", "Chosen by the workflow engine."),
        External("workflow_checkpoints.parent_id", "Chosen by the workflow engine."),
        Elsewhere("workflow_checkpoints.session_id", "An execution session identifier is limited to 128 characters (WorkflowSessionId)."),
        Follows("workflow_definition_versions.name", "workflows.name"),
        External("workflow_requests.port_id", "Chosen by the workflow engine."),
        External("workflow_requests.request_id", "Chosen by the workflow engine."),
        Elsewhere("workflow_requests.session_id", "An execution session identifier is limited to 128 characters (WorkflowSessionId)."),
        Follows("workflow_requests.workflow_name", "workflows.name"),
        Generated("workflow_responses.claim_scope", "Derived by Tracon from the request it answers."),
        External("workflow_responses.request_id", "Names a request the workflow engine made; a response to an unknown one is refused before it is stored."),
        Principal("workflow_responses.responded_by"),
        Elsewhere("workflow_responses.session_id", "An execution session identifier is limited to 128 characters (WorkflowSessionId)."),
        Client(
            "workflows.name",
            Id,
            route: ["name"],
            body: [Prop<ExportedWorkflow>(nameof(ExportedWorkflow.Name))]),
    ];

    private static ClientTextColumn Client(
        string column,
        int limit,
        string[]? route = null,
        (Type Type, string Property)[]? body = null,
        string[]? form = null)
        => new(column, ColumnOrigin.Client, limit, string.Empty)
        {
            Route = route ?? [],
            Body = body ?? [],
            Form = form ?? [],
        };

    private static ClientTextColumn Follows(string column, string source)
        => new(column, ColumnOrigin.Follows, 0, source);

    private static ClientTextColumn Elsewhere(string column, string note)
        => new(column, ColumnOrigin.Elsewhere, 0, note);

    private static ClientTextColumn Generated(string column, string note)
        => new(column, ColumnOrigin.Generated, 0, note);

    private static ClientTextColumn Operator(string column, string note)
        => new(column, ColumnOrigin.Operator, 0, note);

    private static ClientTextColumn Principal(string column)
        => new(column, ColumnOrigin.Principal, 0, PrincipalRule);

    private static ClientTextColumn External(string column, string note)
        => new(column, ColumnOrigin.External, 0, note);

    private static (Type Type, string Property) Prop<T>(string property)
        => (typeof(T), property);
}
