using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Reads the two Anthropic stop reasons the SDK's adapter reports as an ordinary
/// stop: <c>model_context_window_exceeded</c> and <c>pause_turn</c>.
/// </summary>
/// <remarks>
/// <para>
/// Measured (Anthropic SDK 12.39.0, the adapter decompiled): <c>ToFinishReason</c> maps
/// only <c>refusal</c>, <c>max_tokens</c> and <c>tool_use</c>, and every other stop
/// reason becomes <see cref="ChatFinishReason.Stop"/>. The stop reason itself is still on
/// the raw representation: the response's <see cref="Message"/>, and the
/// <see cref="RawMessageDeltaEvent"/> of a stream.
/// </para>
/// <para>
/// <c>model_context_window_exceeded</c>: the answer was cut off because the conversation
/// filled the context window. Reported as <see cref="TraconFinishReasons.ContextWindowExceeded"/>,
/// so the registry's finish-reason ring treats it as a cut-off answer and names the window.
/// </para>
/// <para>
/// <c>pause_turn</c>: the API paused a long turn of its own server tools (web search, for
/// one) before the model finished. The turn is not over. As the API documents, the paused
/// response is sent back as it is, as the last assistant message, and the model continues
/// where it stopped; this repeats up to <see cref="MaxPauseContinuations"/> times. The
/// blocks are sent back from the API's own JSON: the adapter would drop a server tool's
/// call and result if they went through its own conversion. A turn still paused after the
/// last continuation fails with <see cref="AnthropicTurnPausedException"/>.
/// </para>
/// <para>
/// Sits directly on the adapter, inside <see cref="AnthropicProviderSettingsChatClient"/>, so
/// every continuation goes out with the same settings as the call it continues. Every call
/// is a model call of its own, and its usage counts.
/// </para>
/// </remarks>
internal sealed class AnthropicStopReasonChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <summary>How many times a paused turn is continued before the call fails.</summary>
    internal const int MaxPauseContinuations = 5;

    private const string PauseTurn = "pause_turn";
    private const string ContextWindowExceeded = "model_context_window_exceeded";

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        var response = await base.GetResponseAsync(history, options, cancellationToken).ConfigureAwait(false);
        List<ContentBlockParam>? paused = null;

        for (var continuation = 0; IsPaused(response, out var message); continuation++)
        {
            if (continuation == MaxPauseContinuations)
            {
                throw new AnthropicTurnPausedException(MaxPauseContinuations);
            }

            // The turn so far, as one assistant message: the next call continues it.
            paused ??= [];
            paused.AddRange(message.Content.Select(static block => new ContentBlockParam(block.Json)));

            var next = await base.GetResponseAsync(
                [.. history, AsItIs(paused)],
                options,
                cancellationToken).ConfigureAwait(false);

            response = Merge(response, next);
        }

        if (response.RawRepresentation is Message { StopReason: { } stop }
            && string.Equals(stop.Raw(), ContextWindowExceeded, StringComparison.Ordinal))
        {
            response.FinishReason = TraconFinishReasons.ContextWindowExceeded;
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        List<ContentBlockParam>? paused = null;

        for (var continuation = 0; ; continuation++)
        {
            var events = new List<RawMessageStreamEvent>();
            string? stopReason = null;

            var request = paused is null ? history : [.. history, AsItIs(paused)];

            await foreach (var update in base.GetStreamingResponseAsync(request, options, cancellationToken)
                .ConfigureAwait(false))
            {
                if (update.RawRepresentation is RawMessageStreamEvent streamEvent)
                {
                    events.Add(streamEvent);

                    if (streamEvent.Value is RawMessageDeltaEvent delta)
                    {
                        stopReason = delta.Delta.StopReason?.Raw();
                    }
                }

                // The adapter writes its mapped reason on the stop event and on the
                // usage update after it. A paused segment is not the end of the turn,
                // and the window's cut is not a plain stop.
                if (string.Equals(stopReason, PauseTurn, StringComparison.Ordinal))
                {
                    update.FinishReason = null;
                }
                else if (string.Equals(stopReason, ContextWindowExceeded, StringComparison.Ordinal))
                {
                    update.FinishReason = TraconFinishReasons.ContextWindowExceeded;
                }

                yield return update;
            }

            if (!string.Equals(stopReason, PauseTurn, StringComparison.Ordinal))
            {
                yield break;
            }

            if (continuation == MaxPauseContinuations)
            {
                throw new AnthropicTurnPausedException(MaxPauseContinuations);
            }

            paused ??= [];
            paused.AddRange(StreamedBlocks(events));
        }
    }

    private static bool IsPaused(ChatResponse response, [NotNullWhen(true)] out Message? message)
    {
        message = response.RawRepresentation as Message;

        return message?.StopReason is { } reason && string.Equals(reason.Raw(), PauseTurn, StringComparison.Ordinal);
    }

    /// <summary>The paused turn as one assistant message the adapter sends unchanged.</summary>
    /// <remarks>
    /// The adapter passes a content whose raw representation is a
    /// <see cref="ContentBlockParam"/> through as it is (measured on the decompiled
    /// <c>CreateMessageParams</c>), and converts everything else itself.
    /// </remarks>
    private static ChatMessage AsItIs(List<ContentBlockParam> blocks)
        => new(ChatRole.Assistant, [.. blocks.Select(static block => new AIContent { RawRepresentation = block })]);

    /// <summary>Joins a continuation onto the turn it continues: one assistant message, both usages.</summary>
    private static ChatResponse Merge(ChatResponse turn, ChatResponse continuation)
    {
        var usage = turn.Usage;

        if (continuation.Usage is { } more)
        {
            usage = new UsageDetails();
            usage.Add(turn.Usage ?? new UsageDetails());
            usage.Add(more);
        }

        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [.. turn.Messages.SelectMany(static message => message.Contents), .. continuation.Messages.SelectMany(static message => message.Contents)])
        {
            MessageId = continuation.Messages.LastOrDefault()?.MessageId,
            CreatedAt = continuation.CreatedAt,
        })
        {
            CreatedAt = continuation.CreatedAt,
            FinishReason = continuation.FinishReason,
            ModelId = continuation.ModelId,
            RawRepresentation = continuation.RawRepresentation,
            ResponseId = continuation.ResponseId,
            Usage = usage,
        };
    }

    /// <summary>Rebuilds the blocks of one streamed message from its events, as the API's JSON.</summary>
    /// <remarks>
    /// The SDK's own <c>MessageContentAggregator</c> cannot be used: it reads a server
    /// tool block's deltas as blocks of that type and throws on the first
    /// <c>input_json_delta</c> (decompiled, 12.39.0). Each block starts as the JSON of its
    /// <c>content_block_start</c>; text, thinking and signature deltas are appended, a
    /// citation is added, and the partial JSON of a tool's input is parsed at the end.
    /// </remarks>
    private static IEnumerable<ContentBlockParam> StreamedBlocks(IEnumerable<RawMessageStreamEvent> events)
    {
        var blocks = new SortedDictionary<long, (JsonObject Block, StringBuilder Input)>();

        foreach (var streamEvent in events)
        {
            var json = JsonNode.Parse(streamEvent.Json.GetRawText())!.AsObject();
            var type = (string?)json["type"];

            if (string.Equals(type, "content_block_start", StringComparison.Ordinal))
            {
                blocks[(long)json["index"]!] = (json["content_block"]!.DeepClone().AsObject(), new StringBuilder());
                continue;
            }

            if (!string.Equals(type, "content_block_delta", StringComparison.Ordinal)
                || !blocks.TryGetValue((long)json["index"]!, out var entry))
            {
                continue;
            }

            var delta = json["delta"]!.AsObject();

            switch ((string?)delta["type"])
            {
                case "text_delta":
                    entry.Block["text"] = (string?)entry.Block["text"] + (string?)delta["text"];
                    break;

                case "thinking_delta":
                    entry.Block["thinking"] = (string?)entry.Block["thinking"] + (string?)delta["thinking"];
                    break;

                case "signature_delta":
                    entry.Block["signature"] = (string?)entry.Block["signature"] + (string?)delta["signature"];
                    break;

                case "citations_delta":
                    if (entry.Block["citations"] is not JsonArray citations)
                    {
                        entry.Block["citations"] = citations = [];
                    }

                    citations.Add(delta["citation"]?.DeepClone());
                    break;

                case "input_json_delta":
                    entry.Input.Append((string?)delta["partial_json"]);
                    break;

                default:
                    break;
            }
        }

        foreach (var (block, input) in blocks.Values)
        {
            if (input.Length > 0)
            {
                block["input"] = JsonNode.Parse(input.ToString());
            }

            using var document = JsonDocument.Parse(block.ToJsonString());

            yield return new ContentBlockParam(document.RootElement.Clone());
        }
    }
}

/// <summary>
/// The Anthropic API paused a turn more times than Tracon continues one.
/// </summary>
/// <remarks>
/// Thrown by the provider's own client, so the model-call boundary records it like any
/// other provider failure: error type <c>upstream_error</c>, class
/// <see cref="RunErrorClass.ProviderError"/>, and this type's name as the fault. The run
/// does not complete with a half-done turn.
/// </remarks>
internal sealed class AnthropicTurnPausedException(int continuations)
    : Exception(
        $"The Anthropic API paused the turn again after {continuations} continuations (stop reason 'pause_turn'); " +
        "the model did not finish its answer.");
