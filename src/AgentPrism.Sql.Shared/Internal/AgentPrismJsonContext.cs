using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// The source generator context for the types written to <c>jsonb</c> columns.
/// </summary>
/// <remarks>
/// <para>
/// Reflection-based overloads such as <c>JsonSerializer.Serialize(object)</c> produce
/// <c>IL2026</c> and <c>IL3050</c>. <c>AgentPrism.PostgreSql</c> is marked AOT
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
// ChatMessage.AdditionalProperties (HATA-S1-008). If it is not declared, the
// polymorphic AdditionalProperties dictionary throws NotSupportedException when it
// meets this type, and nothing catches it — every run that USES the memory
// provider fails silently (no response is ever returned).
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]

// The run input (phase 47). The list is written to a SINGLE `json` column; the
// `$type` discriminator of the polymorphic content is preserved as the first
// property of the object (K-027).
[JsonSerializable(typeof(IReadOnlyList<ChatMessage>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(IReadOnlyList<ExperimentVariant>))]

// The canary policy (phase 56). It lives in a separate `canary_policy` column and
// is written INDEPENDENTLY of `variants`: SetCanaryPolicyAsync works regardless of
// the state of the experiment.
[JsonSerializable(typeof(CanaryPolicy))]
[JsonSerializable(typeof(Dictionary<string, string>))]

// Array transport (phase 23). PostgreSQL sends a native array; SQL Server has no
// array parameter and arrays travel as JSON text (unpacked with OPENJSON).
// Rationale: docs/KARARLAR.md, decision K-182.
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Guid[]))]

// Argument-level approval conditions (phase 63). Written to the
// `tool_approval_rules.argument_conditions` jsonb/text column, independent of
// AgentPrismCoreJsonContext's ToolApprovalRule graph.
[JsonSerializable(typeof(IReadOnlyList<ToolArgumentCondition>))]
internal sealed partial class AgentPrismJsonContext : JsonSerializerContext;
