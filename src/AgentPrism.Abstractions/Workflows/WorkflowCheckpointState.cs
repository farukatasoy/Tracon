using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Kontrol noktasi durumu icin paylasilan sabit degerler.
/// </summary>
/// <remarks>
/// <see cref="WorkflowCheckpointRecord.State"/> zorunlu bir alandir, ancak
/// listeleme sorgulari yuku bilerek okumaz: bir kontrol noktasi kilobaytlarca
/// opak JSON tasir ve listeye eklemek arayuzu kullanilamaz hale getirirdi.
/// Bu tip, "yuk bu kayitta yok" durumunu <see langword="null"/> yerine acik bir
/// degerle anlatir - alanin nullable yapilmasi, gercek bir okumada bos gelen
/// yuku sessizce kabul etmek anlamina gelirdi.
/// </remarks>
public static class WorkflowCheckpointState
{
    /// <summary>Listeleme sorgusunda durum yukunun okunmadigini belirten deger.</summary>
    public static JsonElement Omitted { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Verilen durumun okunmamis bir yer tutucu olup olmadigini bildirir.</summary>
    /// <param name="state">Denetlenecek durum.</param>
    /// <returns>Yuk okunmamissa <see langword="true"/>.</returns>
    public static bool IsOmitted(JsonElement state)
        => state.ValueKind == JsonValueKind.Object && !state.EnumerateObject().Any();
}
