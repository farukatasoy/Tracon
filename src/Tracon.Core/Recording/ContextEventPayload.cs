using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Builds the <see cref="RunEventCustomTypes.Context"/> event: how many of the messages of a model request
/// came from each source.
/// </summary>
/// <remarks>
/// <para>
/// Counts only. The payload never carries a message, its text or its length: it answers how many
/// messages memory or retrieval added to what the model saw, and nothing about their content. That is
/// why it is written with <c>RecordToolPayloads</c> off, like the other notices whose payload names what
/// happened and never what was said.
/// </para>
/// <para>
/// The source is the framework's own attribution on the message
/// (<see cref="ChatMessageExtensions.GetAgentRequestMessageSourceType"/>). The type is kept as the
/// framework's string, so a source type added later is counted under its own name. A message with no
/// attribution is the caller's own and the framework reports it as <c>External</c>.
/// </para>
/// </remarks>
internal static class ContextEventPayload
{
    /// <summary>Describes the source breakdown of one model request as a reserved <c>Custom</c> event draft.</summary>
    /// <param name="messages">The messages of the request.</param>
    /// <returns>The draft to append with the writer's reserved path.</returns>
    public static RunEventDraft Describe(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var counts = new Dictionary<(string Type, string Id), int>();

        foreach (var message in messages)
        {
            var key = (
                message.GetAgentRequestMessageSourceType().Value ?? string.Empty,
                message.GetAgentRequestMessageSourceId() ?? string.Empty);

            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        // A fixed order, so two runs of one request give one payload: by type, then by identifier.
        var sources = counts
            .OrderBy(static pair => pair.Key.Type, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Id, StringComparer.Ordinal)
            .ToList();

        var payload = ContentEventPayload.Build(writer =>
        {
            ContentEventPayload.WriteString(writer, "type", "context");
            writer.WriteStartArray("sources");

            foreach (var (key, count) in sources)
            {
                writer.WriteStartObject();
                ContentEventPayload.WriteString(writer, "type", key.Type);

                if (key.Id.Length > 0)
                {
                    ContentEventPayload.WriteString(writer, "id", key.Id);
                }

                writer.WriteNumber("count", count);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

        return new RunEventDraft(RunEventType.Custom)
        {
            CustomType = RunEventCustomTypes.Context,
            Text = $"context: {messages.Count} messages",
            Payload = payload,
        };
    }
}
