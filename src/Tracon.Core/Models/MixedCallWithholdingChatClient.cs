using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Sits right under the tool-call loop. When one model response asks for a tool that runs on the server and for a
/// declaration-only tool (one that runs on the client) at the same time, it withholds the declaration-only calls from
/// that response, so the loop runs the server calls and the model asks for the client call again once it has read
/// their results.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why it exists.</strong> The loop stops at the first declaration-only call it meets and returns every call
/// of that iteration to the caller unexecuted. A server call that shared the response never runs, and the next request
/// carries the client's result for the one call it can answer: the server call is left without a result and a strict
/// provider refuses the history with a 400. The framework has a switch for this (<c>EnableInvocableFunctionBypassing</c>),
/// but it covers only <c>ChatClientAgent</c>, not a harness agent, and in a stream it hands the server call and its
/// result to the caller at the same moment, so the tool's row records no duration. Withholding the call here covers
/// every agent that is built over the pipeline and leaves the stream's timing as it was.
/// </para>
/// <para>
/// <strong>What it costs.</strong> One more model call: the model asks for the client call a second time. It may ask with
/// other arguments, and the parallel intent of the first answer is lost. A response that is not mixed (only server
/// calls, only client calls, no calls) is handed on as the same object.
/// </para>
/// <para>
/// <strong>Which tool is a client tool.</strong> The one the request declares as an <see cref="AIFunctionDeclaration"/>
/// that is not an <see cref="AIFunction"/>, which is what the loop itself looks at. A call to a name the request does not
/// declare is neither kind and stays.
/// </para>
/// <para>
/// A decorator never changes the objects its inner client returned: a client that caches its answers would hand the
/// changed object out again. A mixed response is rebuilt from copies; the objects that carry no withheld call pass
/// through as they are.
/// </para>
/// </remarks>
internal sealed class MixedCallWithholdingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (ToolKinds.Of(options) is not { } kinds)
        {
            return response;
        }

        return kinds.IsMixed(response.Messages.SelectMany(static message => message.Contents))
            ? Without(response, kinds)
            : response;
    }

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => ToolKinds.Of(options) is { } kinds
            ? StreamAsync(messages, options, kinds, cancellationToken)
            : base.GetStreamingResponseAsync(messages, options, cancellationToken);

    private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        ToolKinds kinds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Whether the response mixes the two kinds is known only when it ends, and a call can arrive in any update. Updates
        // go out as they come until the first one that carries a call; from there on they are held until this one model
        // response ends. The loop runs its tools only after the response has ended, so holding them delays nothing it does.
        List<ChatResponseUpdate>? held = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (held is null && !update.Contents.Any(static content => content is FunctionCallContent))
            {
                yield return update;

                continue;
            }

            (held ??= []).Add(update);
        }

        if (held is null)
        {
            yield break;
        }

        var mixed = kinds.IsMixed(held.SelectMany(static update => update.Contents));

        foreach (var update in held)
        {
            yield return mixed ? Without(update, kinds) : update;
        }
    }

    // A message that held only withheld calls is dropped: an assistant message with no content is not a valid history entry.
    private static ChatResponse Without(ChatResponse response, ToolKinds kinds)
    {
        var messages = new List<ChatMessage>(response.Messages.Count);

        foreach (var message in response.Messages)
        {
            if (!message.Contents.Any(kinds.IsWithheld))
            {
                messages.Add(message);

                continue;
            }

            var copy = message.Clone();
            copy.Contents = [.. message.Contents.Where(content => !kinds.IsWithheld(content))];

            if (copy.Contents.Count > 0)
            {
                messages.Add(copy);
            }
        }

        return new ChatResponse(messages)
        {
            AdditionalProperties = response.AdditionalProperties,
            ContinuationToken = response.ContinuationToken,
            ConversationId = response.ConversationId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            ModelId = response.ModelId,
            RawRepresentation = response.RawRepresentation,
            ResponseId = response.ResponseId,
            Usage = response.Usage,
        };
    }

    // An update that carried only withheld calls stays, empty: it may be the one that reports the finish reason.
    private static ChatResponseUpdate Without(ChatResponseUpdate update, ToolKinds kinds)
    {
        if (!update.Contents.Any(kinds.IsWithheld))
        {
            return update;
        }

        var copy = update.Clone();
        copy.Contents = [.. update.Contents.Where(content => !kinds.IsWithheld(content))];

        return copy;
    }

    /// <summary>The tools a request declares, sorted by whether the loop can run them.</summary>
    private sealed class ToolKinds
    {
        private readonly HashSet<string> _invocable = new(StringComparer.Ordinal);
        private readonly HashSet<string> _declarationOnly = new(StringComparer.Ordinal);

        /// <summary>Reads the request's tools.</summary>
        /// <param name="options">The request's options.</param>
        /// <returns>The kinds; <see langword="null"/> when the request has no tool of one of the two kinds, so no response can be mixed.</returns>
        public static ToolKinds? Of(ChatOptions? options)
        {
            if (options?.Tools is not { Count: > 0 } tools)
            {
                return null;
            }

            var kinds = new ToolKinds();

            foreach (var tool in tools)
            {
                // The first declaration of a name decides, as it does in the loop.
                if (tool is not AIFunctionDeclaration declaration ||
                    kinds._invocable.Contains(declaration.Name) ||
                    kinds._declarationOnly.Contains(declaration.Name))
                {
                    continue;
                }

                (declaration is AIFunction ? kinds._invocable : kinds._declarationOnly).Add(declaration.Name);
            }

            return kinds._invocable.Count > 0 && kinds._declarationOnly.Count > 0 ? kinds : null;
        }

        /// <summary>Whether a response with these contents asks for a server tool and a client tool at once.</summary>
        /// <param name="contents">The contents of one model response.</param>
        public bool IsMixed(IEnumerable<AIContent> contents)
        {
            var server = false;
            var client = false;

            foreach (var content in contents)
            {
                if (content is not FunctionCallContent call)
                {
                    continue;
                }

                server |= _invocable.Contains(call.Name);
                client |= _declarationOnly.Contains(call.Name);
            }

            return server && client;
        }

        /// <summary>Whether the content is a call to a client tool.</summary>
        /// <param name="content">The content.</param>
        public bool IsWithheld(AIContent content) => content is FunctionCallContent call && _declarationOnly.Contains(call.Name);
    }
}
