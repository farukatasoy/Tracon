using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The source generator context for the types written to <c>jsonb</c> columns.
/// </summary>
/// <remarks>
/// <para>
/// Reflection-based overloads such as <c>JsonSerializer.Serialize(object)</c> produce
/// <c>IL2026</c> and <c>IL3050</c>. <c>Tracon.PostgreSql</c> is marked AOT
/// compatible and those diagnostics break the build; every serialized type is therefore
/// declared here and the calls use the overloads that take a <c>JsonTypeInfo</c>.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentDefinitionPayload))]
[JsonSerializable(typeof(WorkflowDefinitionPayload))]
[JsonSerializable(typeof(ChatHistoryState))]
[JsonSerializable(typeof(ChatMessage))]

// The source/provenance information that the MAF memory providers
// (FileMemoryProvider read path, TodoProvider) add to
// ChatMessage.AdditionalProperties. If it is not declared, the
// polymorphic AdditionalProperties dictionary throws NotSupportedException when it
// meets this type, and nothing catches it — every run that USES the memory
// provider fails silently (no response is ever returned).
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]

// The run input. The list is written to a SINGLE `json` column; the
// `$type` discriminator of the polymorphic content is preserved as the first
// property of the object.
[JsonSerializable(typeof(IReadOnlyList<ChatMessage>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(IReadOnlyList<ExperimentVariant>))]

// The canary policy. It lives in a separate `canary_policy` column and
// is written INDEPENDENTLY of `variants`: SetCanaryPolicyAsync works regardless of
// the state of the experiment.
[JsonSerializable(typeof(CanaryPolicy))]
[JsonSerializable(typeof(Dictionary<string, string>))]

// Array transport. PostgreSQL sends a native array; SQL Server has no
// array parameter and arrays travel as JSON text (unpacked with OPENJSON).
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Guid[]))]

// Argument-level approval conditions. Written to the
// `tool_approval_rules.argument_conditions` jsonb/text column, independent of
// TraconCoreJsonContext's ToolApprovalRule graph.
[JsonSerializable(typeof(IReadOnlyList<ToolArgumentCondition>))]

// The tool-approval presentation. Written to the
// `pending_approvals.presentation` jsonb/text column, resolved once when the
// request is first written.
[JsonSerializable(typeof(ToolApprovalPresentation))]
internal sealed partial class TraconJsonContext : JsonSerializerContext;
