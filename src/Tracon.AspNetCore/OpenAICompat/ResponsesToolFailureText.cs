using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Keeps the text of a failed tool call out of a Responses answer.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework writes a <c>function_call_output</c> item from a
/// <see cref="FunctionResultContent"/> in one place, and that place prefers the exception to the result:
/// when <see cref="FunctionResultContent.Exception"/> is set, the item's <c>output</c> is the exception's
/// type name and its message, <c>InvalidOperationException("Server=db.internal;Password=…")</c>. The model
/// was given <c>Error: Function failed.</c> in its place (the tool loop withholds the detail on purpose),
/// and a tool's own exception can carry a connection string or a path, so the HTTP caller must not be
/// handed more than the model got. Both writers the endpoint uses, the buffered one and the streaming
/// one, go through that same conversion.
/// </para>
/// <para>
/// The content is copied without its exception and the original is left as it was: the run record has
/// already read the failure from it, and the session may hold the same instances. Contents without an
/// exception pass through as the same objects. The copy exists to be written: it does not carry the
/// continuation token (an experimental member the writer does not read).
/// </para>
/// </remarks>
internal static class ResponsesToolFailureText
{
    /// <summary>Returns the response to write: the same instance when no tool result carries an exception.</summary>
    /// <param name="response">The run's response.</param>
    /// <returns>The same response, or a copy whose failed tool results carry no exception.</returns>
    internal static AgentResponse Redact(AgentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<ChatMessage>? messages = null;

        for (var index = 0; index < response.Messages.Count; index++)
        {
            var message = response.Messages[index];

            if (WithoutExceptions(message.Contents) is not { } contents)
            {
                messages?.Add(message);

                continue;
            }

            messages ??= [.. response.Messages.Take(index)];
            messages.Add(new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId,
                RawRepresentation = message.RawRepresentation,
                AdditionalProperties = message.AdditionalProperties,
            });
        }

        if (messages is null)
        {
            return response;
        }

        return new AgentResponse(messages)
        {
            AdditionalProperties = response.AdditionalProperties,
            AgentId = response.AgentId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            RawRepresentation = response.RawRepresentation,
            ResponseId = response.ResponseId,
            Usage = response.Usage,
        };
    }

    /// <summary>Passes the updates on; an update that carries a failed tool result is replaced by a copy without the exception.</summary>
    /// <param name="updates">The updates the run produced.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>The same updates, or copies of those that carried an exception.</returns>
    internal static async IAsyncEnumerable<AgentResponseUpdate> Redact(
        IAsyncEnumerable<AgentResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (WithoutExceptions(update.Contents) is not { } contents)
            {
                yield return update;

                continue;
            }

            yield return new AgentResponseUpdate(update.Role, contents)
            {
                AdditionalProperties = update.AdditionalProperties,
                AgentId = update.AgentId,
                AuthorName = update.AuthorName,
                CreatedAt = update.CreatedAt,
                FinishReason = update.FinishReason,
                MessageId = update.MessageId,
                RawRepresentation = update.RawRepresentation,
                ResponseId = update.ResponseId,
            };
        }
    }

    private static List<AIContent>? WithoutExceptions(IList<AIContent> contents)
    {
        List<AIContent>? copy = null;

        for (var index = 0; index < contents.Count; index++)
        {
            if (contents[index] is FunctionResultContent { Exception: not null } failed)
            {
                copy ??= [.. contents.Take(index)];
                copy.Add(new FunctionResultContent(failed.CallId, failed.Result)
                {
                    Annotations = failed.Annotations,
                    AdditionalProperties = failed.AdditionalProperties,
                    RawRepresentation = failed.RawRepresentation,
                });
            }
            else
            {
                copy?.Add(contents[index]);
            }
        }

        return copy;
    }
}
