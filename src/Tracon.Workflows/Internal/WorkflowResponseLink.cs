using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Writes the payload of a <see cref="RunEventType.WorkflowResponseApplied"/> event: the run
/// that waited and the request the new run answers.
/// </summary>
/// <remarks>
/// The answer itself is never part of it. The run event stream is readable by the client,
/// and an answer can hold anything the person typed; the decision record lives in
/// <see cref="IWorkflowResponseStore"/>, as a hash.
/// </remarks>
internal static class WorkflowResponseLink
{
    /// <summary>Writes the payload.</summary>
    /// <param name="sourceRunId">The run that waited for the answer.</param>
    /// <param name="requestId">The request the answer is for.</param>
    /// <returns><c>{"sourceRunId":"…","requestId":"…"}</c>.</returns>
    public static string Write(Guid sourceRunId, string requestId)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceRunId", sourceRunId);
            writer.WriteString("requestId", requestId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Reads the run that waited from a <see cref="RunEventType.WorkflowResponseApplied"/> payload.</summary>
    /// <param name="payload">The payload <see cref="Write"/> produced.</param>
    /// <returns>The run that waited, or <see langword="null"/> when the payload names none.</returns>
    public static Guid? ReadSourceRunId(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement is { ValueKind: JsonValueKind.Object } root &&
                   root.TryGetProperty("sourceRunId", out var source) &&
                   source.ValueKind == JsonValueKind.String &&
                   source.TryGetGuid(out var sourceRunId)
                ? sourceRunId
                : null;
        }
        catch (JsonException)
        {
            // The payload is cut at the recording limit like every other; a link that cannot be read is no link.
            return null;
        }
    }
}
