using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
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
[JsonSerializable(typeof(WorkflowDefinitionPayload))]
[JsonSerializable(typeof(ChatHistoryState))]
[JsonSerializable(typeof(ChatMessage))]

// MAF'in bellek saglayicilarinin (FileMemoryProvider okuma yolu, TodoProvider)
// ChatMessage.AdditionalProperties'e ekledigi kaynak/provenance bilgisi
// (HATA-S1-008). Bildirilmezse polimorfik AdditionalProperties sozlugu bu tiple
// karsilastiginda NotSupportedException firlatir ve hicbir yerde yakalanmaz —
// bellek sagayicisini KULLANAN her tur sessizce coker (yanit hic donmez).
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]

// Calistirma girdisi (Faz 47). Liste TEK bir `json` sutununa yazilir; polimorfik
// icerigin `$type` ayraci nesnenin ilk ozelligi olarak korunur (K-027).
[JsonSerializable(typeof(IReadOnlyList<ChatMessage>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(IReadOnlyList<ExperimentVariant>))]

// Kanarya kurali (Faz 56). Ayri bir `canary_policy` sutununda, `variants`'tan
// BAGIMSIZ yazilir: SetCanaryPolicyAsync deneyin durumundan bagimsiz calisir.
[JsonSerializable(typeof(CanaryPolicy))]
[JsonSerializable(typeof(Dictionary<string, string>))]

// Dizi tasima (Faz 23). PostgreSQL yerel dizi gonderir; SQL Server'da dizi
// parametresi yoktur ve diziler JSON metni olarak tasinir (OPENJSON ile acilir).
// Gerekce: docs/KARARLAR.md, karar K-182.
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Guid[]))]
internal sealed partial class AgentPrismJsonContext : JsonSerializerContext;
