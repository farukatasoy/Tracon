import { apiUrl } from './base';
import { authHeaders, setToken } from './auth';
import type {
  AgentDefinition,
  AgentDefinitionRequest,
  AgentDescriptor,
  AgentDetail,
  Conversation,
  Meta,
  ModelProviderDescriptor,
  RunEvent,
  RunRecord,
  RunStatistics,
  RunStatus,
  SessionDetail,
  SessionRecord,
  ToolDescriptor,
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

  tools: () => request<ToolDescriptor[]>('api/tools'),
  models: () => request<ModelProviderDescriptor[]>('api/models'),
  stats: (params: { agentName?: string; startedAfter?: string; maxAgents?: number } = {}) =>
    request<RunStatistics>(`api/stats${query(params)}`),

  /**
   * Reserves a conversation identifier.
   *
   * A conversation and a session are the same identity space (decision K-043),
   * so this is how the playground obtains a session id without inventing its
   * own format.
   */
  createConversation: () => send<Conversation>('POST', 'v1/conversations', {}),
};

export type { RunEvent };
