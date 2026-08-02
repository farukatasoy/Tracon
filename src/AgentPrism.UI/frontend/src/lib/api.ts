import { apiUrl } from './base';
import { authHeaders, setToken } from './auth';
import type {
  AgentDefinition,
  AgentDefinitionRequest,
  AgentDescriptor,
  AgentDetail,
  AgentSkillDefinition,
  AgentSkillRequest,
  AuditEntry,
  Conversation,
  CurrentTenant,
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

function query(params: Record<string, string | number | undefined | null>): string {
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
      skip?: number;
      take?: number;
    } = {},
  ) => request<RunRecord[]>(`api/runs${query(params)}`),
  run: (id: string) => request<RunRecord>(`api/runs/${encodeURIComponent(id)}`),

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
   * Reserves a conversation identifier.
   *
   * A conversation and a session are the same identity space (decision K-043),
   * so this is how the playground obtains a session id without inventing its
   * own format.
   */
  createConversation: () => send<Conversation>('POST', 'v1/conversations', {}),

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
