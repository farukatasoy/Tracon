import { apiUrl } from './base';
import { authHeaders, setToken } from './auth';
import type {
  AgentDefinition,
  AgentDefinitionRequest,
  AgentDescriptor,
  AgentDetail,
  AgentSkillDefinition,
  AgentSkillRequest,
  AgentValidationReport,
  AgentVersionDiffResponse,
  AttachmentDescriptor,
  SkillScriptGrant,
  SkillScriptGrantRequest,
  AuditEntry,
  Conversation,
  CurrentTenant,
  DiagnosticsReport,
  EvalCase,
  EvalCaseInput,
  EvalCasePromotionRequest,
  EvalRun,
  EvalRunDetailResponse,
  EvalRunTriggerRequest,
  EvalSuite,
  EvalSuiteSaveRequest,
  Experiment,
  ExperimentResultsResponse,
  ExperimentSaveRequest,
  JobDetailResponse,
  JobKind,
  JobRecord,
  JobSchedule,
  JobScheduleSaveRequest,
  JobStatus,
  JobTriggerRequest,
  McpOAuthStartResponse,
  McpPromptContent,
  McpPromptSummary,
  McpResourceContent,
  McpResourceSummary,
  McpServerDefinition,
  McpServerRequest,
  Meta,
  ModelProviderDescriptor,
  ModelProviderHealth,
  RunCostRecalculationResult,
  RunEvent,
  RunFeedbackRequest,
  RunKind,
  RunComparisonResponse,
  RunInputResponse,
  RunRecord,
  RunReplayRequest,
  RunReplayResponse,
  RunScore,
  RunStatistics,
  RunStatus,
  RunTrace,
  SessionDetail,
  SessionBranchRequest,
  SessionBranchResult,
  SessionRecord,
  TenantDescriptor,
  TimeSeriesBucket,
  TimeSeriesPoint,
  ToolApprovalRule,
  ToolDescriptor,
  ToolInvocationRecord,
  ToolUsage,
  QuotaDefinition,
  QuotaSaveRequest,
  QuotaUsageResponse,
  RetentionPolicy,
  RetentionPolicySaveRequest,
  RetentionPreview,
  RetentionRun,
  RetentionRunTriggerResponse,
  WebhookDelivery,
  WebhookDeliveryStatus,
  WebhookSaveRequest,
  WebhookSubscription,
  WebhookTestResponse,
  WorkflowCheckpointRecord,
  WorkflowDefinition,
  WorkflowDescriptor,
  WorkflowGraph,
  WorkflowPendingRequest,
  WorkflowSaveRequest,
  SpeakResponse,
  VoiceDescriptor,
  VoiceHealth,
} from './types';

/**
 * A failed request, carrying the server's own explanation.
 *
 * The management API answers with `application/problem+json` (decision K-038),
 * so `title` and `detail` are written for a human and are shown verbatim.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string | null;

  constructor(status: number, title: string, detail: string | null) {
    super(detail ? `${title}: ${detail}` : title);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = detail;
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

async function toError(response: Response): Promise<ApiError> {
  let title = `HTTP ${response.status}`;
  let detail: string | null = null;

  try {
    const body = (await response.json()) as ProblemDetails;

    title = body.title ?? title;
    detail = body.detail ?? null;
  } catch {
    // A body that is not problem+json leaves the status line as the message.
  }

  return new ApiError(response.status, title, detail);
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(apiUrl(path), {
    ...init,
    headers: {
      Accept: 'application/json',
      ...authHeaders(),
      ...init?.headers,
    },
  });

  if (response.status === 401) {
    // The stored token was rejected. Dropping it returns the app to the token
    // prompt instead of retrying a credential that is known to be wrong.
    setToken(null);
  }

  if (!response.ok) {
    throw await toError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function send<T>(method: string, path: string, body: unknown): Promise<T> {
  return request<T>(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/** Opens a Server-Sent Events stream, carrying auth and abort support. */
