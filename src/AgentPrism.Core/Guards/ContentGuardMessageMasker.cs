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
            message => MaskMessageAsync(pipeline, direction, message, modelId, cancellationToken));

    /// <summary>
    /// Inspects every message EXCEPT the system instruction in the given
    /// direction but does NOT RECORD A DECISION
    /// (<see cref="ContentGuardPipeline.PreviewAsync"/>) — see that method's
    /// docs: this is the only way <c>RunRecordingAgent.BeginRunAsync</c> can call
    /// it safely before the run row exists yet.
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
            message => PreviewMessageAsync(pipeline, direction, message, modelId, cancellationToken));

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

    private static async ValueTask<ChatMessage?> MaskMessageAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        ChatMessage message,
        string? modelId,
        CancellationToken cancellationToken)
        => await RewriteMessageAsync(
            message,
            text => pipeline.InspectAsync(direction, text, modelId, cancellationToken)).ConfigureAwait(false);

    private static async ValueTask<ChatMessage?> PreviewMessageAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        ChatMessage message,
        string? modelId,
        CancellationToken cancellationToken)
        => await RewriteMessageAsync(
            message,
            text => pipeline.PreviewAsync(direction, text, modelId, cancellationToken)).ConfigureAwait(false);

    private static async ValueTask<ChatMessage?> RewriteMessageAsync(
        ChatMessage message,
        Func<string, ValueTask<string?>> inspectAsync)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < message.Contents.Count; index++)
        {
            var content = message.Contents[index];

            if (ReadText(content) is not { Length: > 0 } text)
            {
                continue;
            }

            var rewritten = await inspectAsync(text).ConfigureAwait(false);

            if (rewritten is null)
            {
                continue;
            }

            contents ??= [.. message.Contents];
            contents[index] = WriteText(content, rewritten);
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
    /// Reads inspectable text; <see langword="null"/> for content that cannot be inspected.
    /// </summary>
    /// <remarks>
    /// <see cref="FunctionResultContent"/> is deliberately covered: a tool
    /// result is part of the content the model sees, and malicious text returned
    /// by a remote MCP tool enters exactly through here.
    /// </remarks>
    public static string? ReadText(AIContent content) => content switch
    {
        TextContent text => text.Text,
        FunctionResultContent { Result: string result } => result,
        FunctionResultContent { Result: { } result } => result.ToString(),
        _ => null,
    };

    public static AIContent WriteText(AIContent content, string text) => content switch
    {
        FunctionResultContent result => new FunctionResultContent(result.CallId, text) { Exception = result.Exception },
        _ => new TextContent(text),
    };
}
