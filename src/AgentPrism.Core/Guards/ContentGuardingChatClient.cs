using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Decorator that runs the messages going to the model and the response coming
/// from the model through the registered <see cref="IContentGuard"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The layer choice was measured and deviated from the plan.</strong>
/// An <c>IAgentDecorator</c> sees only the agent's first input and final output;
/// it does not see the turns in between. A tool result enters the model on the
/// <em>second</em> call, and that is the most common path for prompt injection.
/// This is why the guard sits at the <c>IChatClient</c> layer.
/// </para>
/// <para>
/// It also sits <strong>INSIDE</strong> <c>UseFunctionInvocation()</c>.
/// Measured: the tool-call loop is driven by MAF's
/// <c>FunctionInvokingChatClient</c>, and every turn of that loop goes to the
/// same inner client. If the decorator sat outside the loop it would see only
/// ONE call per agent turn and tool results would never be inspected. The entire
/// pipeline is assembled inside <c>ModelProviderRegistry.CreateChatClient</c>.
/// </para>
/// <para>
/// The circuit breaker sits <em>outside</em> this decorator; an explicit carve-out
/// inside <c>CircuitBreakingChatClient</c> ensures a block decision does not open
/// the circuit (a block is not a provider failure — the same rationale as a
/// filtered response).
/// </para>
/// <para>
/// <strong>The system instruction is not inspected.</strong> It is written in
/// code or through the management API, already enters the audit log, and
/// re-inspecting it on every call is a fixed cost that catches nothing.
/// </para>
/// </remarks>
internal sealed class ContentGuardingChatClient(
    ContentGuardPipeline pipeline,
    string? modelId,
    IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var settings = pipeline.Options;

        var outbound = settings.InspectInput
            ? await InspectInputAsync(messages, cancellationToken).ConfigureAwait(false)
            : messages;

        var response = await base.GetResponseAsync(outbound, options, cancellationToken).ConfigureAwait(false);

        if (settings.InspectOutput)
        {
            await InspectOutputAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = pipeline.Options;

        var outbound = settings.InspectInput
            ? await InspectInputAsync(messages, cancellationToken).ConfigureAwait(false)
            : messages;

        if (!settings.InspectOutput)
        {
            await foreach (var update in base.GetStreamingResponseAsync(outbound, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        if (!settings.BufferStreamingOutput)
        {
            // Frame-by-frame inspection: a pattern may not match on a partial
            // piece of text, and a pattern that straddles a frame boundary
            // ESCAPES detection. This path is an explicit choice
            // (BufferStreamingOutput = false), not a hidden behavior.
            await foreach (var update in base.GetStreamingResponseAsync(outbound, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return await MaskUpdateAsync(update, cancellationToken).ConfigureAwait(false) ?? update;
            }

            yield break;
        }

        // 🚨 Buffered path: no frame reaches the client before inspection is
        // done. A frame cannot be recalled once sent; a block decision can only
        // be made correctly once the whole text has been seen.
        var buffered = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(outbound, options, cancellationToken)
            .ConfigureAwait(false))
        {
            buffered.Add(update);
        }

        ApplyMask(buffered, await InspectBufferAsync(buffered, cancellationToken).ConfigureAwait(false));

        foreach (var update in buffered)
        {
            yield return update;
        }
    }

    /// <summary>
    /// Inspects the messages to be sent and, if needed, builds a masked copy.
    /// </summary>
    /// <remarks>
    /// The caller's list is <strong>not modified</strong>. Masking changes the
    /// prompt going to the model, not the conversation history: if it were
    /// written to the history the mask would become permanent and the user's own
    /// text would be lost irrecoverably.
    /// </remarks>
    private ValueTask<IEnumerable<ChatMessage>> InspectInputAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // Not copied if the list is already an IReadOnlyList: MAF passes
        // messages as a list, and if there is no match, no allocation happens.
        var buffer = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        return MaskAsync(buffer, cancellationToken);
    }

    // Logic SHARED with RunRecordingAgent: the recording path (RunStarted
    // event, IRunInputStore) applies the same inspection in the same order so
    // that what goes to the model and what is recorded NEVER diverge (HATA-S3-006).
    private async ValueTask<IEnumerable<ChatMessage>> MaskAsync(
        IReadOnlyList<ChatMessage> buffer,
        CancellationToken cancellationToken)
        => await ContentGuardMessageMasker
            .MaskAsync(pipeline, ContentGuardDirection.Input, buffer, modelId, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Inspects the response and replaces it with masked messages if needed.
    /// </summary>
    /// <remarks>
    /// <see cref="ChatMessage"/> objects are <strong>not modified in place</strong>.
    /// The inner client may reuse the same instance (a caching client, or a
    /// pre-built fake client); modifying in place would permanently corrupt that
    /// instance, and a second call would mistake the masked text for "the
    /// model's response". Only the <see cref="ChatResponse.Messages"/> list
    /// itself is ours.
    /// </remarks>
    private async ValueTask InspectOutputAsync(ChatResponse response, CancellationToken cancellationToken)
    {
        var changed = false;

        for (var index = 0; index < response.Messages.Count; index++)
        {
            if (await MaskOutputAsync(response.Messages[index], cancellationToken).ConfigureAwait(false)
                is { } replacement)
            {
                response.Messages[index] = replacement;
                changed = true;
            }
        }

        if (changed)
        {
            // Same rationale: the raw response carries the unmasked text.
            response.RawRepresentation = null;
        }
    }

    private async ValueTask<ChatMessage?> MaskOutputAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < message.Contents.Count; index++)
        {
            var replacement = await ContentGuardMessageMasker
                .RewriteContentAsync(
                    message.Contents[index], pipeline, ContentGuardDirection.Output, message.Role,
                    callIdToToolName: null, modelId, recordDecision: true, cancellationToken)
                .ConfigureAwait(false);

            if (replacement is null)
            {
                continue;
            }

            contents ??= [.. message.Contents];
            contents[index] = replacement;
        }

        if (contents is null)
        {
            return null;
        }

        var clone = message.Clone();
        clone.Contents = contents;
        clone.RawRepresentation = null;

        return clone;
    }

    /// <summary>
    /// Inspects a single frame; returns a new frame if masking is needed.
    /// </summary>
    /// <returns><see langword="null"/> if nothing changed.</returns>
    private async ValueTask<ChatResponseUpdate?> MaskUpdateAsync(
        ChatResponseUpdate update,
        CancellationToken cancellationToken)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < update.Contents.Count; index++)
        {
            var replacement = await ContentGuardMessageMasker
                .RewriteContentAsync(
                    update.Contents[index], pipeline, ContentGuardDirection.Output, update.Role,
                    callIdToToolName: null, modelId, recordDecision: true, cancellationToken)
                .ConfigureAwait(false);

            if (replacement is null)
            {
                continue;
            }

            contents ??= [.. update.Contents];
            contents[index] = replacement;
        }

        if (contents is null)
        {
            return null;
        }

        var clone = update.Clone();
        clone.Contents = contents;
        clone.RawRepresentation = null;

        return clone;
    }

    /// <summary>
    /// Inspects the COMBINED text of the buffered frames.
    /// </summary>
    /// <returns>
    /// The masked combined text; <see langword="null"/> if nothing changed.
    /// </returns>
    private async ValueTask<string?> InspectBufferAsync(
        List<ChatResponseUpdate> buffered,
        CancellationToken cancellationToken)
    {
        StringBuilder? joined = null;

        foreach (var update in buffered)
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent { Text.Length: > 0 } text)
                {
                    (joined ??= new StringBuilder()).Append(text.Text);
                }
            }
        }

        return joined is null
            ? null
            : await pipeline
                .InspectAsync(
                    ContentGuardDirection.Output, joined.ToString(), ContentGuardSource.ModelOutput, toolName: null,
                    modelId, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the masked combined text back into the frame sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Because masking can change the character count, matches cannot be mapped
    /// back to individual frames one by one. The whole text is written to the
    /// <strong>first</strong> text frame, the remaining text frames are emptied;
    /// non-text content (tool calls, usage counters) stays in place. The total
    /// text is preserved, and neither recording nor metrics are broken.
    /// </para>
    /// <para>
    /// Frames are <strong>copied</strong>, not modified in place: the inner
    /// client may reuse the same instances.
    /// </para>
    /// </remarks>
    private static void ApplyMask(List<ChatResponseUpdate> buffered, string? masked)
    {
        if (masked is null)
        {
            return;
        }

        var written = false;

        for (var position = 0; position < buffered.Count; position++)
        {
            var update = buffered[position];
            List<AIContent>? contents = null;

            for (var index = 0; index < update.Contents.Count; index++)
            {
                if (update.Contents[index] is not TextContent { Text.Length: > 0 })
                {
                    continue;
                }

                contents ??= [.. update.Contents];
                contents[index] = new TextContent(written ? string.Empty : masked);
                written = true;
            }

            if (contents is null)
            {
                continue;
            }

            var clone = update.Clone();
            clone.Contents = contents;
            clone.RawRepresentation = null;
            buffered[position] = clone;
        }
    }
}
