using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// The source-generated context for types serialized in <c>Tracon.Core</c>.
/// </summary>
/// <remarks>
/// Reflection-based <c>JsonSerializer</c> overloads produce <c>IL2026</c> and
/// <c>IL3050</c>. <c>Tracon.Core</c> is marked as AOT-compatible, and these
/// diagnostics fail the build. Declare every serialized type here.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(AgentDefinition))]
[JsonSerializable(typeof(McpServerDefinition))]
[JsonSerializable(typeof(TenantDescriptor))]
[JsonSerializable(typeof(ToolApprovalRule))]
[JsonSerializable(typeof(SkillScriptGrant))]
[JsonSerializable(typeof(AgentSkillScriptDefinition))]
[JsonSerializable(typeof(AgentSkillDefinition))]
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(Experiment))]
[JsonSerializable(typeof(CanaryRollbackAuditPayload))]

// Conversation branching (Phase 47): reads the conversation identifier from the
// session state bag and writes it to the new session.
[JsonSerializable(typeof(ChatHistoryState))]

// Phase 62: the ModelFallbackUsed run-event payload.
[JsonSerializable(typeof(ModelFallbackUsedEventPayload))]

// Phase 131: the StructuredResponseRejected run-event payload.
[JsonSerializable(typeof(StructuredResponseRejectedEventPayload))]

// Phase 134: the StructuredResponseRepairAttempted run-event payload.
[JsonSerializable(typeof(StructuredResponseRepairAttemptedEventPayload))]

// Phase 142: the RunAwaitingInput run-event payload for a root run closing
// with RunStatus.AwaitingApproval.
[JsonSerializable(typeof(IReadOnlyList<PendingToolApprovalEventItem>))]

// Phase 144: the ChildRunTimedOut run-event payload.
[JsonSerializable(typeof(ChildRunTimedOutEventPayload))]

// Phase 146: the quota threshold notice's Custom run-event payload.
[JsonSerializable(typeof(QuotaThresholdNoticePayload))]

// Phase 151: the LoopIterationCompleted run-event payload.
[JsonSerializable(typeof(LoopIterationCompletedEventPayload))]
internal sealed partial class TraconCoreJsonContext : JsonSerializerContext;
