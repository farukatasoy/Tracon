using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The source-generated context for types serialized in <c>AgentPrism.Core</c>.
/// </summary>
/// <remarks>
/// Reflection-based <c>JsonSerializer</c> overloads produce <c>IL2026</c> and
/// <c>IL3050</c>. <c>AgentPrism.Core</c> is marked as AOT-compatible, and these
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
internal sealed partial class AgentPrismCoreJsonContext : JsonSerializerContext;
