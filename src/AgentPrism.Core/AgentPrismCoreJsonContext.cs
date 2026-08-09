using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// <c>AgentPrism.Core</c> icinde serilestirilen tiplerin kaynak ureteci baglami.
/// </summary>
/// <remarks>
/// Yansimaya dayanan <c>JsonSerializer</c> asiri yuklemeleri <c>IL2026</c> ve
/// <c>IL3050</c> uretir; <c>AgentPrism.Core</c> AOT uyumlu isaretlidir ve bu
/// tanilar build'i kirar. Serilestirilen her tip burada bildirilir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
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

// Konusma dallandirmasi (Faz 47): oturumun durum cantasindaki konusma kimligi
// okunur ve yeni oturuma yazilir.
[JsonSerializable(typeof(ChatHistoryState))]
internal sealed partial class AgentPrismCoreJsonContext : JsonSerializerContext;
