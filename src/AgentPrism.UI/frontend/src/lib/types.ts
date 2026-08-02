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

export interface ModelProviderDescriptor {
  name: string;
  displayName?: string | null;
  models: ModelDescriptor[];
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
  errorRate?: number | null;
}

/** `POST /v1/conversations` reserves an identifier; the session is born on first use. */
export interface Conversation {
  id: string;
  object: string;
  created_at: number;
}
