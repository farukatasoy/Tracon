namespace Tracon;

/// <summary>
/// Every machine-readable error code Tracon writes: the type of a failed run, the code of a job the
/// queue failed, and the <c>error.type</c> of an OpenAI-compatible error body.
/// </summary>
/// <remarks>
/// <para>
/// A code is a contract. Run records persist it, the HTTP API returns it as <c>errorType</c>, and a
/// consumer matches on it. A value never changes inside a major version: a new code is a minor change,
/// a removed, renamed or re-purposed code is a major one. The prose around a code may change.
/// </para>
/// <para>
/// The exception classes keep their own <c>*ErrorType</c> constants. Each is bound to the constant
/// here, so the string is written once. A test pins the list of values; a value that leaves the list
/// is a breaking change.
/// </para>
/// <para>
/// One kind of value is NOT in this class: the .NET type name of an unclassified exception
/// (<c>InvalidOperationException</c>, <c>TimeoutException</c>) that a run records as it is. That value
/// is not a code Tracon owns.
/// </para>
/// </remarks>
public static class TraconErrorTypes
{
    // --- Codes a TraconException carries (TraconException.ErrorType) ---

    /// <summary>The model provider filtered the content (<see cref="TraconContentFilteredException"/>).</summary>
    public const string ContentFiltered = "content_filtered";

    /// <summary>A content guard blocked the content (<see cref="TraconContentBlockedException"/>).</summary>
    public const string ContentBlocked = "content_blocked";

    /// <summary>An agent definition did not compile (<see cref="TraconCompilationException"/>).</summary>
    public const string CompilationFailed = "compilation_failed";

    /// <summary>The model provider could not be reached (<see cref="TraconProviderUnavailableException"/>).</summary>
    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>A tool call ran past its time limit (<see cref="TraconToolTimeoutException"/>).</summary>
    public const string ToolTimeout = "tool_timeout";

    /// <summary>A child run ran past its time limit (<see cref="TraconChildRunTimeoutException"/>).</summary>
    public const string ChildRunTimeout = "child_run_timeout";

    /// <summary>A workflow function node ran past its time limit (<see cref="TraconWorkflowFunctionTimeoutException"/>).</summary>
    public const string WorkflowFunctionTimeout = "workflow_function_timeout";

    /// <summary>A session was changed by another run at the same time (<see cref="TraconSessionConflictException"/>).</summary>
    public const string SessionConflict = "session_conflict";

    /// <summary>An outbound call was refused by the egress policy (<see cref="TraconExternalCallException"/>).</summary>
    public const string ExternalCallRejected = "external_call_rejected";

    /// <summary>The model answer did not match the requested schema (<see cref="TraconStructuredResponseException"/>).</summary>
    public const string StructuredResponseInvalid = "structured_response_invalid";

    /// <summary>A loop ended without meeting its exit condition (<see cref="TraconLoopUnsatisfiedException"/>).</summary>
    public const string LoopUnsatisfied = "loop_unsatisfied";

    /// <summary>The model cut its answer off at an output limit (<see cref="TraconOutputTruncatedException"/>).</summary>
    public const string OutputTruncated = "output_truncated";

    /// <summary>A session needs an owner and the caller has no identity (<see cref="TraconSessionOwnerRequiredException"/>).</summary>
    public const string SessionOwnerRequired = "session_owner_required";

    /// <summary>The tenant may not use the provider (<see cref="TraconTenantProviderRefusedException"/>).</summary>
    public const string TenantProviderRefused = "tenant_provider_refused";

    /// <summary>A job handler asked for a retry with backoff (<see cref="JobRetryException"/>).</summary>
    public const string JobRetry = "job_retry";

    /// <summary>An agent source failed (<see cref="TraconAgentSourceException"/>).</summary>
    public const string AgentSourceFailed = "agent_source_failed";

    /// <summary>An agent source broke its contract (<see cref="TraconAgentSourceException"/>).</summary>
    public const string AgentSourceContract = "agent_source_contract";

    /// <summary>Two eval runs cannot be compared (<see cref="EvalRunDiffUnavailableException"/>).</summary>
    public const string EvalRunDiffUnavailable = "eval_run_diff_unavailable";

    /// <summary>A tenant quota was used up (<see cref="TraconTenantQuotaExceededException"/>).</summary>
    public const string TenantQuotaExceeded = "tenant_quota_exceeded";

