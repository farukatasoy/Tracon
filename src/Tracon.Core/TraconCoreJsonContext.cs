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

// Conversation branching: reads the conversation identifier from the
// session state bag and writes it to the new session.
[JsonSerializable(typeof(ChatHistoryState))]

// The ModelFallbackUsed run-event payload.
[JsonSerializable(typeof(ModelFallbackUsedEventPayload))]

// The StructuredResponseRejected run-event payload.
[JsonSerializable(typeof(StructuredResponseRejectedEventPayload))]

// The StructuredResponseRepairAttempted run-event payload.
[JsonSerializable(typeof(StructuredResponseRepairAttemptedEventPayload))]

// The RunAwaitingInput run-event payload for a root run closing
// with RunStatus.AwaitingApproval.
[JsonSerializable(typeof(IReadOnlyList<PendingToolApprovalEventItem>))]

// The RunAwaitingInput run-event payload for a root run closing
// with RunStatus.AwaitingInput because a tool asked for user input.
[JsonSerializable(typeof(IReadOnlyList<PendingToolInputEventItem>))]

// The identities of the calls a suspended run left without a result, kept in the session.
[JsonSerializable(typeof(List<string>))]

// The ChildRunTimedOut run-event payload.
[JsonSerializable(typeof(ChildRunTimedOutEventPayload))]

// The quota threshold notice's Custom run-event payload.
[JsonSerializable(typeof(QuotaThresholdNoticePayload))]

// The output truncation notice's Custom run-event payload.
[JsonSerializable(typeof(OutputTruncatedEventPayload))]

// The output filter notice's Custom run-event payload.
[JsonSerializable(typeof(OutputFilteredEventPayload))]

// The LoopIterationCompleted run-event payload.
[JsonSerializable(typeof(LoopIterationCompletedEventPayload))]
internal sealed partial class TraconCoreJsonContext : JsonSerializerContext;
