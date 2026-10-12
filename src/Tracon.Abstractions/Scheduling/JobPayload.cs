using System.Text.Json;

namespace Tracon;

/// <summary>
/// Extracts the job item input list from the free-form JSON in
/// <see cref="JobRecord.Payload"/>/<see cref="JobSchedule.Payload"/>, and the
/// attribution a batch payload carries beside its items.
/// </summary>
/// <remarks>
/// <para>
/// Both the background worker (when producing a job from a schedule) and the
/// HTTP layer (on manual trigger) must use the same interpretation; this is
/// why it is defined in a single place.
/// </para>
/// <para>
/// <strong>The attribution envelope.</strong> For the two built-in
/// handlers that run batch items (<see cref="JobHandlerKeys.Workflow"/> and
/// <see cref="JobHandlerKeys.AgentBatch"/>), a payload that is a JSON object
/// whose members are all among <c>items</c>, <c>userId</c> and <c>labels</c> is
/// an envelope: <c>{"items": [...], "userId": "...", "labels": {"team": "..."}}</c>.
/// Its items are the <c>items</c> array, read exactly like a top-level array, and
/// the worker runs them with that user and those labels. Every other payload
/// keeps its meaning, and so does every payload of every other handler key.
/// </para>
/// <para>
/// The worker does not see the HTTP request that queued a job, so the identity
/// has to travel in the job itself, as it does in the agent run job's
/// <c>userId</c>. The field names are the ones
/// <see cref="AmbientRunAttributionScope.Begin(string?, IReadOnlyDictionary{string, string}?)"/>
/// takes.
/// </para>
/// </remarks>
internal static class JobPayload
{
    private const string ItemsMember = "items";
    private const string UserIdMember = "userId";
    private const string LabelsMember = "labels";

    private static readonly string[] EnvelopeMembers = [ItemsMember, UserIdMember, LabelsMember];

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
    /// <para>
    /// JSON <c>null</c> and <see cref="JsonValueKind.Undefined"/> both mean
    /// "no payload" and both produce an empty list. They have to: a payload
    /// left unset is normalized to JSON <c>null</c> on the way into a record
    /// (see <see cref="FreeFormJson"/>) and the SQL stores write the same
    /// literal, so reading <c>null</c> as a single item whose text is
    /// <c>"null"</c> would turn an empty schedule into a job with one bogus
    /// item.
    /// </para>
    /// <para>
    /// This reading knows no envelope. A caller that knows the handler key
    /// reads through <see cref="Read"/> instead.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ExtractItems(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        if (payload.ValueKind == JsonValueKind.Array)
        {
            return ItemsOf(payload);
        }

