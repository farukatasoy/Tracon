using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Marks an OpenAI Responses answer <c>incomplete</c> the way OpenAI's own API does for an answer cut off at
/// <c>max_output_tokens</c> or stopped by its content filter: the response <c>status</c> and
/// <c>incomplete_details</c>, and the status of the message item that was cut.
/// </summary>
/// <remarks>
/// <para>
/// <c>Microsoft.Agents.AI.Hosting.OpenAI</c> writes the answer and never reads the finish reason (measured,
/// 1.25.0-alpha.261014.1): every reason ends in <c>status: "completed"</c>, and a stream ends in
/// <c>response.completed</c>. The finish reason comes from the run instead
/// (<see cref="OpenAICompatSupport.ToResponsesIncompleteReason"/>, the mapping the Chat Completions endpoint's
/// <c>finish_reason</c> shares its predicates with) and is applied to what MAF wrote.
/// </para>
/// <para>
/// Every rewrite applies to a <c>completed</c> body only. If MAF starts marking the answer itself, the body
/// is no longer <c>completed</c>, nothing here fires, and MAF's own reason stands.
/// </para>
/// </remarks>
internal static class ResponsesIncompleteness
{
    internal const string CompletedStatus = "completed";
    internal const string IncompleteStatus = "incomplete";

