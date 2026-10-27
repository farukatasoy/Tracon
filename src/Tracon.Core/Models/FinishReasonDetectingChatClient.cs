using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Reads the finish reason of a model response and turns two of them into an
/// explicit outcome: a response cut off by a content/safety filter (an
/// <strong>empty</strong> one is a <see cref="TraconContentFilteredException"/>, one
/// with text a <see cref="RunEventCustomTypes.OutputFiltered"/> event), and a
/// response stopped at the output token limit
/// (<see cref="TraconOutputTruncatedException"/> or a
/// <see cref="RunEventCustomTypes.OutputTruncated"/> event).
/// </summary>
/// <remarks>
/// <para>
/// Not embedded inside a provider implementation;
/// <see cref="ModelProviderRegistry.CreateChatClient"/> wraps every client with
/// this type — the same pattern as the circuit breaker. This way
/// OpenAI, Anthropic, and Gemini get the same behavior from a single place, and
/// the Azure provider inherits it without writing a rule. The finish reason is
/// read in ONE place: a second decorator would have to repeat the streaming
/// bookkeeping below and could disagree with this one about which update counts.
/// </para>
/// <para>
/// <strong>Wrapping order matters:</strong> this decorator sits <em>outside</em>
/// the circuit breaker and the fallback chain. A filtered or truncated response
/// means the provider is healthy; if it sat inside the circuit breaker, the
/// exception it throws would increase the consecutive-failure counter and a
/// handful of such requests would close the circuit on the provider. Outside
/// the fallback chain, a truncated response is never retried on another link.
/// </para>
/// <para>
/// It also sits <em>outside</em> the tool-call loop, so it judges the
/// <strong>final</strong> turn only. A tool-call turn that hit the limit is an
/// intermediate response: the loop runs the call and asks the model again, and
/// only the answer that ends the loop decides the outcome. On the non-streaming
/// path the loop returns the last turn's response, so its finish reason is the
/// final one. On the streaming path the loop forwards every turn's updates, so
/// the LAST finish reason in the stream is the final turn's — measured against
/// <c>FunctionInvokingChatClient</c> (MEAI 10.11.0), which yields the tool-call
/// turn's updates, then the tool results, then the next turn.
/// </para>
/// <para>
/// <strong>Only an empty filtered response is turned into an error.</strong> If
/// the model produced text and was then filtered (not Gemini's common behavior,
/// but possible), the user has a partial answer in hand; discarding it is a loss
/// of information. The answer passes through and the run records a
/// <see cref="RunEventCustomTypes.OutputFiltered"/> event, so the record says why it
/// ends where it does. A structured (JSON) response gets the event too; whether the
/// document is usable stays the structured-response validator's decision. An empty
/// response left silent, on the other hand, produces the hardest case to debug: the
/// user sees an empty answer and there is no trace in the record.
/// </para>
/// <para>
/// <strong>A truncated response fails only when it is structured.</strong> When
/// the request asked for JSON (<see cref="ChatResponseFormatJson"/>), a cut-off
/// document cannot be parsed, so the call throws
/// <see cref="TraconOutputTruncatedException"/>. A plain-text answer is still
/// partly useful: it passes through and the run records a
/// <see cref="RunEventCustomTypes.OutputTruncated"/> event. The agent run endpoint
/// reads that event back and sends it on its live SSE stream as a <c>custom</c>
/// frame before <c>done</c>; this type is still the only place that decides.
/// </para>
/// <para>
/// <strong>The tokens of a response this type rejects are folded into the
/// run.</strong> The run recorder writes no usage for a run that ended in an
/// exception — on the streaming path either, measured: the usage updates that
/// already passed through are dropped with it — so the run's usage, cost and
/// quota entry would say the call spent nothing; for a truncated response, a
/// full output limit's worth of tokens. The same fold
/// <see cref="StructuredResponseValidatingAgent"/> and
/// <see cref="RecordingLoopEvaluator"/> use. A side-channel call is folded under
/// its own model on the non-streaming path only:
/// <see cref="SideChannelUsageTrackingChatClient"/> already counts a failed
/// stream in its own <c>finally</c>.
/// </para>
/// </remarks>
internal sealed class FinishReasonDetectingChatClient(string providerName, string modelName, IChatClient inner)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (IsContentFilter(response.FinishReason))
        {
            if (!HasContent(response))
            {
                FoldRejectedUsage(response.Usage);
                throw Filtered(response.FinishReason);
            }

            await RecordFilteredTextAsync(cancellationToken).ConfigureAwait(false);
        }

        if (IsTruncated(response.FinishReason))
        {
            if (IsStructured(options))
            {
                FoldRejectedUsage(response.Usage);
                throw Truncated(options, response.FinishReason);
            }

            await RecordTruncatedTextAsync(options, response.FinishReason, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatFinishReason? finishReason = null;
        var sawContent = false;
        UsageDetails? usage = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            // The LAST reported reason wins: a tool-call turn reports its own
            // reason before the loop starts the next turn (see the remarks).
            if (update.FinishReason is { } reported)
            {
                finishReason = reported;
            }

            sawContent = sawContent || HasContent(update.Contents);

            foreach (var content in update.Contents)
            {
                if (content is UsageContent usageContent)
                {
                    // Summed into a fresh accumulator: one UsageContent per model
                    // call, and no UsageDetails owned by an inner client is modified.
                    (usage ??= new UsageDetails()).Add(usageContent.Details);
                }
            }

            yield return update;
        }

        // The decision is made at the END of the stream: the finish reason
        // arrives in the last frame for most providers, and text may have been
        // produced before it.
        if (IsContentFilter(finishReason))
        {
            if (!sawContent)
            {
                FoldRejectedStreamUsage(usage);
                throw Filtered(finishReason);
            }

            await RecordFilteredTextAsync(cancellationToken).ConfigureAwait(false);
        }

        if (IsTruncated(finishReason))
        {
            if (IsStructured(options))
            {
                FoldRejectedStreamUsage(usage);
                throw Truncated(options, finishReason);
            }

            await RecordTruncatedTextAsync(options, finishReason, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Tells whether a finish reason means a content or safety filter stopped the model.</summary>
    /// <param name="finishReason">The finish reason a model call reported.</param>
    /// <returns><see langword="true"/> for <see cref="ChatFinishReason.ContentFilter"/>.</returns>
    internal static bool IsContentFilter(ChatFinishReason? finishReason)
        => finishReason == ChatFinishReason.ContentFilter;

    /// <summary>Tells whether an answer that ends with this finish reason may be incomplete.</summary>
    /// <param name="finishReason">The finish reason a model call reported.</param>
    /// <returns>
    /// <see langword="true"/> when <see cref="IsTruncated"/> or <see cref="IsContentFilter"/> is.
    /// </returns>
    /// <remarks>
    /// The ONE predicate for "this run may hold a notice about the answer". The agent run endpoint
    /// reads it to decide whether to read the run's notices back before <c>done</c>. It says "may":
    /// a filtered answer with no content fails its run and writes no notice, and a tool turn that
    /// hit the limit is not the final answer, so the caller still looks the notice up.
    /// </remarks>
    internal static bool IsIncomplete(ChatFinishReason? finishReason)
        => IsTruncated(finishReason) || IsContentFilter(finishReason);

    // F-426 added the context window; phase 207 opened the predicate to the endpoints.

    /// <summary>Tells whether a finish reason means the answer was cut off.</summary>
    /// <param name="finishReason">The finish reason a model call reported.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ChatFinishReason.Length"/> and
    /// <see cref="TraconFinishReasons.ContextWindowExceeded"/>.
    /// </returns>
    /// <remarks>
    /// The ONE predicate for a cut-off answer. The Chat Completions endpoint uses it to
    /// report <c>"length"</c> and the Responses endpoint <c>max_output_tokens</c>, and
    /// <see cref="IsIncomplete"/> (this or a content filter) is what the agent run endpoint
    /// reads to decide whether to read the run's notices back before <c>done</c>; a second
    /// list of reasons would drift from this one. An answer the context window cut off is
    /// cut off too; only the remedy differs, so the message and the event name the
    /// cause.
    /// </remarks>
    internal static bool IsTruncated(ChatFinishReason? finishReason)
        => finishReason == ChatFinishReason.Length || IsContextWindow(finishReason);

    private static bool IsContextWindow(ChatFinishReason? finishReason)
        => finishReason == TraconFinishReasons.ContextWindowExceeded;

    // ChatResponseFormat.Json and ForJsonSchema(...) both produce this type;
    // ChatResponseFormat.Text does not.
    private static bool IsStructured(ChatOptions? options)
        => options?.ResponseFormat is ChatResponseFormatJson;

    private static bool HasContent(ChatResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            return true;
        }

        foreach (var message in response.Messages)
        {
            if (HasContent(message.Contents))
            {
                return true;
            }
        }

        return false;
    }

    // Content other than text also counts as "not empty": a tool call or a
    // generated image shows the response is usable. Usage counters
    // (UsageContent) alone are not content; a filtered response still spends tokens.
    private static bool HasContent(IEnumerable<AIContent> contents)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                case FunctionCallContent:
                case DataContent:
                case UriContent:
                    return true;

                default:
                    break;
            }
        }

        return false;
    }

    /// <summary>Adds the usage of a response this type rejects to the running run.</summary>
    /// <remarks>
    /// A side-channel call (the compaction summarizer, an <c>aiJudge</c> judge)
    /// runs on its own binding: its share is priced at the model that answered
    /// it, the same rule <see cref="SideChannelUsageTrackingChatClient"/> applies,
    /// which cannot see this response once this type throws.
    /// </remarks>
    private void FoldRejectedUsage(UsageDetails? usage)
    {
        if (usage is null || TraconRunContext.Current is not { ExtraUsage: { } accumulator } scope)
        {
            return;
        }

        if (scope.SideChannelFallback is { } sideChannel)
        {
            var (provider, model) = sideChannel.Current ?? (providerName, modelName);
            accumulator.Add(usage, provider, model);
            return;
        }

        accumulator.Add(usage);
    }

    /// <summary>Adds the usage of a rejected stream to the running run.</summary>
    /// <remarks>
    /// The agent's own call only. A side-channel stream is already counted by
    /// <see cref="SideChannelUsageTrackingChatClient"/>, whose <c>finally</c> runs
    /// for a failed stream too; folding it here would count it twice.
    /// </remarks>
    private static void FoldRejectedStreamUsage(UsageDetails? usage)
    {
        if (usage is null
            || TraconRunContext.Current is not { ExtraUsage: { } accumulator } scope
            || scope.SideChannelFallback is not null)
        {
            return;
        }

        accumulator.Add(usage);
    }

    /// <summary>Writes the <see cref="RunEventCustomTypes.OutputTruncated"/> notice for a plain-text answer.</summary>
    /// <remarks>
    /// Only for the agent's own model call (<see cref="NoticeWriter"/>). The writer
    /// already keeps a store failure from reaching the run.
    /// </remarks>
    private async ValueTask RecordTruncatedTextAsync(
        ChatOptions? options,
        ChatFinishReason? finishReason,
        CancellationToken cancellationToken)
    {
        if (NoticeWriter() is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new OutputTruncatedEventPayload
            {
                Provider = providerName,
                MaxOutputTokens = options?.MaxOutputTokens,
                Cause = IsContextWindow(finishReason)
                    ? OutputTruncatedEventPayload.ContextWindowCause
                    : OutputTruncatedEventPayload.OutputLimitCause,
            },
            TraconCoreJsonContext.Default.OutputTruncatedEventPayload);

        await writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom)
            {
                CustomType = RunEventCustomTypes.OutputTruncated,
                Text = IsContextWindow(finishReason)
                    ? "The model stopped when the conversation filled its context window; the answer is incomplete."
                    : "The model stopped at its output token limit; the answer is incomplete.",
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the <see cref="RunEventCustomTypes.OutputFiltered"/> notice for an answer that has text.</summary>
    /// <remarks>
    /// The same rules as <see cref="RecordTruncatedTextAsync"/>: only the agent's own call writes, and the
    /// payload names the provider and never the answer. The provider's own filter word is not known here
    /// (see <see cref="OutputFilteredEventPayload"/>).
    /// </remarks>
    private async ValueTask RecordFilteredTextAsync(CancellationToken cancellationToken)
    {
        if (NoticeWriter() is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new OutputFilteredEventPayload { Provider = providerName },
            TraconCoreJsonContext.Default.OutputFilteredEventPayload);

        await writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom)
            {
                CustomType = RunEventCustomTypes.OutputFiltered,
                Text = "The provider's content filter stopped the model mid-answer; the answer is incomplete.",
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The writer a notice about the agent's own answer goes to, or <see langword="null"/> outside a
    /// recorded run and in a side-channel call.
    /// </summary>
    /// <remarks>
    /// A side-channel call's answer (the compaction summarizer, an <c>aiJudge</c> judge) is not the run's
    /// answer, and a notice about it would point the operator at the wrong model.
    /// </remarks>
    private static RunEventWriter? NoticeWriter()
        => TraconRunContext.Current is { Writer: { } writer, SideChannelFallback: null } ? writer : null;

    private TraconContentFilteredException Filtered(ChatFinishReason? finishReason)
        => new($"The '{providerName}' provider cut off the response with a content filter and returned no content. " +
               "This is a failed run; the input should be reviewed before the request is retried. " +
               "On Gemini, safety thresholds can be relaxed with ModelBinding.ProviderSettings " +
               "(example: google.safety.harassment = \"BLOCK_ONLY_HIGH\").")
        {
            ProviderName = providerName,
            FinishReason = finishReason?.Value,
        };

    private TraconOutputTruncatedException Truncated(ChatOptions? options, ChatFinishReason? finishReason)
    {
        if (IsContextWindow(finishReason))
        {
            return new TraconOutputTruncatedException(
                $"The '{providerName}' model stopped when the conversation filled its context window, before the " +
                "structured response was complete. A cut-off JSON document cannot be parsed, and a repair turn would " +
                "fill the same window, so none runs. Shorten the conversation (context compaction) or use a model " +
                "with a larger context window; a larger output limit does not help.")
            {
                ProviderName = providerName,
                MaxOutputTokens = options?.MaxOutputTokens,
            };
        }

        var limit = options?.MaxOutputTokens is { } explicitLimit
            ? $"the output token limit of {explicitLimit}"
            : "the provider's default output token limit (ModelBinding.MaxOutputTokens is not set)";

        return new TraconOutputTruncatedException(
            $"The '{providerName}' model stopped at {limit} before the structured response was complete. " +
            "A cut-off JSON document cannot be parsed, and a repair turn would hit the same limit, so none runs. " +
            "Raise ModelBinding.MaxOutputTokens.")
        {
            ProviderName = providerName,
            MaxOutputTokens = options?.MaxOutputTokens,
        };
    }
}

/// <summary>The <see cref="RunEventCustomTypes.OutputTruncated"/> event payload.</summary>
internal sealed record OutputTruncatedEventPayload
{
    /// <summary>Gets the provider whose model stopped at the limit.</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the output token limit the request set, or <see langword="null"/> when the
    /// provider applied its own default.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Gets what cut the answer off: <see cref="OutputLimitCause"/> or
    /// <see cref="ContextWindowCause"/>.
    /// </summary>
    public required string Cause { get; init; }

    /// <summary>The model reached its output token limit.</summary>
    public const string OutputLimitCause = "output_limit";

    /// <summary>The conversation and the answer filled the model's context window.</summary>
    public const string ContextWindowCause = "context_window";
}
