import { apiUrl } from './base';
import { authHeaders, setToken } from './auth';
import type {
  AgentDefinition,
  AgentDefinitionRequest,
  AgentDescriptor,
  AgentDetail,
  AgentSkillDefinition,
  AgentSkillRequest,
  AttachmentDescriptor,
  SkillScriptGrant,
  SkillScriptGrantRequest,
  AuditEntry,
  Conversation,
  CurrentTenant,
  EvalCase,
  EvalCaseInput,
  EvalRun,
  EvalRunDetailResponse,
  EvalRunTriggerRequest,
  EvalSuite,
  EvalSuiteSaveRequest,
  JobDetailResponse,
  JobKind,
  JobRecord,
  JobSchedule,
  JobScheduleSaveRequest,
  JobStatus,
  JobTriggerRequest,
  McpServerDefinition,
  McpServerRequest,
  Meta,
  ModelProviderDescriptor,
  ModelProviderHealth,
  RunEvent,
  RunRecord,
  RunStatistics,
  RunStatus,
  RunTrace,
  SessionDetail,
  SessionRecord,
  TenantDescriptor,
  ToolApprovalRule,
  ToolDescriptor,
  ToolInvocationRecord,
  ToolUsage,
  WorkflowCheckpointRecord,
  WorkflowDefinition,
  WorkflowDescriptor,
  WorkflowGraph,
  WorkflowPendingRequest,
  WorkflowSaveRequest,
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

  agents: () => request<AgentDescriptor[]>('api/agents'),
  agent: (name: string) => request<AgentDetail>(`api/agents/${encodeURIComponent(name)}`),
  createAgent: (body: AgentDefinitionRequest) => send<AgentDefinition>('POST', 'api/agents', body),
  updateAgent: (name: string, body: AgentDefinitionRequest) =>
    send<AgentDefinition>('PUT', `api/agents/${encodeURIComponent(name)}`, body),
  deleteAgent: (name: string) =>
    request<void>(`api/agents/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  agentVersions: (name: string) =>
    request<AgentDefinition[]>(`api/agents/${encodeURIComponent(name)}/versions`),
  rollbackAgent: (name: string, version: number) =>
    send<AgentDefinition>('POST', `api/agents/${encodeURIComponent(name)}/rollback`, { version }),

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
  triggerEvalRun: (name: string, body: EvalRunTriggerRequest = {}) =>
    send<EvalRun>('POST', `api/evals/${encodeURIComponent(name)}/run`, body),
  evalRuns: (name: string, params: { skip?: number; take?: number } = {}) =>
    request<EvalRun[]>(`api/evals/${encodeURIComponent(name)}/runs${query(params)}`),
  evalRun: (id: string) => request<EvalRunDetailResponse>(`api/evals/runs/${encodeURIComponent(id)}`),

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
