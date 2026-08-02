/**
 * Wire contracts served by AgentPrism.AspNetCore.
 *
 * These mirror the .NET records one to one. Enums arrive as names, never as
 * numbers (decision K-040), so the string unions below are the whole contract.
 */

export type AgentOrigin = 'Code' | 'Maf' | 'Database';

export type RunStatus = 'Running' | 'Completed' | 'Failed' | 'Canceled';

export type RunEventType =
  | 'RunStarted'
  | 'MessageDelta'
  | 'MessageCompleted'
  | 'ToolInvoking'
  | 'ToolInvoked'
  | 'ToolFailed'
  | 'RunCompleted'
  | 'RunFailed';

export interface Meta {
  version: string;
  prefix: string;
  authentication: {
    allowRemoteAccess: boolean;
    requiresBearerToken: boolean;
    requiresAuthorizationPolicy: boolean;
  };
  storage: {
    persistent: boolean;
    agentDefinitionStore: string;
    runStore: string;
    sessionStore: string;
  };
}

export interface ModelBinding {
  provider: string;
  model: string;
  temperature?: number | null;
  maxOutputTokens?: number | null;
  topP?: number | null;
  reasoningEffort?: string | null;
}

export interface HarnessSettings {
  maxContextWindowTokens?: number | null;
  maxOutputTokens?: number | null;
  maximumIterationsPerRequest?: number | null;
  harnessInstructions?: string | null;
  disableCompaction?: boolean;
  disableTodoProvider?: boolean;
  disableFileMemory?: boolean;
  disableWebSearch?: boolean;
  disableToolAutoApproval?: boolean;
  disableAgentSkillsProvider?: boolean;
  disableAgentModeProvider?: boolean;
}

export interface AgentDescriptor {
  name: string;
  displayName?: string | null;
  description?: string | null;
  origin: AgentOrigin;
  sourceName: string;
  version: number;
  model?: ModelBinding | null;
  toolNames: string[];
  usesHarness: boolean;
  updatedAt?: string | null;
}

export interface AgentDefinition {
  name: string;
  displayName?: string | null;
  description?: string | null;
  instructions?: string | null;
  model: ModelBinding;
  toolNames: string[];
  harness?: HarnessSettings | null;
  origin: AgentOrigin;
  version: number;
  tenantId?: string | null;
  updatedAt?: string | null;
  metadata?: Record<string, unknown>;
}

export interface AgentDetail {
  descriptor: AgentDescriptor;
  definition: AgentDefinition | null;
  isEditable: boolean;
}

/** Body of POST/PUT `/api/agents`. Server-owned fields are deliberately absent. */
export interface AgentDefinitionRequest {
  name: string;
  displayName?: string | null;
  description?: string | null;
  instructions?: string | null;
  model: ModelBinding;
  toolNames: string[];
  harness?: HarnessSettings | null;
}

export interface ToolDescriptor {
  name: string;
  description?: string | null;
  jsonSchema?: string | null;
  requiresApproval: boolean;
  /** Server name when the tool comes from a remote MCP server; null when defined in code. */
  source?: string | null;
}

export interface ToolInvocationRecord {
  id: string;
  runId: string;
  toolName: string;
  toolCallId?: string | null;
  source?: string | null;
  arguments?: string | null;
  result?: string | null;
  /** .NET TimeSpan serialises as "hh:mm:ss.fffffff"; null when not measured. */
  duration?: string | null;
  error?: string | null;
  createdAt: string;
  succeeded: boolean;
}

export interface ToolUsage {
  toolName: string;
  totalCalls: number;
  failedCalls: number;
  averageDurationMs?: number | null;
  lastCalledAt?: string | null;
  errorRate?: number | null;
}

export interface ModelDescriptor {
  name: string;
  displayName?: string | null;
  contextWindowTokens?: number | null;
  maxOutputTokens?: number | null;
  supportsStreaming: boolean;
  supportsTools: boolean;
  supportsReasoning: boolean;
  inputCostPerMillionTokens?: number | null;
  outputCostPerMillionTokens?: number | null;
}

/**
 * A provider's connectivity status.
 *
 * `Unknown` means the provider does not implement the optional health-check
 * contract — not an error. Serialized as a name (decision K-040), matching
 * `ModelProviderHealthStatus` in `AgentPrism.Abstractions`.
 */
export type ModelProviderHealthStatus = 'Unknown' | 'Healthy' | 'Degraded' | 'Unhealthy';

export interface ModelProviderDescriptor {
  name: string;
  displayName?: string | null;
  models: ModelDescriptor[];
  /** Cached status; this endpoint never makes a network call for it. */
  status: ModelProviderHealthStatus;
}

/**
 * A provider's last health-check result.
 *
 * `detail` never carries an API key or the raw response body — only an HTTP
 * status code and a short reason, or a circuit-breaker note.
 */
