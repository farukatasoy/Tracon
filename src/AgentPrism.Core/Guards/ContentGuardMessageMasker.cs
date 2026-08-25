using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Shared helper that runs <see cref="ChatMessage"/> lists through
/// <see cref="ContentGuardPipeline"/> and builds a masked copy.
/// </summary>
/// <remarks>
/// <see cref="ContentGuardingChatClient"/> (the request going to the model, using
/// <see cref="MaskAsync"/> with <see cref="ContentGuardPipeline.InspectAsync"/>)
/// and <see cref="RunRecordingAgent"/> (the run record — the <c>RunStarted</c>
/// event and <see cref="IRunInputStore"/>, using <see cref="PreviewAsync"/> with
/// <see cref="ContentGuardPipeline.PreviewAsync"/>) share the SAME
/// message-scanning logic: both must keep the recorded input equal to what the
/// model actually saw. If the guard result diverges between the two, masked or
/// blocked content is left raw in permanent storage.
/// </remarks>
internal static class ContentGuardMessageMasker
{
    /// <summary>
    /// The fixed replacement for a tool result that cannot be normalized into
    /// inspectable text. This text is the final replacement itself — it is
    /// never handed to a guard's pattern match, because there is no real
    /// text to match a pattern against; the whole point is that AgentPrism
    /// could not read the content it is protecting.
    /// </summary>
    internal const string UninspectableToolResultText = "[Tool result could not be inspected]";

    /// <summary>
    /// Inspects every message EXCEPT the system instruction in the given
    /// direction and RECORDS A DECISION (<see cref="ContentGuardPipeline.InspectAsync"/>).
    /// </summary>
    /// <returns>The caller's OWN list if nothing changed (allocation-free path).</returns>
    public static ValueTask<IReadOnlyList<ChatMessage>> MaskAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        IReadOnlyList<ChatMessage> messages,
        string? modelId,
        CancellationToken cancellationToken)
        => RewriteAsync(
            messages,
            message => RewriteMessageAsync(pipeline, direction, message, modelId, recordDecision: true, cancellationToken));

    /// <summary>
    /// Inspects every message EXCEPT the system instruction in the given
    /// direction but does NOT RECORD A DECISION
    /// (<see cref="ContentGuardPipeline.PreviewAsync"/>) — see that method's
    /// docs: this is the only way <c>RunRecordingAgent.BeginRunAsync</c> can
    /// call it safely before the run row exists yet.
    /// </summary>
    /// <returns>The caller's OWN list if nothing changed (allocation-free path).</returns>
    public static ValueTask<IReadOnlyList<ChatMessage>> PreviewAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        IReadOnlyList<ChatMessage> messages,
        string? modelId,
        CancellationToken cancellationToken)
        => RewriteAsync(
            messages,
            message => RewriteMessageAsync(pipeline, direction, message, modelId, recordDecision: false, cancellationToken));

    private static async ValueTask<IReadOnlyList<ChatMessage>> RewriteAsync(
        IReadOnlyList<ChatMessage> messages,
        Func<ChatMessage, ValueTask<ChatMessage?>> rewriteAsync)
    {
        List<ChatMessage>? rebuilt = null;

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            // The system instruction is deliberately skipped (see ContentGuardingChatClient).
            var replacement = message.Role == ChatRole.System
                ? null
                : await rewriteAsync(message).ConfigureAwait(false);

            if (replacement is null)
            {
                rebuilt?.Add(message);
                continue;
            }

            if (rebuilt is null)
            {
                rebuilt = new List<ChatMessage>(messages.Count);

                for (var earlier = 0; earlier < index; earlier++)
                {
                    rebuilt.Add(messages[earlier]);
                }
            }

            rebuilt.Add(replacement);
        }

        return rebuilt ?? messages;
    }

    private static async ValueTask<ChatMessage?> RewriteMessageAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        ChatMessage message,
        string? modelId,
        bool recordDecision,
        CancellationToken cancellationToken)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < message.Contents.Count; index++)
        {
            var replacement = await RewriteContentAsync(
                message.Contents[index], pipeline, direction, modelId, recordDecision, cancellationToken)
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

        // 🚨 The raw representation is DELIBERATELY dropped — see
        // ContentGuardingChatClient for the same rationale (Phase 26).
        clone.RawRepresentation = null;

        return clone;
    }

    /// <summary>
    /// Decides the replacement for one piece of content. Shared by the input
    /// path (<see cref="MaskAsync"/>/<see cref="PreviewAsync"/>) and
    /// <see cref="ContentGuardingChatClient"/>'s output path, so the
    /// fail-closed rule below lives in exactly one place.
    /// </summary>
    /// <remarks>
    /// <see cref="FunctionResultContent"/> that <see cref="ToolResultText.TryGetText"/>
    /// cannot normalize is <strong>unconditionally</strong> replaced with
    /// <see cref="UninspectableToolResultText"/> — it never reaches a guard's
    /// pattern match. Routing it through the guard chain the same way real text
    /// is routed would mean testing a fixed English sentence against every
    /// guard's pattern instead of the tool's real, unexamined output; that
    /// sentence essentially never matches, so the real content would reach the
    /// model and permanent storage completely unmasked despite guards being
    /// registered — the opposite of "content that cannot be inspected is not
    /// let through" (see <see cref="ContentGuardPipeline"/>'s own remarks).
    /// </remarks>
    /// <returns>The replacement content, or <see langword="null"/> if unchanged.</returns>
    public static async ValueTask<AIContent?> RewriteContentAsync(
        AIContent content,
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        string? modelId,
        bool recordDecision,
        CancellationToken cancellationToken)
    {
        if (content is FunctionResultContent { Result: var result } && !ToolResultText.TryGetText(result, out _))
        {
            if (recordDecision)
            {
                await ContentGuardPipeline.RecordUninspectableToolResultAsync(direction, cancellationToken).ConfigureAwait(false);
            }

            return WriteText(content, UninspectableToolResultText);
        }

        if (ReadText(content) is not { Length: > 0 } text)
        {
            return null;
        }

        var rewritten = recordDecision
            ? await pipeline.InspectAsync(direction, text, modelId, cancellationToken).ConfigureAwait(false)
            : await pipeline.PreviewAsync(direction, text, modelId, cancellationToken).ConfigureAwait(false);

        return rewritten is null ? null : WriteText(content, rewritten);
    }

    /// <summary>
    /// Reads inspectable text; <see langword="null"/> for content that is not
    /// text at all (an attachment, a function call). A <see cref="FunctionResultContent"/>
    /// that cannot be normalized is handled by <see cref="RewriteContentAsync"/>
    /// BEFORE this is reached — it is never routed here.
    /// </summary>
    /// <remarks>
    /// <see cref="FunctionResultContent"/> is deliberately covered: a tool
    /// result is part of the content the model sees, and malicious text returned
    /// by a remote MCP tool enters exactly through here.
    /// </remarks>
    public static string? ReadText(AIContent content) => content switch
    {
        TextContent text => text.Text,
        FunctionResultContent { Result: var result } when ToolResultText.TryGetText(result, out var text) => text,
        _ => null,
    };

    public static AIContent WriteText(AIContent content, string text) => content switch
    {
        FunctionResultContent result => new FunctionResultContent(result.CallId, text) { Exception = result.Exception },
        _ => new TextContent(text),
    };
}
