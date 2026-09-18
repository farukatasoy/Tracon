using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Turns a <see cref="TraconException"/> thrown by a tool into the tool's
/// result, so the model reads the sentence Tracon wrote for it.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework's <c>FunctionInvokingChatClient</c> converts a
/// thrown exception into a generic <c>"Error: Function failed."</c> unless
/// <c>IncludeDetailedErrors</c> is on. Tracon does not turn that setting on,
/// and it should not: it would publish EVERY exception's detail to the model,
/// including a provider SDK's own message and anything a stack trace carries.
/// </para>
/// <para>
/// A <see cref="TraconException"/> is different in kind. Its message is a
/// sentence Tracon itself wrote for exactly this audience — "that attachment
/// is not an audio file", "the text is longer than the limit", "these
/// arguments were rejected because …". Withholding it does not protect
/// anything; it only stops the model from correcting itself, and a rejected
/// tool call the model cannot understand is a call it repeats.
/// </para>
/// <para>
/// Installed by <see cref="ToolWrapperChain.Compose"/> as the OUTERMOST
/// layer, so it covers every layer beneath it — argument validation,
/// authorization, the timeout, and the tool's own body — for every tool
/// source at once. Nothing else is caught: a provider SDK failure, a bug, a
/// cancellation all keep travelling as they did.
/// </para>
/// <para>
/// Internal, unlike its public siblings in the chain: a consumer never builds
/// or configures it, and there is no knob to turn. Growing the package's
/// public surface for a layer nobody can address is a cost with no return.
/// </para>
/// </remarks>
/// <param name="innerFunction">The tool to wrap.</param>
internal sealed class ExplainedFailureAIFunction(AIFunction innerFunction)
    : DelegatingAIFunction(innerFunction)
{
    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException ex)
        {
            // The message becomes the result, and that has a cost: the run
            // record reads FunctionResultContent.Exception to tell a failed
            // call from a successful one, and this call no longer carries one.
            // The failure therefore travels beside the result, the same way
            // AuthorizingAIFunction carries a denial that also returns
            // normally. Without this the record showed a timed-out or rejected
            // call as an ordinary success.
            if (FunctionInvokingChatClient.CurrentContext?.CallContent.CallId is { Length: > 0 } callId)
            {
                TraconRunContext.Current?.ToolExplainedFailures?.RecordFailure(callId, ex);
            }

            return ex.Message;
        }
    }
}
