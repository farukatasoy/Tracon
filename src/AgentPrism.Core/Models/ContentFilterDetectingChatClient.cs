using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Turns an <strong>empty</strong> response that was cut off by a content/safety
/// filter into an error via <see cref="AgentPrismContentFilteredException"/>.
/// </summary>
/// <remarks>
/// <para>
/// Not embedded inside a provider implementation;
/// <see cref="ModelProviderRegistry.CreateChatClient"/> wraps every client with
/// this type — the same pattern as the circuit breaker. This way
/// OpenAI, Anthropic, and Gemini get the same behavior from a single place, and
/// The Azure provider inherits it without writing a rule.
/// </para>
/// <para>
/// <strong>Wrapping order matters:</strong> this decorator sits <em>outside</em>
/// the circuit breaker. A filtered response means the provider is healthy; if it
/// sat inside the circuit breaker, the exception it throws would increase the
/// consecutive-failure counter and a handful of content-filtered requests would
/// close the circuit on the provider.
/// </para>
/// <para>
/// <strong>Only an empty response is turned into an error.</strong> If the model
/// produced text and was then cut off (not Gemini's common behavior, but
/// possible), the user has a partial answer in hand; discarding it is a loss of
/// information. An empty response left silent, on the other hand, produces the
/// hardest case to debug: the user sees an empty answer and there is no trace
/// in the record.
/// </para>
/// </remarks>
internal sealed class ContentFilterDetectingChatClient(string providerName, IChatClient inner)
    : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        if (IsContentFilter(response.FinishReason) && !HasContent(response))
        {
            throw Filtered(response.FinishReason);
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

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            finishReason ??= update.FinishReason;
            sawContent = sawContent || HasContent(update.Contents);

            yield return update;
        }

        // The decision is made at the END of the stream: the finish reason
        // arrives in the last frame for most providers, and text may have been
        // produced before it.
        if (IsContentFilter(finishReason) && !sawContent)
        {
            throw Filtered(finishReason);
        }
    }

    private static bool IsContentFilter(ChatFinishReason? finishReason)
        => finishReason == ChatFinishReason.ContentFilter;

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

    private AgentPrismContentFilteredException Filtered(ChatFinishReason? finishReason)
        => new($"The '{providerName}' provider cut off the response with a content filter and returned no content. " +
               "This is a failed run; the input should be reviewed before the request is retried. " +
               "On Gemini, safety thresholds can be relaxed with ModelBinding.ProviderSettings " +
               "(example: google.safety.harassment = \"BLOCK_ONLY_HIGH\").")
        {
            ProviderName = providerName,
            FinishReason = finishReason?.Value,
        };
}
