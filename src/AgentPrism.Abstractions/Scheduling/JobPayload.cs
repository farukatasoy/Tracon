using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <see cref="JobRecord.Payload"/>/<see cref="JobSchedule.Payload"/> icindeki
/// serbest JSON'dan is ogesi girdi listesini cikarir.
/// </summary>
/// <remarks>
/// Hem arka plan iscisi (zamanlamadan is uretirken) hem de HTTP katmani
/// (elle tetiklemede) ayni yorumlamayi kullanmalidir; bu yuzden tek bir yerde
/// tanimlidir.
/// </remarks>
public static class JobPayload
{
    /// <summary>
    /// Yuku is ogesi girdi listesine cevirir.
    /// </summary>
    /// <param name="payload">Serbest JSON yuku.</param>
    /// <returns>
    /// Yuk bir JSON dizisiyse her ogenin metni (dize ise dogrudan, degilse
    /// ham JSON metni); degilse yukun kendisini tek ogeli bir liste olarak
    /// dondurur. Yuk tanimsizsa bos liste doner.
    /// </returns>
    public static IReadOnlyList<string> ExtractItems(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        if (payload.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();

            foreach (var element in payload.EnumerateArray())
            {
                items.Add(element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText());
            }

            return items;
        }

        return [payload.ValueKind == JsonValueKind.String ? payload.GetString()! : payload.GetRawText()];
    }
}