export async function openStream(
  path: string,
  init?: RequestInit & { lastEventId?: string },
): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: 'text/event-stream',
    ...authHeaders(),
    ...(init?.headers as Record<string, string> | undefined),
  };

  if (init?.lastEventId !== undefined) {
    headers['Last-Event-ID'] = init.lastEventId;
  }

  const response = await fetch(apiUrl(path), { ...init, headers });

  if (!response.ok) {
    throw await toError(response);
  }

  return response;
}

function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }

  const text = search.toString();

  return text.length > 0 ? `?${text}` : '';
}

export const api = {
  meta: () => request<Meta>('api/meta'),
  diagnostics: () => request<DiagnosticsReport>('api/diagnostics'),

  agents: () => request<AgentDescriptor[]>('api/agents'),
  agent: (name: string) => request<AgentDetail>(`api/agents/${encodeURIComponent(name)}`),
  createAgent: (body: AgentDefinitionRequest) => send<AgentDefinition>('POST', 'api/agents', body),
  updateAgent: (name: string, body: AgentDefinitionRequest) =>
    send<AgentDefinition>('PUT', `api/agents/${encodeURIComponent(name)}`, body),
  validateAgent: (body: AgentDefinitionRequest) =>
    send<AgentValidationReport>('POST', 'api/agents/validate', body),
  deleteAgent: (name: string) =>
    request<void>(`api/agents/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  agentVersions: (name: string) =>
    request<AgentDefinition[]>(`api/agents/${encodeURIComponent(name)}/versions`),
  rollbackAgent: (name: string, version: number) =>
    send<AgentDefinition>('POST', `api/agents/${encodeURIComponent(name)}/rollback`, { version }),
  agentVersionDiff: (name: string, a: number, b: number) =>
    request<AgentVersionDiffResponse>(
      `api/agents/${encodeURIComponent(name)}/versions/${a}/diff/${b}`,
    ),

  skills: () => request<AgentSkillDefinition[]>('api/skills'),
  skill: (name: string) => request<AgentSkillDefinition>(`api/skills/${encodeURIComponent(name)}`),
  saveSkill: (name: string, body: AgentSkillRequest) =>
    send<AgentSkillDefinition>('PUT', `api/skills/${encodeURIComponent(name)}`, body),
  deleteSkill: (name: string) =>
    request<void>(`api/skills/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  skillScriptGrants: () => request<SkillScriptGrant[]>('api/skill-script-grants'),
  grantSkillScript: (body: SkillScriptGrantRequest) =>
    send<SkillScriptGrant>('POST', 'api/skill-script-grants', body),
  revokeSkillScript: (skillName: string, scriptName?: string | null) =>
    request<void>(
      `api/skill-script-grants/${encodeURIComponent(skillName)}` +
        (scriptName ? `?scriptName=${encodeURIComponent(scriptName)}` : ''),
      { method: 'DELETE' },
    ),

  sessions: (params: { agentName?: string; skip?: number; take?: number } = {}) =>
    request<SessionRecord[]>(`api/sessions${query(params)}`),
  session: (id: string) => request<SessionDetail>(`api/sessions/${encodeURIComponent(id)}`),
  deleteSession: (id: string) =>
    request<void>(`api/sessions/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  runs: (
    params: {
      agentName?: string;
      status?: RunStatus;
      sessionId?: string;
      startedAfter?: string;
      /** Include child runs. The server returns only root runs by default. */
      includeChildren?: boolean;
      parentRunId?: string;
      rootRunId?: string;
      skip?: number;
      take?: number;
    } = {},
  ) => request<RunRecord[]>(`api/runs${query(params)}`),
  run: (id: string) => request<RunRecord>(`api/runs/${encodeURIComponent(id)}`),

  /**
   * Every run in the tree this run belongs to, rooted at the top.
   *
   * Asking from a child returns the whole tree, not the subtree: you cannot tell
   * where you are in a tree without seeing the sibling branches.
   */
  runTree: (id: string) => request<RunRecord[]>(`api/runs/${encodeURIComponent(id)}/tree`),

  /**
   * Span tree for a run.
   *
   * Spans are sampled: successful runs are persisted at a configurable ratio,
   * so a 404 here is a normal outcome, not a failure.
   */
  runTrace: (id: string) => request<RunTrace>(`api/runs/${encodeURIComponent(id)}/trace`),
  runToolInvocations: (id: string) =>
    request<ToolInvocationRecord[]>(`api/runs/${encodeURIComponent(id)}/tools`),
  /**
   * Requests cancellation of a running run. A 202 only means cancellation was
   * requested — the final status is read back from `run()`/the event stream.
   */
  cancelRun: (id: string) =>
    request<RunRecord>(`api/runs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),
  runFeedback: (id: string) =>
    request<RunScore[]>(`api/runs/${encodeURIComponent(id)}/feedback`),
  saveRunFeedback: (id: string, body: RunFeedbackRequest) =>
    send<RunScore>('POST', `api/runs/${encodeURIComponent(id)}/feedback`, body),
  deleteRunFeedback: (id: string, scoreId: string) =>
    request<void>(
      `api/runs/${encodeURIComponent(id)}/feedback/${encodeURIComponent(scoreId)}`,
      { method: 'DELETE' },
    ),

  /**
   * A run's recorded input. A 404 means the run cannot be replayed — input
   * recording was off when it started, or retention removed the row.
   */
  runInput: (id: string) => request<RunInputResponse>(`api/runs/${encodeURIComponent(id)}/input`),

  /**
   * Replays a run with its recorded input under changed conditions.
   *
   * `ReplayTools` (the default) runs no tool bodies at all: recorded results are
   * played back. A call with no recorded result stops the replay with a 422 —
   * that is a finding, not a failure: the new version calls a different tool.
   */
  replayRun: (id: string, body: RunReplayRequest) =>
    send<RunReplayResponse>('POST', `api/runs/${encodeURIComponent(id)}/replay`, body),

  compareRuns: (a: string, b: string) =>
    request<RunComparisonResponse>(
      `api/runs/${encodeURIComponent(a)}/compare/${encodeURIComponent(b)}`,
    ),

  /**
   * Branches a session's conversation at a point and opens a new session on it.
   *
   * Items are copied, not chained: the read path is untouched. A 501 means this
   * deployment has no SQL provider, where history lives inside the opaque
   * session state and cannot be copied up to a sequence.
   */
  branchSession: (id: string, body: SessionBranchRequest) =>
    send<SessionBranchResult>('POST', `api/sessions/${encodeURIComponent(id)}/branch`, body),
  toolUsage: (params: { startedAfter?: string; maxTools?: number } = {}) =>
    request<ToolUsage[]>(`api/tools/usage${query(params)}`),

  tools: () => request<ToolDescriptor[]>('api/tools'),
  models: () => request<ModelProviderDescriptor[]>('api/models'),
  modelsHealth: (refresh = false) =>
    request<ModelProviderHealth[]>(`api/models/health${query({ refresh: refresh ? 'true' : undefined })}`),
  modelHealth: (name: string, refresh = false) =>
    request<ModelProviderHealth>(
      `api/models/health/${encodeURIComponent(name)}${query({ refresh: refresh ? 'true' : undefined })}`,
    ),
  stats: (params: { agentName?: string; startedAfter?: string; maxAgents?: number } = {}) =>
    request<RunStatistics>(`api/stats${query(params)}`),
  timeseries: (
    params: {
      from?: string;
      to?: string;
      bucket?: TimeSeriesBucket;
      agentName?: string;
      modelId?: string;
      kind?: RunKind;
    } = {},
  ) => request<TimeSeriesPoint[]>(`api/stats/timeseries${query(params)}`),
  recalculateCosts: () => send<RunCostRecalculationResult>('POST', 'api/stats/recalculate-costs', {}),

  currentTenant: () => request<CurrentTenant>('api/tenants/current'),
  tenants: () => request<TenantDescriptor[]>('api/tenants'),
  saveTenant: (slug: string, displayName: string) =>
    send<TenantDescriptor>('PUT', `api/tenants/${encodeURIComponent(slug)}`, { displayName }),
  deleteTenant: (slug: string) =>
    request<void>(`api/tenants/${encodeURIComponent(slug)}`, { method: 'DELETE' }),

  mcpServers: () => request<McpServerDefinition[]>('api/mcp-servers'),
  saveMcpServer: (name: string, body: McpServerRequest) =>
    send<McpServerDefinition>('PUT', `api/mcp-servers/${encodeURIComponent(name)}`, body),
  deleteMcpServer: (name: string) =>
    request<void>(`api/mcp-servers/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  refreshMcpTools: () => send<{ toolCount: number }>('POST', 'api/mcp-servers/refresh', {}),
  mcpPrompts: (name: string) =>
    request<McpPromptSummary[]>(`api/mcp-servers/${encodeURIComponent(name)}/prompts`),
  mcpPromptContent: (name: string, prompt: string, args: Record<string, string>) =>
    send<McpPromptContent>(
      'POST',
      `api/mcp-servers/${encodeURIComponent(name)}/prompts/${encodeURIComponent(prompt)}`,
      { arguments: args },
    ),
  mcpResources: (name: string) =>
    request<McpResourceSummary[]>(`api/mcp-servers/${encodeURIComponent(name)}/resources`),
  mcpResourceContent: (name: string, uri: string) =>
    request<McpResourceContent>(
      `api/mcp-servers/${encodeURIComponent(name)}/resources/read?uri=${encodeURIComponent(uri)}`,
    ),
  startMcpOAuth: (name: string) =>
    send<McpOAuthStartResponse>('POST', `api/mcp-servers/${encodeURIComponent(name)}/oauth/start`, {}),

  approvalRules: () => request<ToolApprovalRule[]>('api/approvals/rules'),
  deleteApprovalRule: (id: string) =>
    request<void>(`api/approvals/rules/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  /**
   * Uploads a file, returning its stored descriptor.
   *
   * The body is `multipart/form-data`, not JSON, so this bypasses `send()` and
   * lets the browser set its own `Content-Type` (with the multipart boundary).
   * Type is validated server-side from the file's magic bytes, never trusted
   * from what the browser reports.
   */
  uploadAttachment: async (file: File, sessionId?: string | null): Promise<AttachmentDescriptor> => {
    const form = new FormData();
    form.append('file', file, file.name);

    return request<AttachmentDescriptor>(
      `api/attachments${query({ sessionId: sessionId ?? undefined })}`,
      { method: 'POST', body: form },
    );
  },
  deleteAttachment: (id: string) =>
    request<void>(`api/attachments/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  /**
   * Fetches an attachment's raw bytes as a `Blob`.
   *
   * A plain `<img src="...">` cannot carry the bearer token, so a preview
   * thumbnail must fetch through here and wrap the result in an object URL
   * (`URL.createObjectURL`) instead of pointing at the endpoint directly.
   */
  attachmentBlob: async (id: string): Promise<Blob> => {
    const response = await fetch(apiUrl(`api/attachments/${encodeURIComponent(id)}`), {
      headers: { ...authHeaders() },
    });

    if (!response.ok) {
      throw await toError(response);
    }

    return response.blob();
  },

  /**
   * Reserves a conversation identifier.
   *
   * A conversation and a session are the same identity space (decision K-043),
   * so this is how the playground obtains a session id without inventing its
   * own format.
   */
  createConversation: () => send<Conversation>('POST', 'v1/conversations', {}),

  /**
   * Synthesises speech and stores it as an attachment.
   *
   * This is an operator action and runs OUTSIDE an agent run, so it writes no
   * `tool_invocations` row; the measured characters and cost come back in the
   * response instead. The agent's `speak` tool is the recorded path.
   */
  speak: (text: string, sessionId: string | null, voiceId?: string) =>
    send<SpeakResponse>('POST', 'api/voice/speak', { text, sessionId, voiceId }),

  voices: () => request<VoiceDescriptor[]>('api/voice/voices'),
  voiceHealth: () => request<VoiceHealth>('api/voice/health'),

  workflows: () => request<WorkflowDescriptor[]>('api/workflows'),
  workflow: (name: string) => request<WorkflowDefinition>(`api/workflows/${encodeURIComponent(name)}`),
  saveWorkflow: (name: string, body: WorkflowSaveRequest) =>
    send<WorkflowDefinition>('PUT', `api/workflows/${encodeURIComponent(name)}`, body),
  deleteWorkflow: (name: string) =>
    request<void>(`api/workflows/${encodeURIComponent(name)}`, { method: 'DELETE' }),

  /**
   * The compiled graph.
   *
   * Compiled, not derived from the definition: the ready-made patterns add
   * nodes the user never wrote (`OutputMessages`, `Batcher/*`, `GroupChatHost`)
   * and run events name exactly those. A graph drawn from the definition alone
   * would never light up.
   */
  workflowGraph: (name: string) =>
    request<WorkflowGraph>(`api/workflows/${encodeURIComponent(name)}/graph`),

  workflowCheckpoints: (runId: string) =>
    request<WorkflowCheckpointRecord[]>(
      `api/workflows/runs/${encodeURIComponent(runId)}/checkpoints`,
    ),

  /** Requests a run is blocked on. Empty unless the run is `AwaitingInput`. */
  workflowRequests: (runId: string) =>
    request<WorkflowPendingRequest[]>(`api/workflows/runs/${encodeURIComponent(runId)}/requests`),

  schedules: () => request<JobSchedule[]>('api/schedules'),
  schedule: (name: string) => request<JobSchedule>(`api/schedules/${encodeURIComponent(name)}`),
  saveSchedule: (name: string, body: JobScheduleSaveRequest) =>
    send<JobSchedule>('PUT', `api/schedules/${encodeURIComponent(name)}`, body),
  deleteSchedule: (name: string) =>
    request<void>(`api/schedules/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  triggerSchedule: (name: string, body: JobTriggerRequest = {}) =>
    send<JobRecord>('POST', `api/schedules/${encodeURIComponent(name)}/trigger`, body),

  jobs: (
    params: {
      kind?: JobKind;
      status?: JobStatus;
      scheduleId?: string;
      skip?: number;
      take?: number;
    } = {},
  ) => request<JobRecord[]>(`api/jobs${query(params)}`),
  job: (id: string) => request<JobDetailResponse>(`api/jobs/${encodeURIComponent(id)}`),
  cancelJob: (id: string) => request<void>(`api/jobs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),

  evalSuites: () => request<EvalSuite[]>('api/evals'),
  evalSuite: (name: string) => request<EvalSuite>(`api/evals/${encodeURIComponent(name)}`),
  saveEvalSuite: (name: string, body: EvalSuiteSaveRequest) =>
    send<EvalSuite>('PUT', `api/evals/${encodeURIComponent(name)}`, body),
  deleteEvalSuite: (name: string) =>
    request<void>(`api/evals/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  evalCases: (name: string) => request<EvalCase[]>(`api/evals/${encodeURIComponent(name)}/cases`),
  saveEvalCases: (name: string, cases: EvalCaseInput[]) =>
    send<EvalCase[]>('PUT', `api/evals/${encodeURIComponent(name)}/cases`, cases),
  promoteRunToEvalCase: (name: string, runId: string, body: EvalCasePromotionRequest = {}) =>
    send<EvalCase>('POST', `api/evals/${encodeURIComponent(name)}/cases/from-run/${encodeURIComponent(runId)}`, body),
  triggerEvalRun: (name: string, body: EvalRunTriggerRequest = {}) =>
    send<EvalRun>('POST', `api/evals/${encodeURIComponent(name)}/run`, body),
  evalRuns: (name: string, params: { skip?: number; take?: number } = {}) =>
    request<EvalRun[]>(`api/evals/${encodeURIComponent(name)}/runs${query(params)}`),
  evalRun: (id: string) => request<EvalRunDetailResponse>(`api/evals/runs/${encodeURIComponent(id)}`),

  experiments: () => request<Experiment[]>('api/experiments'),
  experiment: (name: string) => request<Experiment>(`api/experiments/${encodeURIComponent(name)}`),
  saveExperiment: (name: string, body: ExperimentSaveRequest) =>
    send<Experiment>('PUT', `api/experiments/${encodeURIComponent(name)}`, body),
  deleteExperiment: (name: string) =>
    request<void>(`api/experiments/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  startExperiment: (name: string) =>
    send<Experiment>('POST', `api/experiments/${encodeURIComponent(name)}/start`, {}),
  stopExperiment: (name: string) =>
    send<Experiment>('POST', `api/experiments/${encodeURIComponent(name)}/stop`, {}),
  experimentResults: (name: string) =>
    request<ExperimentResultsResponse>(`api/experiments/${encodeURIComponent(name)}/results`),

  quotas: () => request<QuotaDefinition[]>('api/quotas'),
  saveQuota: (body: QuotaSaveRequest) => send<QuotaDefinition>('PUT', 'api/quotas', body),
  deleteQuota: (id: string) =>
    request<void>(`api/quotas/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  quotaUsage: (params: { agentName?: string; period?: string } = {}) =>
    request<QuotaUsageResponse>(`api/quotas/usage${query(params)}`),

  webhooks: () => request<WebhookSubscription[]>('api/webhooks'),
  webhook: (name: string) =>
    request<WebhookSubscription>(`api/webhooks/${encodeURIComponent(name)}`),
  saveWebhook: (name: string, body: WebhookSaveRequest) =>
    send<WebhookSubscription>('PUT', `api/webhooks/${encodeURIComponent(name)}`, body),
  deleteWebhook: (name: string) =>
    request<void>(`api/webhooks/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  testWebhook: (name: string) =>
    send<WebhookTestResponse>('POST', `api/webhooks/${encodeURIComponent(name)}/test`, {}),
  webhookDeliveries: (
    name: string,
    params: { status?: WebhookDeliveryStatus; skip?: number; take?: number } = {},
  ) =>
    request<WebhookDelivery[]>(
      `api/webhooks/${encodeURIComponent(name)}/deliveries${query(params)}`,
    ),

  retentionPolicies: () => request<RetentionPolicy[]>('api/retention'),
  saveRetentionPolicy: (target: string, body: RetentionPolicySaveRequest) =>
    send<RetentionPolicy>('PUT', `api/retention/${encodeURIComponent(target)}`, body),
  deleteRetentionPolicy: (target: string) =>
    request<void>(`api/retention/${encodeURIComponent(target)}`, { method: 'DELETE' }),
  retentionPreview: (target?: string) =>
    request<RetentionPreview[]>(`api/retention/preview${query({ target })}`),
  runRetention: (target?: string) =>
    send<RetentionRunTriggerResponse>('POST', `api/retention/run${query({ target })}`, {}),
  retentionHistory: (params: { target?: string; skip?: number; take?: number } = {}) =>
    request<RetentionRun[]>(`api/retention/history${query(params)}`),

  audit: (
    params: {
      actor?: string;
      action?: string;
      entity?: string;
      after?: string;
      before?: string;
      limit?: number;
    } = {},
  ) => request<AuditEntry[]>(`api/audit${query(params)}`),
  entityAudit: (entity: string, limit?: number) =>
    request<AuditEntry[]>(`api/audit/${encodeURIComponent(entity)}${query({ limit })}`),
};

export type { RunEvent };
