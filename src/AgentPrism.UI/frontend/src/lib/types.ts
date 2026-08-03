/**
 * Wire contracts served by AgentPrism.AspNetCore.
 *
 * These mirror the .NET records one to one. Enums arrive as names, never as
 * numbers (decision K-040), so the string unions below are the whole contract.
 */

export type AgentOrigin = 'Code' | 'Maf' | 'Database';

/**
 * `AwaitingInput` only ever appears on workflow runs: the graph reached a
 * request port, its state was written to a checkpoint and the stream closed.
 * Answering it starts a *new* run — the original row keeps this status, because
 * run history is append-only (decision K-014).
 */
export type RunStatus = 'Running' | 'Completed' | 'Failed' | 'Canceled' | 'AwaitingInput';

export type RunEventType =
  | 'RunStarted'
  | 'MessageDelta'
  | 'MessageCompleted'
  | 'ToolInvoking'
  | 'ToolInvoked'
  | 'ToolFailed'
  | 'RunCompleted'
  | 'RunFailed'
  | 'ChildRunStarted'
  | 'ChildRunCompleted'
  | 'HistoryCompacted'
  | 'WorkflowStarted'
  | 'SuperStepStarted'
  | 'SuperStepCompleted'
  | 'ExecutorInvoked'
  | 'ExecutorCompleted'
  | 'ExecutorFailed'
  | 'WorkflowOutput'
  | 'WorkflowRequest'
  | 'RunAwaitingInput';

export type CompactionStrategyKind =
  | 'None'
  | 'SlidingWindow'
  | 'Truncation'
  | 'ToolResult'
  | 'Summarization'
  | 'ContextWindow'
  | 'Pipeline';

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
    jobStore: string;
    jobWorkerEnabled: boolean;
  };
  roles: RoleMeta;
}

/**
 * Which role levels the caller of `/api/meta` currently satisfies.
 *
 * The UI hides buttons the caller cannot use; the server is still the only
 * real enforcement. When a role's policy is not registered on the consumer's
 * side, the corresponding flag is `true` — there is no role restriction, so
 * nothing should be hidden for it.
 */