    /// <summary>A run used up its budget (<see cref="TraconRunBudgetExceededException"/>).</summary>
    public const string RunBudgetExceeded = "run_budget_exceeded";

    /// <summary>A Handoff workflow run made more handoffs than its limit allows (<see cref="WorkflowDefinition.MaxHandoffs"/>).</summary>
    public const string HandoffLimitExceeded = "handoff_limit_exceeded";

    /// <summary>A replay met a tool call the recording does not have.</summary>
    public const string ReplayToolMismatch = "replay_tool_mismatch";

    // --- Codes Tracon writes on a run it closed itself (RunError.Type) ---

    /// <summary>A run reached the time limit of the job that ran it and was cancelled with it.</summary>
    public const string JobMaxDurationExceeded = "job_max_duration_exceeded";

    /// <summary>A run without a session stopped at a tool approval that nobody can answer.</summary>
    public const string ApprovalUnanswerable = "approval_unanswerable";

    /// <summary>A workflow run was cut short by a process stop and recovery closed it.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>Reconciliation closed a run whose process stopped.</summary>
    public const string Orphaned = "orphaned";

    /// <summary>A workflow request nobody answered expired.</summary>
    public const string WorkflowRequestExpired = "WorkflowRequestExpired";

    /// <summary>A tool approval nobody decided expired.</summary>
    public const string ApprovalExpired = "ApprovalExpired";

    /// <summary>The model provider answered with a failure.</summary>
    public const string UpstreamError = "upstream_error";

    /// <summary>The tenant's credential cannot be used with this provider.</summary>
    public const string ProviderCredentialUnsupported = "provider_credential_unsupported";

    /// <summary>The workflow request already has an answer.</summary>
    public const string RequestAlreadyAnswered = "request_already_answered";

    /// <summary>An answer could not be recorded.</summary>
    public const string AnswerNotRecorded = "answer_not_recorded";

    // --- Codes a job the queue itself failed carries (see JobErrorCodes) ---

    /// <summary>No handler is registered for the job's handler key.</summary>
    public const string JobUnknownHandlerKey = "tracon.job.unknown-handler-key";

    /// <summary>The job's handler could not be constructed.</summary>
    public const string JobHandlerActivationFailed = "tracon.job.handler-activation-failed";

    /// <summary>The job was leased again past its attempt limit.</summary>
    public const string JobAttemptsExhausted = "tracon.job.attempts-exhausted";

    /// <summary>The attempt ran longer than the job's time limit.</summary>
    public const string JobMaxDurationExceededCode = "tracon.job.max-duration-exceeded";

    /// <summary>A queued turn ended but its session could not be saved. The run keeps the status the turn gave it.</summary>
    public const string JobSessionSaveFailed = "tracon.job.session-save-failed";

    /// <summary>A queued turn ended at an approval that could not be written, so the run closes as failed.</summary>
    public const string JobApprovalSaveFailed = "tracon.job.approval-save-failed";

    /// <summary>A queued workflow run's job payload cannot be run.</summary>
    public const string JobWorkflowRunPayloadInvalid = "tracon.job.workflow-run-payload-invalid";

    /// <summary>A queued workflow run was refused, failed or was cancelled. The run's own error says why.</summary>
    public const string JobWorkflowRunFailed = "tracon.job.workflow-run-failed";

    /// <summary>A queued workflow run was interrupted and its chain is at the continuation limit.</summary>
    public const string JobWorkflowRunNotContinued = "tracon.job.workflow-run-not-continued";

    // --- The error.type of an OpenAI-compatible error body ---

    /// <summary>The request is not valid. The default type of an OpenAI-compatible error.</summary>
    public const string InvalidRequestError = "invalid_request_error";

    /// <summary>The named resource does not exist.</summary>
    public const string NotFoundError = "not_found_error";

    /// <summary>The model name does not match an agent.</summary>
    public const string ModelNotFound = "model_not_found";

    /// <summary>The run attribution in the request is not valid.</summary>
    public const string InvalidRunAttribution = "invalid_run_attribution";

    /// <summary>The caller may not start this run.</summary>
    public const string RunNotAuthorized = "run_not_authorized";

    /// <summary>The server is draining and takes no new run.</summary>
    public const string ServiceUnavailable = "service_unavailable";

    /// <summary>The run stopped at a tool approval.</summary>
    public const string ApprovalRequired = "approval_required";
}
