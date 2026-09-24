import type * as Generated from '@tracon/client';

/**
 * Response types with fields the generated type gets wrong in two specific,
 * verified ways — see docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md, section 84.6.
 *
 * 1. **Missing `required`.** ASP.NET Core's OpenAPI generator marks a
 *    non-nullable property `required` only when the C# property has no
 *    default-value initializer. A response record like `AgentDescriptor`
 *    sets `ToolNames { get; init; } = [];` for ergonomics on the SERVER
 *    side, but `System.Text.Json` serializes every public property
 *    regardless of whether it equals the type's default — a response
 *    instance always carries a real value. The heuristic is
 *    request/response-blind; for a REQUEST type the same pattern correctly
 *    means "the client may omit this".
 *
 * 2. **`number | string` instead of `number`.** Every `int`/`long` field in
 *    the whole document (254 schemas, 72 directly) is typed as
 *    `["integer","string"]` with a numeric-string regex pattern — an
 *    interoperability allowance for reading a number that arrived as a
 *    JSON string. This repo has no `[JsonNumberHandling(WriteAsString)]`
 *    anywhere (verified: `grep -rn JsonNumberHandling src` is empty), so
 *    every one of these fields is ALWAYS written as a native JSON number.
 *
 * Fixing either at the source is its own body of work (roughly 150
 * `required` annotations across a dozen-plus files, or auditing why the
 * OpenAPI generator adds the numeric-string allowance), not a side effect
 * of generating a TypeScript client — deferred to a follow-up candidate
 * (Open Question 4, option A). This file is the frontend's side of that
 * deferral: `Fix` below narrows exactly the listed fields so the 43
 * migrated screens read them the same way the old hand-written `types.ts`
 * did, with no new `?.`/`!`/`Number(...)` noise at any call site.
 *
 * Every type below is generated from `docs/openapi/tracon.json` by a
 * script that (a) walks every schema transitively reachable from a 2xx JSON
 * response, (b) flags exactly the two patterns above per field, and (c) for
 * any type that embeds ANOTHER widened type by reference (directly or in an
 * array), replaces that field's type with the widened one too — otherwise
 * `record.usage?.inputTokens` would still be `number | string` even though
 * `RunRecord` itself is "fixed". A type with no field of its own to fix but
 * that embeds a widened one (e.g. `RunComparisonResponse`) still gets an
 * entry, built with `Omit<Generated.X, ...> & { ... }` instead of `Fix`.
 */
type Fix<T, K extends keyof T> = Omit<T, K> & {
  [P in K]-?: [Extract<T[P], number>] extends [never] ? T[P] : Exclude<T[P], string>;
};

// 92 schemas widened; 28 carry a nested-reference override (104 total exports)