export interface ModelProviderHealth {
  providerName: string;
  status: ModelProviderHealthStatus;
  detail?: string | null;
  /** A .NET `TimeSpan` wire string; parse with `latencyText`. */
  latency?: string | null;
  checkedAt: string;
  models: string[];
}

export interface SessionRecord {
  id: string;
  agentName: string;
  state: unknown;
  createdAt: string;
  updatedAt: string;
  tenantId?: string | null;
}

/**
 * `messages` is Microsoft Agent Framework's `ChatMessage[]`, not an AgentPrism
 * DTO (rule K3). Contents are polymorphic and carry a `$type` discriminator.
 * The field is null when the history could not be read — that is not an error.
 */
export interface SessionDetail {
  id: string;
  agentName: string;
  tenantId?: string | null;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessage[] | null;
  state: unknown;
}

export interface ChatMessage {
  role?: string;
  authorName?: string | null;
  contents?: ChatContent[];
  messageId?: string | null;
  createdAt?: string | null;
}

export interface ChatContent {
  $type?: string;
  text?: string;
  name?: string;
  callId?: string;
  arguments?: Record<string, unknown> | null;
  result?: unknown;
  exception?: unknown;
  details?: {
    inputTokenCount?: number | null;
    outputTokenCount?: number | null;
    totalTokenCount?: number | null;
  };
  [key: string]: unknown;
}

export interface RunUsage {
  inputTokens?: number | null;
  outputTokens?: number | null;
  totalTokens?: number | null;
}

export interface RunError {
  type: string;
  message: string;
}

export interface RunRecord {
  id: string;
  agentName: string;
  status: RunStatus;
  startedAt: string;
  completedAt?: string | null;
  tenantId?: string | null;
  sessionId?: string | null;
  modelId?: string | null;
  isStreaming: boolean;
  usage?: RunUsage | null;
  error?: RunError | null;
  eventCount: number;
}

export interface RunEvent {
  runId: string;
  sequence: number;
  type: RunEventType;
  timestamp: string;
  text?: string | null;
  toolName?: string | null;
  toolCallId?: string | null;
  payload?: string | null;
}

export interface RunAgentStatistics {
  agentName: string;
  totalRuns: number;
  failedRuns: number;
  totalTokens: number;
}

export interface RunModelStatistics {
  modelId: string;
  totalRuns: number;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
}

export interface RunStatistics {
  totalRuns: number;
  completedRuns: number;
  failedRuns: number;
  canceledRuns: number;
  runningRuns: number;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  byAgent: RunAgentStatistics[];
  byModel: RunModelStatistics[];
  errorRate?: number | null;
}

/* ------------------------------------------------------- observability */

export type TraceSpanKind = 'Internal' | 'Server' | 'Client' | 'Producer' | 'Consumer';

export type TraceSpanStatus = 'Unset' | 'Ok' | 'Error';

export interface TraceSpan {
  id: string;
  parentId?: string | null;
  spanId: string;
  name: string;
  kind: TraceSpanKind;
  startedAt: string;
  endedAt?: string | null;
  status: TraceSpanStatus;
  attributes: Record<string, string>;
}

export interface RunTrace {
  id: string;
  traceId: string;
  runId?: string | null;
  tenantId: string;
  startedAt: string;
  endedAt?: string | null;
  spans: TraceSpan[];
}

/* ------------------------------------------------------------ governance */

export type McpTransportMode = 'StreamableHttp' | 'Sse';

/**
 * A registered remote MCP server.
 *
 * Carries no secret: only the *name* of the configuration key whose value
 * becomes the `Authorization` header (decision K-059).
 */
export interface McpServerDefinition {
  id: string;
  tenantId: string;
  name: string;
  description?: string | null;
  endpoint: string;
  transport: McpTransportMode;
  authorizationConfigurationKey?: string | null;
  headers: Record<string, string>;
  enabled: boolean;
  requiresApproval: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface McpServerRequest {
  description?: string | null;
  endpoint: string;
  transport: McpTransportMode;
  authorizationConfigurationKey?: string | null;
  headers?: Record<string, string>;
  enabled: boolean;
  requiresApproval: boolean;
}

export interface ToolApprovalRule {
  id: string;
  tenantId: string;
  agentName?: string | null;
  toolName: string;
  argumentsHash?: string | null;
  createdBy?: string | null;
  createdAt: string;
}

export interface TenantDescriptor {
  id: string;
  slug: string;
  displayName: string;
  createdAt: string;
}

export interface CurrentTenant {
  tenantId: string;
}

/** Decision sent back for a pending tool call. */
export interface ToolApprovalDecision {
  requestId: string;
  approved: boolean;
  reason?: string;
  remember?: boolean;
  rememberArgumentsOnly?: boolean;
}

/** `POST /v1/conversations` reserves an identifier; the session is born on first use. */
export interface Conversation {
  id: string;
  object: string;
  created_at: number;
}
