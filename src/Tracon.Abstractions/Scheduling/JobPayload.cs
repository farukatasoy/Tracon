using System.Text.Json;

namespace Tracon;

/// <summary>
/// Extracts the job item input list from the free-form JSON in
/// <see cref="JobRecord.Payload"/>/<see cref="JobSchedule.Payload"/>.
/// </summary>
/// <remarks>
/// Both the background worker (when producing a job from a schedule) and the
/// HTTP layer (on manual trigger) must use the same interpretation; this is
/// why it is defined in a single place.
/// </remarks>
public static class JobPayload
{
    /// <summary>
    /// Converts the payload into a job item input list.
    /// </summary>
    /// <param name="payload">The free-form JSON payload.</param>
    /// <returns>
    /// If the payload is a JSON array, each element's text (directly if a
    /// string, otherwise its raw JSON text); otherwise the payload itself
    /// returned as a single-item list. An empty list if the payload is undefined.
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
