using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Finish reasons Tracon reads beyond the ones <see cref="ChatFinishReason"/> defines.
/// </summary>
/// <remarks>
/// A model provider reports one of these on its <see cref="ChatResponse"/> (or on the
/// last streamed <see cref="ChatResponseUpdate"/>) the same way it reports
/// <see cref="ChatFinishReason.Length"/>. Tracon reads them in the same shared ring that
/// reads the standard reasons, so every provider that reports one gets the same
/// behaviour.
/// </remarks>
public static class TraconFinishReasons
{
    /// <summary>
    /// Gets the finish reason of an answer the model stopped because the conversation
    /// and the answer filled its context window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tracon treats it as a cut-off answer, like <see cref="ChatFinishReason.Length"/>:
    /// a structured (JSON) response fails its run with
    /// <see cref="TraconOutputTruncatedException"/> (<see cref="RunErrorClass.OutputTruncated"/>),
    /// and a plain-text answer completes with a
    /// <see cref="RunEventCustomTypes.OutputTruncated"/> run event whose payload
    /// <c>cause</c> is <c>context_window</c>.
    /// </para>
    /// <para>
    /// It is a separate reason because the remedy is different. A larger
    /// <see cref="ModelBinding.MaxOutputTokens"/> does not fit in a full window; a shorter
    /// conversation (context compaction) or a model with a larger window does. The
    /// error message and the event say so.
    /// </para>
    /// <para>
    /// <c>Tracon.Anthropic</c> reports it for Anthropic's
    /// <c>model_context_window_exceeded</c> stop reason.
    /// </para>
    /// </remarks>
    public static ChatFinishReason ContextWindowExceeded { get; } = new("context_window_exceeded");
}