    /// <summary>Marks a response object as incomplete.</summary>
    /// <param name="response">The <c>response</c> object MAF wrote, on a JSON body or inside a terminal stream event.</param>
    /// <param name="reason">The <c>incomplete_details.reason</c>.</param>
    /// <returns>
    /// <see langword="true"/> when the response was <c>completed</c> and is now incomplete;
    /// <see langword="false"/> when it was left as it was.
    /// </returns>
    /// <remarks>
    /// The last <c>message</c> item of <c>output</c> is the answer that was cut, so only it is marked:
    /// OpenAI marks the cut message and leaves the tool items before it alone.
    /// </remarks>
    internal static bool Mark(JsonObject response, string reason)
    {
        if (!string.Equals(StatusOf(response), CompletedStatus, StringComparison.Ordinal))
        {
            return false;
        }

        response["status"] = IncompleteStatus;
        response["incomplete_details"] = new JsonObject { ["reason"] = reason };

        if (response["output"] is JsonArray output)
        {
            for (var index = output.Count - 1; index >= 0; index--)
            {
                if (output[index] is JsonObject item && string.Equals(TextOf(item["type"]), "message", StringComparison.Ordinal))
                {
                    MarkItem(item);
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>Marks an output item as incomplete when it is <c>completed</c>.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> when the item changed.</returns>
    internal static bool MarkItem(JsonObject item)
    {
        if (!string.Equals(StatusOf(item), CompletedStatus, StringComparison.Ordinal))
        {
            return false;
        }

        item["status"] = IncompleteStatus;

        return true;
    }

    /// <summary>Reads the <c>status</c> text of an object, or <see langword="null"/> when it has none.</summary>
    /// <param name="node">A response object or an output item.</param>
    /// <returns>The status.</returns>
    internal static string? StatusOf(JsonObject node) => TextOf(node["status"]);

    /// <summary>Reads a node as text without throwing when it is a different kind of value.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text, or <see langword="null"/> when the node is not a string.</returns>
    internal static string? TextOf(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// Rewrites the closing frames of one streamed Responses answer when the run says the answer is incomplete.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Position.</strong> MAF writes the closing frames of the last output item
/// (<c>response.output_item.done</c>) and <c>response.completed</c> only after the update stream has ended;
/// an earlier item (text before a tool call) is closed while updates still arrive (measured,
/// 1.25.0-alpha.261014.1). So the frames written after the source ended are the ones that belong to the
/// answer that was cut, and by then the last finish reason is known. A frame written earlier is never
/// touched, and neither is any frame of a complete answer: those pass through as the same string, unparsed.
/// </para>
/// <para>
/// <strong>What changes.</strong> The closing message item becomes <c>incomplete</c>, and
/// <c>response.completed</c> becomes <c>response.incomplete</c> with the response marked by
/// <see cref="ResponsesIncompleteness.Mark"/>. The numbering of the frames is not touched. A frame this
/// type cannot read passes through as MAF wrote it and <see cref="MissedTerminalFrame"/> says so, so the
/// endpoint can log that the status stayed <c>completed</c>.
/// </para>
/// <para>
/// <strong>A limit.</strong> A message item MAF had already closed before the source ended keeps
/// <c>completed</c> in its own <c>response.output_item.done</c> frame: the finish reason was not known
/// yet, and a frame written earlier is never touched. MAF closes an item when it starts the next one; the
/// measured case is another kind of content that follows the text (a Gemini stream that reports its
/// reasoning after the answer). The terminal event is the source of truth: its <c>output</c> marks the
/// last message item.
/// </para>
/// <para>One instance serves one response; it is not shared.</para>
/// </remarks>
internal sealed class ResponsesStreamRewriter
{
    private const string DataPrefix = "data: ";
    private const string ItemDoneLine = "event: response.output_item.done\n";
    private const string CompletedLine = "event: response.completed\n";
    private const string IncompleteLine = "event: response.incomplete\n";
    private const string IncompleteType = "response.incomplete";

    private string? _reason;

    /// <summary>
    /// Gets whether the answer is incomplete and no terminal frame was found to say so: the status of the
    /// stream stayed as MAF wrote it. It is <see langword="true"/> from the moment the source ends with an
    /// incomplete answer until a terminal frame is rewritten, or found already marked.
    /// </summary>
    internal bool MissedTerminalFrame { get; private set; }

    /// <summary>Passes the updates on unchanged and remembers the last finish reason they reported.</summary>
    /// <param name="updates">The updates the run produced.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>The same updates.</returns>
    /// <remarks>
    /// The LAST reported reason is the answer's: the tool loop streams a tool turn's update with that turn's
    /// own reason before the next turn (the rule the run's detector and the Chat Completions stream apply).
    /// The decision is taken when the source ends, which is also when frames start to be rewritable.
    /// </remarks>
    internal async IAsyncEnumerable<AgentResponseUpdate> Observe(
        IAsyncEnumerable<AgentResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatFinishReason? finishReason = null;

        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (update.FinishReason is { } reported)
            {
                finishReason = reported;
            }

            yield return update;
        }

        _reason = OpenAICompatSupport.ToResponsesIncompleteReason(finishReason);
        MissedTerminalFrame = _reason is not null;
    }

    /// <summary>Returns the frame to write for a frame MAF produced.</summary>
    /// <param name="frame">A complete SSE frame: <c>event:</c> line, <c>data:</c> line and the blank line.</param>
    /// <returns>The same string, or the rewritten frame.</returns>
    internal string Patch(string frame)
    {
        if (_reason is not { } reason)
        {
            return frame;
        }

        if (frame.StartsWith(ItemDoneLine, StringComparison.Ordinal))
        {
            return RewriteClosingItem(frame);
        }

        if (frame.StartsWith(CompletedLine, StringComparison.Ordinal))
        {
            return RewriteTerminal(frame, reason);
        }

        if (frame.StartsWith(IncompleteLine, StringComparison.Ordinal))
        {
            // MAF marked the answer itself.
            MissedTerminalFrame = false;
        }

        return frame;
    }

    private static string RewriteClosingItem(string frame)
    {
        // Best effort: the terminal frame carries the response-level truth, so an item frame this type
        // cannot read is not reported.
        if (!TryReadData(frame, ItemDoneLine.Length, out var body, out var dataEnd)
            || body["item"] is not JsonObject item
            || !string.Equals(ResponsesIncompleteness.TextOf(item["type"]), "message", StringComparison.Ordinal)
            || !ResponsesIncompleteness.MarkItem(item))
        {
            return frame;
        }

        return string.Concat(ItemDoneLine, DataPrefix, body.ToJsonString(), frame.AsSpan(dataEnd));
    }

    private string RewriteTerminal(string frame, string reason)
    {
        if (!TryReadData(frame, CompletedLine.Length, out var body, out var dataEnd)
            || body["response"] is not JsonObject response)
        {
            return frame;
        }

        if (!ResponsesIncompleteness.Mark(response, reason))
        {
            // The body is not a completed one: MAF marked the answer itself, or failed it. A body with no
            // status at all is not a body this type understands.
            MissedTerminalFrame = ResponsesIncompleteness.StatusOf(response) is null;

            return frame;
        }

        body["type"] = IncompleteType;
        MissedTerminalFrame = false;

        return string.Concat(IncompleteLine, DataPrefix, body.ToJsonString(), frame.AsSpan(dataEnd));
    }

    /// <summary>Reads the JSON object of the <c>data:</c> line that follows the <c>event:</c> line.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="eventLineLength">The length of the <c>event:</c> line, with its line feed.</param>
    /// <param name="body">The parsed object.</param>
    /// <param name="dataEnd">The index of the line feed that ends the data line.</param>
    /// <returns><see langword="false"/> when the frame is not shaped like the ones MAF writes.</returns>
    private static bool TryReadData(string frame, int eventLineLength, out JsonObject body, out int dataEnd)
    {
        body = null!;
        dataEnd = 0;

        if (!frame.AsSpan(eventLineLength).StartsWith(DataPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var dataStart = eventLineLength + DataPrefix.Length;
        dataEnd = frame.IndexOf('\n', dataStart);

        if (dataEnd < 0)
        {
            dataEnd = frame.Length;
        }

        try
        {
            if (JsonNode.Parse(frame.Substring(dataStart, dataEnd - dataStart)) is not JsonObject parsed)
            {
                return false;
            }

            body = parsed;

            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
