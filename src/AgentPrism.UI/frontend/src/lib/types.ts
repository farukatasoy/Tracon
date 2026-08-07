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
 *
 * `Queued` only appears on runs started with `Prefer: respond-async` (phase 46):
 * the row exists but the worker has not picked up the job yet.
 */
export type RunStatus = 'Running' | 'Completed' | 'Failed' | 'Canceled' | 'AwaitingInput' | 'Queued';

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

export type AgentResponseFormatKind = 'Text' | 'Json' | 'JsonSchema';

export interface AgentResponseFormat {
  kind: AgentResponseFormatKind;
  schema?: unknown;
  schemaName?: string | null;
  schemaDescription?: string | null;
}

export interface ModelBinding {
  provider: string;
  model: string;
  temperature?: number | null;
  maxOutputTokens?: number | null;
  topP?: number | null;
  reasoningEffort?: string | null;
  responseFormat?: AgentResponseFormat | null;
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

export type ValidationSeverity = 'Error' | 'Warning';

/** One finding from `POST /api/agents/validate`. `message` comes from the server and is not translated (K-232). */
export interface ValidationMessage {
  severity: ValidationSeverity;
  code: string;
  message: string;
  path?: string | null;
}

/** Result of validating a definition without saving it or calling a model. */
export interface AgentValidationReport {
  valid: boolean;
  inconclusive: boolean;
  messages: ValidationMessage[];
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
  supportsStructuredOutput: boolean;
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

/**
 * Self-check report from `GET /api/diagnostics` (Phase 33).
 *
 * Carries no secret value — only whether a configuration key resolved, never
 * the key's content (decision K-059).
 */
export interface DiagnosticsReport {
  persistenceProvider: string;
  registeredPersistenceProviders: number;
  canConnect: boolean;
  migrationsUpToDate: boolean;
  pendingMigrations: string[];
  modelProviders: ProviderDiagnostic[];
  configuration: ConfigurationDiagnostic[];
  uiEmbedded: boolean;
  toolCount: number;
  agentCount: number;
}

export interface ProviderDiagnostic {
  name: string;
  status: ModelProviderHealthStatus;
  circuitOpen: boolean;
}

/** Whether a configuration key resolved — never its value. */
export interface ConfigurationDiagnostic {
  key: string;
  resolved: boolean;
  hint?: string | null;
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

export type RunKind = 'Agent' | 'Workflow' | 'Eval';

/** Where a run's cost came from. `Unknown` means the model is known but has no configured price — never shown as free. */
export type PricingSource = 'Catalog' | 'Configuration' | 'Unknown';

/**
 * A run's own cost, computed once when it completes (a price snapshot — a
 * later price change never rewrites history). `Unknown` still returns a
 * non-null object with both cost fields `null`, distinct from "no model at all".
 */
export interface RunCost {
  inputCost?: number | null;
  outputCost?: number | null;
  currency?: string | null;
  source: PricingSource;
}

/**
 * Total cost of a run's whole subtree (itself and everything under it).
 *
 * Never add this to `cost` — it already contains it, same pattern as
 * `treeUsage`/`usage`.
 */
export interface RunTreeCost {
  inputCost?: number | null;
  outputCost?: number | null;
  currency?: string | null;
  runsWithUnknownPricing: number;
}

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
  /** Definition version this run measured. Null when unknown (pre-Faz-19 rows, code agents without history). */
  agentVersion?: number | null;
  /** Experiment this run was assigned to, if any. */
  experimentId?: string | null;
  /** Variant name assigned within `experimentId`. */
  variant?: string | null;
  /**
   * Tokens spent by this run and everything under it.
   *
   * Never add this to `usage` — it already contains it. The two are separate
   * answers to separate questions: "what did this run cost" and "what did this
   * request cost in total".
   */
  treeUsage?: RunUsage | null;
  /** This run's own cost. Null when the model is unknown (e.g. a code agent with no bound model). */
  cost?: RunCost | null;
  /** Cost of this run's whole subtree. See {@link RunCost} for why it is a separate field. */
  treeCost?: RunTreeCost | null;
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

/** `RunScore.Kind` — the shape a score's `value` takes. */
export type RunScoreKind = 'Binary' | 'Stars';

/** A human (or judge) score attached to a run or a single message within it. */
export interface RunScore {
  id: string;
  tenantId: string;
  runId: string;
  /** Null when the score applies to the whole run rather than one message. */
  messageId?: string | null;
  kind: RunScoreKind;
  /** 0/1 for `Binary`, 1..5 for `Stars`. */
  value: number;
  comment?: string | null;
  /** `human` today; `api`/`judge` are reserved for later phases. */
  source: string;
  /** Null in an unauthenticated setup — every call then writes a new row. */
  author?: string | null;
  createdAt: string;
}

/** Body of `POST /api/runs/{runId}/feedback`. */
export interface RunFeedbackRequest {
  kind: RunScoreKind;
  value: number;
  messageId?: string | null;
  comment?: string | null;
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
  /** This model's total cost. Null when its price is undefined. */
  totalCost?: number | null;
}

/**
 * A run failure's class. `Unknown` is not a failure mode of the classifier —
 * it is the bucket for rows written before error classification existed, and
 * for messages that matched no rule. Server-issued value, never translated
 * (K-232) — only the on-screen label is.
 */
export type RunErrorClass =
  | 'Unknown'
  | 'ProviderError'
  | 'ProviderUnavailable'
  | 'RateLimited'
  | 'QuotaExceeded'
  | 'ContentFiltered'
  | 'ToolError'
  | 'Timeout'
  | 'CompilationFailed'
  | 'BudgetExceeded'
  | 'Canceled';

/** Runs sharing the same normalized-message fingerprint. */
export interface RunErrorCluster {
  fingerprint: string;
  count: number;
  sampleMessage: string;
  sampleRunId: string;
  lastSeenAt: string;
}

export interface RunErrorStatistics {
  class: RunErrorClass;
  totalRuns: number;
  /** Up to three clusters, largest first. */
  topClusters: RunErrorCluster[];
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
  /** Total cost across priced runs only. Null when nothing here has a configured price. */
  totalCost?: number | null;
  currency?: string | null;
  /** Runs whose model is known but has no configured price. */
  runsWithUnknownPricing: number;
  byAgent: RunAgentStatistics[];
  byModel: RunModelStatistics[];
  /** Failed-run breakdown by class. Rows written before error classification existed fall into `Unknown`. */
  byErrorClass: RunErrorStatistics[];
  errorRate?: number | null;
  /** Runs (or messages within them) that carry at least one score. Eval runs are excluded, same as `totalRuns`. */
  scoredRuns: number;
  /** Share of `Binary`-kind scores marked positive (0-1). Star ratings do not count toward this. Null when no binary score exists. */
  positiveRate?: number | null;
}

/** Bucket width for `GET /api/stats/timeseries`. */
export type TimeSeriesBucket = 'Hour' | 'Day';

/** One bucket of `/api/stats/timeseries`. Empty buckets are returned too, with zero counts. */
export interface TimeSeriesPoint {
  bucket: string;
  runs: number;
  failedRuns: number;
  inputTokens: number;
  outputTokens: number;
  cost?: number | null;
  averageDurationMs?: number | null;
}

/** Response of `POST /api/stats/recalculate-costs`. */
export interface RunCostRecalculationResult {
  runsConsidered: number;
  runsUpdated: number;
  runsStillUnknown: number;
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

/** The only OAuth flow this SDK supports — a redirect-based, interactive one. */
export type McpOAuthAuthorizationMode = 'AuthorizationCode';

/**
 * A registered remote MCP server.
 *
 * Carries no secret: only the *name* of the configuration key whose value
 * becomes the `Authorization` header, or the OAuth client secret (decision K-059).
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
  oauthEnabled: boolean;
  oauthClientId?: string | null;
  oauthClientSecretConfigurationKey?: string | null;
  oauthScopes?: string | null;
  oauthAuthorizationMode: McpOAuthAuthorizationMode;
}

export interface McpServerRequest {
  description?: string | null;
  endpoint: string;
  transport: McpTransportMode;
  authorizationConfigurationKey?: string | null;
  headers?: Record<string, string>;
  enabled: boolean;
  requiresApproval: boolean;
  oauthEnabled?: boolean;
  oauthClientId?: string | null;
  oauthClientSecretConfigurationKey?: string | null;
  oauthScopes?: string | null;
}

export interface McpOAuthStartResponse {
  authorizationUri: string;
  state: string;
}

export interface McpPromptArgumentSummary {
  name: string;
  description?: string | null;
  required: boolean;
}

export interface McpPromptSummary {
  name: string;
  title?: string | null;
  description?: string | null;
  arguments: McpPromptArgumentSummary[];
}

export interface McpPromptContent {
  text: string;
  hash: string;
}

export interface McpResourceSummary {
  uri: string;
  name: string;
  mimeType?: string | null;
  description?: string | null;
}

export interface McpResourceContent {
  uri: string;
  mimeType?: string | null;
  text?: string | null;
  isBinary: boolean;
  byteSize: number;
  truncated: boolean;
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

/** Result of `POST api/voice/speak`. */
export interface SpeakResponse {
  attachment: AttachmentDescriptor;
  /**
   * Billed characters.
   *
   * Voice is priced per character, not per token, so this number never joins
   * the token totals shown elsewhere — two different units cannot be summed.
   */
  characters: number;
  /** True when the provider did not report a count and the length was estimated. */
  isEstimated: boolean;
  cost?: number | null;
  currency?: string | null;
}

/** One selectable voice from `GET api/voice/voices`. */
export interface VoiceDescriptor {
  voiceId: string;
  name: string;
  category?: string | null;
}

/** Result of `GET api/voice/health`. */
export interface VoiceHealth {
  providerName: string;
  isHealthy: boolean;
  latency: string;
  checkedAt: string;
  detail?: string | null;
  voiceCount?: number | null;
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
export type JobKind = 'AgentBatch' | 'Workflow' | 'Eval' | 'WebhookDelivery';

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

/** Why a case was promoted from a production run. */
export type EvalCaseSource = 'FailedRun' | 'NegativeScore' | 'ReferenceRun';

/** A single test case within a suite. */
export interface EvalCase {
  id: string;
  suiteId: string;
  seq: number;
  query: string;
  expectedOutput?: string | null;
  expectedTools: string[];
  context?: string | null;
  /** The run this case was promoted from. `null` for hand-written cases. */
  sourceRunId?: string | null;
  /** Why it was promoted. `null` for hand-written cases. */
  sourceKind?: EvalCaseSource | null;
  /** When it was promoted. `null` for hand-written cases. */
  promotedAt?: string | null;
}

/** One entry of the array body of PUT `/api/evals/{name}/cases`. */
export interface EvalCaseInput {
  query: string;
  expectedOutput?: string | null;
  expectedTools?: string[];
  context?: string | null;
}

/** Body of POST `/api/evals/{name}/cases/from-run/{runId}`. */
export interface EvalCasePromotionRequest {
  /** Overrides the auto-detected promotion reason. */
  sourceKind?: EvalCaseSource | null;
}

/** Body of POST `/api/evals/{name}/run`. */
export interface EvalRunTriggerRequest {
  modelId?: string | null;
  numRepetitions?: number | null;
  /** Pins the eval run to a specific definition version instead of the current one. */
  agentVersion?: number | null;
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

/* ---------------------------------------------------------------- versioning & experiments */

/** Raw response of `GET /api/agents/{name}/versions/{a}/diff/{b}`. The client computes the diff. */
export interface AgentVersionDiffResponse {
  left: AgentDefinition;
  right: AgentDefinition;
}

/** An A/B experiment's lifecycle. */
export type ExperimentStatus = 'Draft' | 'Running' | 'Stopped';

/** One arm of an experiment: which definition version, at what traffic share. */
export interface ExperimentVariant {
  name: string;
  version: number;
  weight: number;
}

/** An A/B experiment splitting traffic between definition versions of the same agent. */
export interface Experiment {
  id: string;
  tenantId: string;
  name: string;
  agentName: string;
  variants: ExperimentVariant[];
  status: ExperimentStatus;
  assignmentKey?: string | null;
  startedAt?: string | null;
  endedAt?: string | null;
  updatedAt?: string | null;
}

/** Body of PUT `/api/experiments/{name}`. Server-owned fields are deliberately absent. */
export interface ExperimentSaveRequest {
  agentName: string;
  variants: ExperimentVariant[];
}

/** One variant's run summary. No statistical "winner" claim is made — raw counts only. */
export interface ExperimentVariantResult {
  variant: string;
  version: number;
  totalRuns: number;
  completedRuns: number;
  failedRuns: number;
  canceledRuns: number;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  averageDurationMs?: number | null;
  errorRate?: number | null;
  totalCost?: number | null;
  currency?: string | null;
}

export interface ExperimentResultsResponse {
  experiment: Experiment;
  results: ExperimentVariantResult[];
}

/** How often a quota counter resets. */
export type QuotaPeriod = 'Daily' | 'Monthly';

/** Which measure a quota is exceeded by. */
export type QuotaMetric = 'Runs' | 'Tokens' | 'Cost';

/**
 * A quota rule.
 *
 * `agentName` absent means the rule covers every run of the tenant. All three
 * limits may be null; only the ones that are set are enforced.
 */
export interface QuotaDefinition {
  id: string;
  tenantId: string;
  agentName?: string | null;
  period: QuotaPeriod;
  maxRuns?: number | null;
  maxTokens?: number | null;
  maxCost?: number | null;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

/**
 * A scope's consumption in the current period.
 *
 * An empty `agentName` is the tenant-wide counter. Counts are approximate: the
 * check runs before a run starts, the consumption is written after it ends.
 */
export interface QuotaUsageRecord {
  tenantId: string;
  agentName: string;
  period: QuotaPeriod;
  periodStart: string;
  runs: number;
  tokens: number;
  cost: number;
  updatedAt: string;
}

export interface QuotaUsageResponse {
  tenantId: string;
  timeZone: string;
  usage: QuotaUsageRecord[];
  definitions: QuotaDefinition[];
  dailyResetsAt: string;
  monthlyResetsAt: string;
}

export interface QuotaSaveRequest {
  agentName?: string | null;
  period: QuotaPeriod;
  maxRuns?: number | null;
  maxTokens?: number | null;
  maxCost?: number | null;
  enabled: boolean;
}

/** A webhook delivery attempt's state. */
export type WebhookDeliveryStatus = 'Pending' | 'Delivered' | 'Failed' | 'Dropped';

/**
 * A webhook subscription.
 *
 * There is no secret field: the record only carries the NAME of the
 * configuration key the signing secret is read from. The value never leaves
 * `IConfiguration`.
 */
export interface WebhookSubscription {
  id: string;
  tenantId: string;
  name: string;
  url: string;
  events: string[];
  secretConfigurationKey?: string | null;
  headers: Record<string, string>;
  enabled: boolean;
  consecutiveFailures: number;
  createdAt: string;
  updatedAt: string;
}

/** One delivery record. History only — scheduling lives in the job queue. */
export interface WebhookDelivery {
  id: string;
  subscriptionId: string;
  tenantId: string;
  eventType: string;
  payload: string;
  status: WebhookDeliveryStatus;
  attempt: number;
  responseCode?: number | null;
  error?: string | null;
  createdAt: string;
  deliveredAt?: string | null;
}

export interface WebhookSaveRequest {
  url: string;
  events: string[];
  secretConfigurationKey?: string | null;
  headers?: Record<string, string>;
  enabled: boolean;
}

export interface WebhookTestResponse {
  name: string;
  queued: boolean;
  message: string;
}

/** The fixed whitelist of retention targets (`RetentionTargets` on the server). */
export type RetentionTarget =
  | 'run_events'
  | 'tool_invocations'
  | 'traces'
  | 'jobs'
  | 'webhook_deliveries'
  | 'eval_case_results'
  | 'workflow_checkpoints'
  | 'skill_script_grants'
  | 'attachments'
  | 'sessions'
  | 'conversations';

/** A stored retention policy for one target. Absent unless the tenant configured it. */
export interface RetentionPolicy {
  id: string;
  tenantId: string;
  target: RetentionTarget;
  maxAgeDays?: number | null;
  maxRows?: number | null;
  archive: boolean;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface RetentionPolicySaveRequest {
  maxAgeDays?: number | null;
  maxRows?: number | null;
  archive: boolean;
  enabled: boolean;
}

/** "If this ran right now" preview for one target. Never deletes anything. */
export interface RetentionPreview {
  target: RetentionTarget;
  maxAgeDays?: number | null;
  enabled: boolean;
  cutoff?: string | null;
  matchingRows: number;
}

/** History record of one cleanup run. */
export interface RetentionRun {
  id: string;
  tenantId: string;
  target: RetentionTarget;
  deletedRows: number;
  archivedRows: number;
  startedAt: string;
  completedAt?: string | null;
  error?: string | null;
}

export interface RetentionRunTriggerResponse {
  jobId: string;
  target: string;
}
