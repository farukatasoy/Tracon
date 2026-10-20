using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Reads which kind of hosted tool a piece of model content belongs to, and which call it is.
/// </summary>
/// <remarks>
/// <para>
/// The one place that knows the content types of the hosted tools: the cost ring and the content event
/// payload ask it, so a type added here reaches both.
/// </para>
/// <para>
/// A web search and a code interpreter have content types of their own. A file search has none, and the
/// way to find it was measured (the OpenAI Responses adapter, decompiled and parsed over the wire): the
/// adapter returns a base <see cref="ToolCallContent"/> with NO raw representation, followed in the same
/// message or update by a base <see cref="ToolResultContent"/> whose raw representation is the provider's
/// <c>FileSearchCallResponseItem</c>. The call alone is not enough: the same adapter returns other base
/// calls (a computer call), but those carry their raw item. Core cannot name a provider SDK type, so the
/// result's raw type is matched by its name, and the call is the one with the same identifier in the same
/// batch.
/// </para>
/// </remarks>
internal static class HostedToolContent
{
    private const string FileSearchRawTypeName = "FileSearchCallResponseItem";

    /// <summary>
    /// Finds the file search calls of one message or update: the identifiers of the file search results it
    /// carries.
    /// </summary>
    /// <param name="batch">The contents of one message or one streamed update.</param>
    /// <returns>The call identifiers, or <see langword="null"/> when the batch carries no file search result.</returns>
    public static HashSet<string>? FileSearchCallIds(IEnumerable<AIContent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        HashSet<string>? ids = null;

        foreach (var content in batch)
        {
            if (content is ToolResultContent result && IsFileSearchResult(result) && !string.IsNullOrEmpty(result.CallId))
            {
                (ids ??= new HashSet<string>(StringComparer.Ordinal)).Add(result.CallId);
            }
        }

        return ids;
    }

    /// <summary>Reads the kind and call identifier of a hosted tool call.</summary>
    /// <param name="content">The content.</param>
    /// <param name="fileSearchCallIds">The file search calls of the content's batch (<see cref="FileSearchCallIds"/>).</param>
    /// <param name="kind">The kind of the tool.</param>
    /// <param name="callId">The call identifier the provider assigned.</param>
    /// <returns><see langword="true"/> when the content is the call of a hosted tool of a known kind.</returns>
    public static bool TryGetCall(
        AIContent content,
        HashSet<string>? fileSearchCallIds,
        out HostedToolKind kind,
        out string callId)
    {
        switch (content)
        {
            case WebSearchToolCallContent webSearch:
                (kind, callId) = (HostedToolKind.WebSearch, webSearch.CallId);
                break;

            case CodeInterpreterToolCallContent interpreter:
                (kind, callId) = (HostedToolKind.CodeInterpreter, interpreter.CallId);
                break;

            case ToolCallContent call
                when call.GetType() == typeof(ToolCallContent)
                    && call.RawRepresentation is null
                    && fileSearchCallIds is not null
                    && fileSearchCallIds.Contains(call.CallId):
                (kind, callId) = (HostedToolKind.FileSearch, call.CallId);
                break;

            default:
                kind = default;
                callId = string.Empty;

                return false;
        }

        return !string.IsNullOrEmpty(callId);
    }

    /// <summary>Gets whether a base tool result is the file search result of the Responses API.</summary>
    /// <param name="result">The result content.</param>
    /// <returns><see langword="true"/> for the provider's file search call item.</returns>
    public static bool IsFileSearchResult(ToolResultContent result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.GetType() == typeof(ToolResultContent)
            && result.RawRepresentation is { } raw
            && string.Equals(raw.GetType().Name, FileSearchRawTypeName, StringComparison.Ordinal);
    }
}