        return [payload.ValueKind == JsonValueKind.String ? payload.GetString()! : payload.GetRawText()];
    }

    /// <summary>
    /// Gets whether jobs under a handler key read the attribution envelope.
    /// </summary>
    /// <param name="handlerKey">The handler key.</param>
    /// <returns><see langword="true"/> for the built-in workflow and agent batch keys.</returns>
    public static bool CarriesAttribution(string handlerKey)
        => handlerKey is JobHandlerKeys.Workflow or JobHandlerKeys.AgentBatch;

    /// <summary>
    /// Reads a payload as the handler it is queued for reads it: its items and,
    /// for an envelope, its attribution.
    /// </summary>
    /// <param name="handlerKey">The handler the payload is for.</param>
    /// <param name="payload">The free-form JSON payload.</param>
    /// <returns>
    /// What the payload carries. A malformed envelope carries no items and no
    /// attribution, and <see cref="JobPayloadContent.Problem"/> says why.
    /// </returns>
    public static JobPayloadContent Read(string handlerKey, JsonElement payload)
    {
        ArgumentNullException.ThrowIfNull(handlerKey);

        if (!CarriesAttribution(handlerKey) || !IsEnvelope(payload))
        {
            return new JobPayloadContent { Items = ExtractItems(payload) };
        }

        IReadOnlyList<string> items = [];

        if (payload.TryGetProperty(ItemsMember, out var itemsElement) && itemsElement.ValueKind != JsonValueKind.Null)
        {
            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                return Malformed("'items' must be an array");
            }

            items = ItemsOf(itemsElement);
        }

        string? userId = null;

        if (payload.TryGetProperty(UserIdMember, out var userElement) && userElement.ValueKind != JsonValueKind.Null)
        {
            if (userElement.ValueKind != JsonValueKind.String)
            {
                return Malformed("'userId' must be a string");
            }

            userId = userElement.GetString() is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;
        }

        Dictionary<string, string>? labels = null;

        if (payload.TryGetProperty(LabelsMember, out var labelsElement) && labelsElement.ValueKind != JsonValueKind.Null)
        {
            if (labelsElement.ValueKind != JsonValueKind.Object)
            {
                return Malformed("'labels' must be an object of string values");
            }

            labels = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var label in labelsElement.EnumerateObject())
            {
                if (label.Value.ValueKind != JsonValueKind.String)
                {
                    return Malformed($"the value of label '{label.Name}' is not a string");
                }

                labels[label.Name] = label.Value.GetString()!;
            }
        }

        // Refused whole, never trimmed: the same limits, and the same reason,
        // as every endpoint that starts a run (RunLabels).
        if (RunLabels.Validate(userId, labels) is { } limit)
        {
            return new JobPayloadContent { Items = [], Problem = $"The payload's attribution is not valid: {limit}" };
        }

        return new JobPayloadContent
        {
            Items = items,
            UserId = userId,
            Labels = labels is { Count: > 0 } ? labels : null,
        };

        static JobPayloadContent Malformed(string reason)
            => new() { Items = [], Problem = $"The payload is an attribution envelope, but {reason}." };
    }

    /// <summary>Writes an attribution envelope.</summary>
    /// <param name="items">The job's items, each written as a JSON string.</param>
    /// <param name="userId">The user, or <see langword="null"/>.</param>
    /// <param name="labels">The labels, or <see langword="null"/>.</param>
    /// <returns>The envelope; <see cref="Read"/> reads the same items and attribution back.</returns>
    /// <remarks>Written by hand with <see cref="Utf8JsonWriter"/>, so it stays AOT-compatible.</remarks>
    public static JsonElement Envelope(
        IReadOnlyList<string> items,
        string? userId,
        IReadOnlyDictionary<string, string>? labels)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray(ItemsMember);

            foreach (var item in items)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();

            if (userId is not null)
            {
                writer.WriteString(UserIdMember, userId);
            }

            if (labels is { Count: > 0 })
            {
                writer.WriteStartObject(LabelsMember);

                foreach (var (key, value) in labels)
                {
                    writer.WriteString(key, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        buffer.Position = 0;
        using var document = JsonDocument.Parse(buffer);

        return document.RootElement.Clone();
    }

    private static bool IsEnvelope(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var any = false;

        foreach (var member in payload.EnumerateObject())
        {
            if (!EnvelopeMembers.Contains(member.Name, StringComparer.Ordinal))
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    private static List<string> ItemsOf(JsonElement array)
    {
        var items = new List<string>();

        foreach (var element in array.EnumerateArray())
        {
            items.Add(element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText());
        }

        return items;
    }
}

/// <summary>What a job payload carries once it is read for its handler (<see cref="JobPayload.Read"/>).</summary>
internal sealed record JobPayloadContent
{
    /// <summary>Gets the item inputs.</summary>
    public required IReadOnlyList<string> Items { get; init; }

    /// <summary>Gets the user the runs are attributed to, or <see langword="null"/>.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the labels of the runs, or <see langword="null"/>.</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>
    /// Gets why an attribution envelope is malformed or breaks a limit, or
    /// <see langword="null"/> when it is sound. Never contains a value.
    /// </summary>
    public string? Problem { get; init; }

    /// <summary>Gets whether the payload names a user or at least one label.</summary>
    public bool HasAttribution => UserId is not null || Labels is { Count: > 0 };
}
