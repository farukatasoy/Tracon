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
    /// returned as a single-item list. An empty list if there is no payload.
    /// </returns>
    /// <remarks>
    /// JSON <c>null</c> and <see cref="JsonValueKind.Undefined"/> both mean
    /// "no payload" and both produce an empty list. They have to: a payload
    /// left unset is normalized to JSON <c>null</c> on the way into a record
    /// (see <see cref="FreeFormJson"/>) and the SQL stores write the same
    /// literal, so reading <c>null</c> as a single item whose text is
    /// <c>"null"</c> would turn an empty schedule into a job with one bogus
    /// item.
    /// </remarks>
    public static IReadOnlyList<string> ExtractItems(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
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
