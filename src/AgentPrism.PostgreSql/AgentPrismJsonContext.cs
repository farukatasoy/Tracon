using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// <c>jsonb</c> alanlarina yazilan tiplerin kaynak ureteci baglami.
/// </summary>
/// <remarks>
/// <para>
/// <c>JsonSerializer.Serialize(object)</c> gibi yansimaya dayanan asiri yuklemeler
/// <c>IL2026</c> ve <c>IL3050</c> uretir. <c>AgentPrism.PostgreSql</c> AOT uyumlu
/// isaretlidir ve bu tanilar build'i kirar; bu yuzden serilestirilen her tip
/// burada bildirilir ve cagrilar <c>JsonTypeInfo</c> alan asiri yuklemeleri kullanir.
/// </para>
/// <para>
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentDefinitionPayload))]
[JsonSerializable(typeof(ChatHistoryState))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class AgentPrismJsonContext : JsonSerializerContext;