export interface RoleMeta {
  canRead: boolean;
  canOperate: boolean;
  canAdminister: boolean;
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

export interface CompactionSettings {
  strategy: CompactionStrategyKind;
  triggerTokens?: number | null;
  triggerMessages?: number | null;
  triggerTurns?: number | null;
  minimumPreservedTurns?: number | null;
  minimumPreservedGroups?: number | null;
  maxContextWindowTokens?: number | null;
  maxOutputTokens?: number | null;
  summarizationPrompt?: string | null;
  summarizationModel?: ModelBinding | null;
}

export interface MemorySettings {
  enableFileMemory?: boolean;
  enableTodo?: boolean;
  enableTextSearch?: boolean;
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
  skillNames: string[];
  callableAgentNames: string[];
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
  skillNames: string[];
  callableAgentNames: string[];
  harness?: HarnessSettings | null;
  compaction?: CompactionSettings | null;
  memory?: MemorySettings | null;
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
  skillNames: string[];
  callableAgentNames: string[];
  harness?: HarnessSettings | null;
  compaction?: CompactionSettings | null;
  memory?: MemorySettings | null;
}

export interface AgentSkillResourceDefinition {
  name: string;
  description?: string | null;
  mediaType: string;
  content: string;
}

/** A script the server may execute. Storing one is not enough to run it: an active grant is also required. */
export interface AgentSkillScriptDefinition {
  name: string;
  description?: string | null;
  /** File extension without the dot. Must match a configured interpreter. */
  extension: string;
  content: string;
  parametersSchema?: string | null;
}

/** Permission for a tenant to run a skill script. A null scriptName covers every script of the skill. */
export interface SkillScriptGrant {
  id: string;
  tenantId: string;
  skillName: string;
  scriptName?: string | null;
  grantedBy?: string | null;
  grantedAt: string;
  expiresAt?: string | null;
  revokedAt?: string | null;
}

export interface SkillScriptGrantRequest {
  skillName: string;
  scriptName?: string | null;
  expiresAt?: string | null;
}

export interface AgentSkillDefinition {
  id: string;
  tenantId: string;
  name: string;
  description: string;
  instructions: string;
  compatibility?: string | null;
  license?: string | null;
  allowedTools?: string | null;
  metadata: Record<string, unknown>;
  enabled: boolean;
  version: number;
  resources: AgentSkillResourceDefinition[];
  scripts: AgentSkillScriptDefinition[];
  createdAt: string;
  updatedAt: string;
}

export interface AgentSkillRequest {
  name: string;
  description: string;
  instructions: string;
  compatibility?: string | null;
  license?: string | null;
  allowedTools?: string | null;
  metadata?: Record<string, unknown>;
  enabled: boolean;
  resources: AgentSkillResourceDefinition[];
  scripts: AgentSkillScriptDefinition[];
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

export type RunKind = 'Agent' | 'Workflow';

export interface RunRecord {
  id: string;
  agentName: string;
  /** Whether this row records an agent turn or a workflow execution. */
  kind: RunKind;
  /** Set only on workflow rows; the agent rows underneath carry their own names. */
  workflowName?: string | null;
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
  /** Parent run that started this one. Null for root runs. */
  parentRunId?: string | null;
  /** Root of the call tree. Null for root runs; always set for children. */
  rootRunId?: string | null;
  /** Depth in the call tree. Root runs are 0. */
  depth: number;
  /** Number of *direct* child runs. */
  childRunCount: number;
  /**
   * Tokens spent by this run and everything under it.
   *
   * Never add this to `usage` — it already contains it. The two are separate
   * answers to separate questions: "what did this run cost" and "what did this
   * request cost in total".
   */
  treeUsage?: RunUsage | null;
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
  /** Workflow runs stopped on a human decision. Counted separately: they are neither running nor settled. */
  awaitingInputRuns: number;
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

/**
 * An uploaded file's metadata. The binary content never travels through this
 * type — it lives at `GET api/attachments/{id}` and is fetched separately
 * (decision: docs/14-COK-MODLULUK.md, section 14.1 — messages stay small).
 */
export interface AttachmentDescriptor {
  id: string;
  tenantId: string;
  sessionId?: string | null;
  runId?: string | null;
  fileName: string;
  mediaType: string;
  byteSize: number;
  sha256: string;
  createdBy?: string | null;
  createdAt: string;
}

/** `POST /v1/conversations` reserves an identifier; the session is born on first use. */
export interface Conversation {
  id: string;
  object: string;
  created_at: number;
}

/* ---------------------------------------------------------------- audit */

/**
 * One audit trail row: who changed what, when.
 *
 * Runs (an agent processing a message) are never written here — the `runs`
 * table already keeps that full record. `before`/`after` are raw JSON text
 * that has already passed the server's secret filter.
 */
export interface AuditEntry {
  id: string;
  tenantId: string;
  actor?: string | null;
  action: string;
  entity: string;
  before?: string | null;
  after?: string | null;
  createdAt: string;
}

/* ------------------------------------------------------------- workflows */

/** The five ready-made patterns a workflow can be built from. */
export type WorkflowKind = 'Sequential' | 'Concurrent' | 'Handoff' | 'GroupChat' | 'Magentic';

/**
 * A workflow in the catalogue.
 *
 * Code-defined workflows win over stored ones of the same name (decision K-019
 * applied to workflows): whoever can write to the database cannot take over a
 * behaviour that ships with the deployment.
 */
export interface WorkflowDescriptor {
  name: string;
  displayName?: string | null;
  description?: string | null;
  origin: AgentOrigin;
  /**
   * Null for a workflow registered in code: a free graph built by a factory
   * does not correspond to any of the ready-made patterns. Its shape is only
   * known once compiled — which is what the graph endpoint is for.
   */
  kind?: WorkflowKind | null;
  agentNames: string[];
  version: number;
  updatedAt?: string | null;
}

export interface WorkflowDefinition {
  name: string;
  displayName?: string | null;
  description?: string | null;
  kind: WorkflowKind;
  agentNames: string[];
  /** Required by `Magentic`, rejected by every other pattern (decision K-125). */
  managerAgentName?: string | null;
  maxIterations?: number | null;
  /** Only meaningful for `Handoff`. */
  handoffInstructions?: string | null;
  /** Only meaningful for `Magentic`. Costs a manager turn on every round. */
  requirePlanApproval: boolean;
  tenantId?: string | null;
  version: number;
  updatedAt?: string | null;
}

/** Body of PUT `/api/workflows/{name}`. Server-owned fields are deliberately absent. */
export interface WorkflowSaveRequest {
  displayName?: string | null;
  description?: string | null;
  kind: WorkflowKind;
  agentNames: string[];
  managerAgentName?: string | null;
  maxIterations?: number | null;
  handoffInstructions?: string | null;
  requirePlanApproval: boolean;
}

/**
 * What a node in the graph represents.
 *
 * `Orchestration` nodes are added by the pattern itself — the user never wrote
 * them, but run events name them, so hiding them would leave those events
 * pointing at nothing.
 */
export type WorkflowNodeKind = 'Unknown' | 'Agent' | 'Orchestration' | 'RequestPort' | 'Output';

export type WorkflowEdgeKind = 'Direct' | 'FanOut' | 'FanIn';

export interface WorkflowGraphNode {
  /** Matches the executor id carried by `ExecutorInvoked` events, character for character. */
  id: string;
  label: string;
  kind: WorkflowNodeKind;
  agentName?: string | null;
  executorType?: string | null;
}

export interface WorkflowGraphEdge {
  from: string;
  to: string;
  kind: WorkflowEdgeKind;
}

export interface WorkflowGraph {
  name: string;
  startExecutorId: string;
  nodes: WorkflowGraphNode[];
  edges: WorkflowGraphEdge[];
  /**
   * Mermaid text produced by Microsoft Agent Framework.
   *
   * The console draws the graph itself and never renders this: mermaid.js costs
   * around 100 KB gzipped against a 250 KB budget (decision K-002). It exists so
   * a workflow can be pasted straight into a document.
   */
  mermaid: string;
}

/** Which input the console should show for a pending request. */
export type WorkflowRequestForm = 'Json' | 'Text' | 'Boolean' | 'PlanReview';

export interface WorkflowPendingRequest {
  runId: string;
  requestId: string;
  portId: string;
  requestType?: string | null;
  responseType?: string | null;
  prompt?: string | null;
  form: WorkflowRequestForm;
  requestedAt: string;
}

/** Body of POST `/api/workflows/runs/{runId}/respond`. */
export interface WorkflowRespondRequest {
  requestId: string;
  approved?: boolean;
  text?: string;
  data?: unknown;
  checkpointId?: string;
}

/**
 * A saved execution snapshot. The state itself never travels through this type:
 * it is a `json` column that only the engine reads.
 */
export interface WorkflowCheckpointRecord {
  id: string;
  tenantId: string;
  sessionId: string;
  checkpointId: string;
  parentCheckpointId?: string | null;
  runId?: string | null;
  createdAt: string;
}

/* ------------------------------------------------------------ scheduling */

/** What a job runs. */
export type JobKind = 'AgentBatch' | 'Workflow' | 'Eval';

/** A queued job's lifecycle. */
export type JobStatus = 'Pending' | 'Leased' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

/** A single item's processing state within a job. */
export type JobItemStatus = 'Pending' | 'Completed' | 'Failed';

/**
 * A schedule definition: when and what to run.
 *
 * `cron` absent means the schedule only runs when triggered by hand — no
 * automatic `nextRunAt` is ever computed for it.
 */
export interface JobSchedule {
  id: string;
  tenantId: string;
  name: string;
  kind: JobKind;
  targetName: string;
  cron?: string | null;
  timeZone: string;
  payload: unknown;
  enabled: boolean;
  nextRunAt?: string | null;
  lastRunAt?: string | null;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Body of PUT `/api/schedules/{name}`. Server-owned fields are deliberately absent. */
export interface JobScheduleSaveRequest {
  kind: JobKind;
  targetName: string;
  cron?: string | null;
  timeZone: string;
  payload: unknown;
  enabled: boolean;
}

/** Body of POST `/api/schedules/{name}/trigger`. Omit `payload` to reuse the schedule's own. */
export interface JobTriggerRequest {
  payload?: unknown;
}

/** A queued job: the header row for its items. */
export interface JobRecord {
  id: string;
  tenantId: string;
  scheduleId?: string | null;
  kind: JobKind;
  targetName: string;
  status: JobStatus;
  payload: unknown;
  totalItems: number;
  doneItems: number;
  failedItems: number;
  attempt: number;
  leaseOwner?: string | null;
  leaseUntil?: string | null;
  scheduledFor: string;
  startedAt?: string | null;
  completedAt?: string | null;
  errorMessage?: string | null;
  createdAt: string;
}

/** One input of a batch job and the run it produced. */
export interface JobItemRecord {
  id: string;
  jobId: string;
  seq: number;
  input: string;
  runId?: string | null;
  status: JobItemStatus;
  error?: string | null;
}

export interface JobDetailResponse {
  job: JobRecord;
  items: JobItemRecord[];
}

/* -------------------------------------------------------------------- eval */

/** An eval run's lifecycle. */
export type EvalRunStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

/** A test suite: which agent it measures, and how a case is judged. */
export interface EvalSuite {
  id: string;
  tenantId: string;
  name: string;
  description?: string | null;
  agentName: string;
  checks: unknown;
  createdAt: string;
  updatedAt: string;
}

/** Body of PUT `/api/evals/{name}`. Server-owned fields are deliberately absent. */
export interface EvalSuiteSaveRequest {
  description?: string | null;
  agentName: string;
  checks: unknown;
}

/** A single test case within a suite. */
export interface EvalCase {
  id: string;
  suiteId: string;
  seq: number;
  query: string;
  expectedOutput?: string | null;
  expectedTools: string[];
  context?: string | null;
}

/** One entry of the array body of PUT `/api/evals/{name}/cases`. */
export interface EvalCaseInput {
  query: string;
  expectedOutput?: string | null;
  expectedTools?: string[];
  context?: string | null;
}

/** Body of POST `/api/evals/{name}/run`. */
export interface EvalRunTriggerRequest {
  modelId?: string | null;
  numRepetitions?: number | null;
}

/** One run of a suite and its summary. */
export interface EvalRun {
  id: string;
  tenantId: string;
  suiteId: string;
  jobId?: string | null;
  agentVersion?: number | null;
  modelId?: string | null;
  status: EvalRunStatus;
  total: number;
  passed: number;
  failed: number;
  inputTokens?: number | null;
  outputTokens?: number | null;
  startedAt: string;
  completedAt?: string | null;
}

/** One case's result within an eval run. */
export interface EvalCaseResult {
  id: string;
  evalRunId: string;
  caseId: string;
  runId?: string | null;
  passed: boolean;
  output?: string | null;
  scores: unknown;
  failureReason?: string | null;
}

export interface EvalRunDetailResponse {
  run: EvalRun;
  results: EvalCaseResult[];
}
