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
}