export type AIContentCodeInterpreterToolCallContent = Fix<Generated.AiContentCodeInterpreterToolCallContent, 'additionalProperties' | 'annotations' | 'inputs'>;
export type AIContentFunctionCallContent = Fix<Generated.AiContentFunctionCallContent, 'informationalOnly'>;
export type AIContentHostedFileContent = Fix<Generated.AiContentHostedFileContent, 'sizeInBytes'>;
export type AIContentImageGenerationToolCallContent = Fix<Generated.AiContentImageGenerationToolCallContent, 'additionalProperties' | 'annotations'>;
export type AIContentMcpServerToolCallContent = Fix<Generated.AiContentMcpServerToolCallContent, 'additionalProperties' | 'annotations' | 'arguments'>;
export type AIContentToolApprovalRequestContent = Fix<Generated.AiContentToolApprovalRequestContent, 'requiresConfirmation'>;
export type AIContentUsageContent = Omit<Generated.AiContentUsageContent, 'details'> & { details: UsageDetails };
export type AIContentWebSearchToolCallContent = Fix<Generated.AiContentWebSearchToolCallContent, 'additionalProperties' | 'annotations' | 'queries'>;
export type AgentDefinition = Fix<Generated.AgentDefinition, 'callableAgentNames' | 'mcpResourceUris' | 'metadata' | 'origin' | 'skillNames' | 'toolNames' | 'version'> & { compaction: CompactionSettings | null; harness: HarnessSettings | null; memory: MemorySettings | null; model: ModelBinding; parameters: AgentParameter[] };
export type AgentDescriptor = Fix<Generated.AgentDescriptor, 'callableAgentNames' | 'skillNames' | 'toolNames' | 'usesHarness' | 'version'> & { model: ModelBinding | null };
export type AgentDetailResponse = Omit<Generated.AgentDetailResponse, 'definition' | 'descriptor'> & { definition: AgentDefinition | null; descriptor: AgentDescriptor };
export type AgentParameter = Fix<Generated.AgentParameter, 'required'>;
export type AgentParameterKind = Generated.AgentParameterKind;
export type AgentRunDocument = Generated.AgentRunDocument;
export type TraconDiagnosticsReport = Fix<Generated.TraconDiagnosticsReport, 'agentCount' | 'registeredPersistenceProviders' | 'toolCount'>;
export type AgentSkillDefinition = Fix<Generated.AgentSkillDefinition, 'createdAt' | 'enabled' | 'id' | 'metadata' | 'origin' | 'scriptSetHash' | 'scripts' | 'updatedAt' | 'version'> & { resources: AgentSkillResourceDefinition[] };
export type AgentSkillResourceDefinition = Fix<Generated.AgentSkillResourceDefinition, 'mediaType'>;
export type AgentVersionDiffResponse = Omit<Generated.AgentVersionDiffResponse, 'left' | 'right'> & { left: AgentDefinition; right: AgentDefinition };
export type ApiKeyCreationResult = Omit<Generated.ApiKeyCreationResult, 'record'> & { record: ApiKeyRecord };
export type ApiKeyRecord = Fix<Generated.ApiKeyRecord, 'isActive'>;
export type AttachmentDescriptor = Fix<Generated.AttachmentDescriptor, 'byteSize'>;
export type AuditChainVerification = Fix<Generated.AuditChainVerification, 'entriesChecked'>;
export type CanaryEvaluation = Omit<Generated.CanaryEvaluation, 'canary' | 'control'> & { canary: ExperimentVariantResult | null; control: ExperimentVariantResult | null };
export type CanaryPolicy = Fix<Generated.CanaryPolicy, 'maxErrorRateDelta' | 'minSampleSize' | 'minScore' | 'rampInterval' | 'rampSteps'>;
export type ChatChoice = Fix<Generated.ChatChoice, 'index'>;
export type ChatCompletion = Fix<Generated.ChatCompletion, 'created'> & { choices: ChatChoice[]; usage: ChatUsage | null };
export type ChatMessage = Fix<Generated.ChatMessage, 'role'>;
export type ChatUsage = Fix<Generated.ChatUsage, 'completion_tokens' | 'prompt_tokens' | 'total_tokens'>;
export type CompactionSettings = Fix<Generated.CompactionSettings, 'maxContextWindowTokens' | 'maxOutputTokens' | 'minimumPreservedGroups' | 'minimumPreservedTurns' | 'strategy' | 'triggerMessages' | 'triggerTokens' | 'triggerTurns'> & { summarizationModel: ModelBinding | null };
export type ContextWindowEstimate = Fix<Generated.ContextWindowEstimate, 'allowedPromptTokens' | 'contextWindowTokens' | 'promptTokens'>;
export type ConversationResource = Fix<Generated.ConversationResource, 'created_at'>;
export type EvalCase = Fix<Generated.EvalCase, 'expectedTools' | 'id' | 'seq'>;
export type EvalCaseResult = Fix<Generated.EvalCaseResult, 'id' | 'scores'>;
export type EvalCaseDiff = Generated.EvalCaseDiff;
export type EvalCaseDiffKind = Generated.EvalCaseDiffKind;
export type EvalRunDiff = Fix<Generated.EvalRunDiff, 'addedCount' | 'fixedCount' | 'regressedCount' | 'removedCount' | 'stillFailingCount' | 'totalCases' | 'unchangedCount'> & { baseline: EvalRun; candidate: EvalRun; cases: EvalCaseDiff[] };
export type EvalRun = Fix<Generated.EvalRun, 'agentVersion' | 'failed' | 'inputTokens' | 'outputTokens' | 'passed' | 'total'>;
export type EvalRunDetailResponse = Omit<Generated.EvalRunDetailResponse, 'results' | 'run'> & { results: EvalCaseResult[]; run: EvalRun };
export type EvalSuite = Fix<Generated.EvalSuite, 'checks' | 'createdAt' | 'id' | 'updatedAt'>;
// Flattened to a single Omit<>&{} (not built with Fix<>, like RunStatistics
// above) for the same reason: `status` needs Fix<>'s required-narrowing while
// `canary`/`variants` need a nested-reference override, and stacking a Fix<>
// intersection with a second override intersection is the pattern that broke
// generic inference elsewhere in this file.
export type Experiment = Omit<Generated.Experiment, 'status' | 'canary' | 'variants'> & { status: Generated.ExperimentStatus; canary: CanaryPolicy | null; variants: ExperimentVariant[] };
export type ExperimentCanaryResponse = Omit<Generated.ExperimentCanaryResponse, 'policy'> & { policy: CanaryPolicy | null };
export type ExperimentResultsResponse = Omit<Generated.ExperimentResultsResponse, 'experiment' | 'results'> & { experiment: Experiment; results: ExperimentVariantResult[] };
export type ExperimentVariant = Fix<Generated.ExperimentVariant, 'version' | 'weight'>;
export type ExperimentVariantResult = Fix<Generated.ExperimentVariantResult, 'averageDurationMs' | 'averageScore' | 'canceledRuns' | 'completedRuns' | 'errorRate' | 'failedRuns' | 'inputTokens' | 'outputTokens' | 'totalCost' | 'totalRuns' | 'totalTokens' | 'version'>;
export type HarnessSettings = Fix<Generated.HarnessSettings, 'disableAgentModeProvider' | 'disableAgentSkillsProvider' | 'disableCompaction' | 'disableFileMemory' | 'disableTodoProvider' | 'disableToolAutoApproval' | 'disableWebSearch' | 'maxContextWindowTokens' | 'maxOutputTokens' | 'maximumIterationsPerRequest'>;
export type JobDetailResponse = Omit<Generated.JobDetailResponse, 'items' | 'job'> & { items: JobItemRecord[]; job: JobRecord };
export type JobItemRecord = Fix<Generated.JobItemRecord, 'seq'>;
export type JobRecord = Fix<Generated.JobRecord, 'attempt' | 'doneItems' | 'failedItems' | 'lane' | 'maxAttempts' | 'payload' | 'totalItems'>;
export type JobSchedule = Fix<Generated.JobSchedule, 'enabled' | 'id' | 'lane' | 'payload' | 'timeZone'>;
export type JudgeFailure = Generated.JudgeFailure;
export type JudgeRunResponse = Omit<Generated.JudgeRunResponse, 'failures' | 'scores'> & { failures: JudgeFailure[]; scores: RunScore[] };
export type McpPromptArgumentSummary = Fix<Generated.McpPromptArgumentSummary, 'required'>;
export type McpPromptSummary = Omit<Generated.McpPromptSummary, 'arguments'> & { arguments: McpPromptArgumentSummary[] };
export type McpRefreshResponse = Fix<Generated.McpRefreshResponse, 'toolCount'>;
export type McpResourceContent = Fix<Generated.McpResourceContent, 'byteSize' | 'isBinary' | 'truncated'>;
export type McpServerDefinition = Fix<Generated.McpServerDefinition, 'createdAt' | 'enabled' | 'headerConfigurationKeys' | 'headers' | 'oauthAuthorizationMode' | 'oauthEnabled' | 'requiresApproval' | 'transport' | 'updatedAt'>;
export type MemorySettings = Fix<Generated.MemorySettings, 'enableFileMemory' | 'enableTextSearch' | 'enableTodo' | 'enableVectorSearch'>;
export type ModelBinding = Fix<Generated.ModelBinding, 'allowConcurrentToolCalls' | 'fallbacks' | 'maxOutputTokens' | 'providerSettings' | 'temperature' | 'topP'> & { responseCache: ResponseCacheSettings | null };
export type ModelDescriptor = Fix<Generated.ModelDescriptor, 'cachedInputCostPerMillionTokens' | 'contextWindowTokens' | 'inputCostPerMillionTokens' | 'maxOutputTokens' | 'outputCostPerMillionTokens' | 'supportsReasoning' | 'supportsStreaming' | 'supportsStructuredOutput' | 'supportsTools'>;
export type ModelProviderDescriptor = Fix<Generated.ModelProviderDescriptor, 'status'> & { models: ModelDescriptor[] };
export type ModelProviderHealth = Fix<Generated.ModelProviderHealth, 'checkedAt' | 'models'>;
export type OnlineEvaluationSummary = Fix<Generated.OnlineEvaluationSummary, 'averageScore' | 'judgeCost' | 'lowScoreThreshold' | 'minSampleSize' | 'sampleCount'>;
export type QuotaDefinition = Fix<Generated.QuotaDefinition, 'enabled' | 'maxCost' | 'maxRuns' | 'maxTokens'>;
export type QuotaUsageRecord = Fix<Generated.QuotaUsageRecord, 'cost' | 'runs' | 'tokens'>;
export type QuotaUsageResponse = Omit<Generated.QuotaUsageResponse, 'definitions' | 'usage'> & { definitions: QuotaDefinition[]; usage: QuotaUsageRecord[] };
export type ResponseCacheSettings = Fix<Generated.ResponseCacheSettings, 'enabled' | 'lifetime'>;
export type RetentionPolicy = Fix<Generated.RetentionPolicy, 'archive' | 'enabled' | 'maxAgeDays' | 'maxRows'>;
export type RetentionPreview = Fix<Generated.RetentionPreview, 'enabled' | 'matchingRows' | 'maxAgeDays'>;
export type RetentionRun = Fix<Generated.RetentionRun, 'archivedRows' | 'deletedRows'>;
export type RunAgentStatistics = Fix<Generated.RunAgentStatistics, 'failedRuns' | 'totalRuns' | 'totalTokens'>;
export type RunComparisonResponse = Omit<Generated.RunComparisonResponse, 'left' | 'right'> & { left: RunComparisonSide; right: RunComparisonSide };
export type RunComparisonSide = Fix<Generated.RunComparisonSide, 'agentVersion' | 'durationMs' | 'toolCallCount'> & { cost: RunCost | null; scores: RunScore[]; usage: RunUsage | null };
export type RunCost = Fix<Generated.RunCost, 'cachedInputCost' | 'cachedInputPricePerMillionTokens' | 'inputCost' | 'inputPricePerMillionTokens' | 'outputCost' | 'outputPricePerMillionTokens'>;
export type RunCostRecalculationResult = Fix<Generated.RunCostRecalculationResult, 'runsConsidered' | 'runsSkipped' | 'runsStillUnknown' | 'runsUpdated'>;
export type RunErrorCluster = Fix<Generated.RunErrorCluster, 'count'>;
// `class` is required and non-nullable on THIS schema, but `RunErrorClass` is
// ALSO used nullably elsewhere in the document (`errorClass?: null | RunErrorClass`
// on another schema) — openapi-typescript generates one shared type per schema
// name, so the alias itself carries `| null` regardless of this field's own
// (correct) required declaration. Narrowed here, not on the shared alias.
export type RunErrorStatistics = Fix<Generated.RunErrorStatistics, 'totalRuns'> & {
  class: Exclude<Generated.RunErrorClass, null>;
  topClusters: RunErrorCluster[];
};
export type RunInputResponse = Omit<Generated.RunInputResponse, 'messages'> & { messages: ChatMessage[] };
export type RunLabelStatistics = Fix<Generated.RunLabelStatistics, 'failedRuns' | 'totalCost' | 'totalRuns' | 'totalTokens'>;
export type RunModelStatistics = Fix<Generated.RunModelStatistics, 'inputTokens' | 'outputTokens' | 'totalCost' | 'totalRuns' | 'totalTokens'>;
export type RunRecord = Fix<Generated.RunRecord, 'agentVersion' | 'childRunCount' | 'depth' | 'eventCount' | 'isStreaming' | 'kind'> & { cost: RunCost | null; treeCost: RunTreeCost | null; treeUsage: RunUsage | null; usage: RunUsage | null };
export type RunReplayResponse = Fix<Generated.RunReplayResponse, 'agentVersion'>;
export type RunScore = Fix<Generated.RunScore, 'createdAt' | 'id' | 'value'>;
// `categories` is an inline numeric map (not a $ref), so Fix<> (which only
// widens a field's OWN union, not a nested object's values) cannot reach it;
// built by hand like RunStatistics below, for the same React Query
// generic-inference reason (see the comment on RunStatistics).
export type RunScoreAggregate = Omit<Generated.RunScoreAggregate, 'average' | 'categories' | 'count' | 'maximum' | 'minimum' | 'noValueCount' | 'truncatedCategoryCount'> & { average: number | null; categories: Record<string, number>; count: number; maximum: number | null; minimum: number | null; noValueCount: number; truncatedCategoryCount: number };
export type RunScoreBucketAggregate = Omit<Generated.RunScoreBucketAggregate, 'groups'> & { groups: RunScoreAggregate[] };
export type RunScoreSummary = Omit<Generated.RunScoreSummary, 'byAgent' | 'byAuthor' | 'byName' | 'bySource' | 'series'> & { byAgent: RunScoreAggregate[]; byAuthor: RunScoreAggregate[]; byName: RunScoreAggregate[]; bySource: RunScoreAggregate[]; series: RunScoreBucketAggregate[] };
// Flattened by hand into a single Omit<>&{} (not built with Fix<>, unlike
// every other entry in this file): React Query's generic inference could not
// carry a `Fix<...> & {...}` (three intersected object types) through
// `useQuery`'s own generics — `stats.data.byAgent[0].totalRuns` silently fell
// back to the unwidened `string | number` on the other side of a `queryFn`
// cast, verified in isolation. A single `Omit<>&{}` (two levels, the same
// shape every other override in this file already uses) does not trigger it.
export type RunStatistics = Omit<Generated.RunStatistics, 'totalRuns' | 'completedRuns' | 'failedRuns' | 'canceledRuns' | 'runningRuns' | 'awaitingInputRuns' | 'inputTokens' | 'outputTokens' | 'totalTokens' | 'cachedInputTokens' | 'reasoningTokens' | 'audioInputTokens' | 'audioOutputTokens' | 'byAgent' | 'byModel' | 'byVersion' | 'byUser' | 'byLabel' | 'byErrorClass' | 'totalCost' | 'runsWithUnknownPricing' | 'errorRate' | 'scoredRuns' | 'positiveRate'> & { totalRuns: number; completedRuns: number; failedRuns: number; canceledRuns: number; runningRuns: number; awaitingInputRuns: number; inputTokens: number; outputTokens: number; totalTokens: number; cachedInputTokens: number; reasoningTokens: number; audioInputTokens: number; audioOutputTokens: number; byAgent: RunAgentStatistics[]; byModel: RunModelStatistics[]; byVersion: RunVersionStatistics[]; byUser: RunUserStatistics[]; byLabel: RunLabelStatistics[]; byErrorClass: RunErrorStatistics[]; totalCost: number | null; runsWithUnknownPricing: number; errorRate: number | null; scoredRuns: number; positiveRate: number | null };
export type RunTrace = Omit<Generated.RunTrace, 'spans'> & { spans: TraceSpan[] };
export type RunTreeCost = Fix<Generated.RunTreeCost, 'cachedInputCost' | 'inputCost' | 'outputCost' | 'runsWithUnknownPricing'>;
export type RunUsage = Fix<Generated.RunUsage, 'audioInputTokens' | 'audioOutputTokens' | 'cachedInputTokens' | 'inputTokens' | 'outputTokens' | 'reasoningTokens' | 'totalTokens'>;
export type RunUserStatistics = Fix<Generated.RunUserStatistics, 'failedRuns' | 'totalCost' | 'totalRuns' | 'totalTokens'>;
export type RunVersionStatistics = Fix<Generated.RunVersionStatistics, 'failedRuns' | 'totalRuns' | 'totalTokens' | 'version'>;
export type SearchKnowledgeHit = Fix<Generated.SearchKnowledgeHit, 'chunkIndex' | 'distance'>;
export type SessionBranchResult = Fix<Generated.SessionBranchResult, 'branchFromSequence' | 'copiedItemCount'>;
export type SkillScriptGrant = Fix<Generated.SkillScriptGrant, 'contentHash' | 'grantedAt' | 'id'>;
export type SpeakResponse = Fix<Generated.SpeakResponse, 'characters' | 'cost'> & { attachment: AttachmentDescriptor };
export type TenantDescriptor = Fix<Generated.TenantDescriptor, 'createdAt'>;
export type TimeSeriesPoint = Fix<Generated.TimeSeriesPoint, 'averageDurationMs' | 'cost' | 'failedRuns' | 'inputTokens' | 'outputTokens' | 'runs'>;
export type ToolApprovalRule = Fix<Generated.ToolApprovalRule, 'argumentConditions'>;
export type ToolCallContentBase = Fix<Generated.ToolCallContentBase, 'additionalProperties' | 'annotations'>;
export type ToolCallContentFunctionCallContent = Fix<Generated.ToolCallContentFunctionCallContent, 'additionalProperties' | 'annotations' | 'arguments' | 'informationalOnly'>;
export type ToolCallUsage = Fix<Generated.ToolCallUsage, 'cost' | 'isEstimated' | 'quantity'>;
export type ToolDescriptor = Fix<Generated.ToolDescriptor, 'effect' | 'requiresApproval' | 'runsOnClient'>;
export type ToolInvocationRecord = Fix<Generated.ToolInvocationRecord, 'authorizationDenied' | 'succeeded' | 'timedOut'> & { usage: ToolCallUsage | null };
export type ToolUsage = Fix<Generated.ToolUsage, 'averageDurationMs' | 'errorRate' | 'failedCalls' | 'totalCalls'>;
export type TraceSpan = Fix<Generated.TraceSpan, 'attributes' | 'kind' | 'status'>;
export type UploadDocumentResponse = Fix<Generated.UploadDocumentResponse, 'chunkCount'>;
export type UsageDetails = Fix<Generated.UsageDetails, 'cachedInputTokenCount' | 'inputAudioTokenCount' | 'inputTextTokenCount' | 'inputTokenCount' | 'outputAudioTokenCount' | 'outputTextTokenCount' | 'outputTokenCount' | 'reasoningTokenCount' | 'totalTokenCount'>;
export type VoiceHealth = Fix<Generated.VoiceHealth, 'voiceCount'>;
export type VoiceSessionRecord = Fix<Generated.VoiceSessionRecord, 'inputSeconds' | 'outputChars' | 'turns'>;
export type WebhookDelivery = Fix<Generated.WebhookDelivery, 'attempt' | 'responseCode'>;
export type WebhookSubscription = Fix<Generated.WebhookSubscription, 'consecutiveFailures' | 'enabled' | 'headerConfigurationKeys' | 'headers'>;
// `Generated.WorkflowKind` carries `| null` because `WorkflowDescriptor.kind`
// uses it nullably elsewhere in the document — narrowed here for call sites
// (KIND_HINT lookups, the editor's fixed kind list) that only ever see one of
// the five real values, the same shared-nullable-enum pattern as RunErrorClass.
export type WorkflowKind = Exclude<Generated.WorkflowKind, null>;
export type WorkflowDefinition = Fix<Generated.WorkflowDefinition, 'agentNames' | 'maxIterations' | 'nodes' | 'requirePlanApproval' | 'version'> & { kind: WorkflowKind };
export type WorkflowDescriptor = Fix<Generated.WorkflowDescriptor, 'agentNames' | 'nodes' | 'version'>;
export type WorkflowGraph = Fix<Generated.WorkflowGraph, 'edges' | 'nodes'>;
export type WorkflowPendingRequest = Fix<Generated.WorkflowPendingRequest, 'requestedAt'>;
