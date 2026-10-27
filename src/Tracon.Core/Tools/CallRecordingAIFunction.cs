using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Writes a tool call into the run's <see cref="ToolCallLedger"/> when it starts and when it ends, so the run
/// record has the call whatever way the run ends.
/// </summary>
/// <remarks>
/// <para>
/// The run recorder reads a tool call from the messages of the model's answer. A run that does not stream has them
/// only when it succeeds; a run that fails at its next model call, or is cancelled, has none. This layer records the
/// call where it happens, and the recorder reads the ledger when the run closes (<see cref="ToolCallLedger"/>).
/// </para>
/// <para>
/// Installed by <see cref="ToolWrapperChain.Compose"/> OUTSIDE <see cref="ExplainedFailureAIFunction"/>, the
/// outermost layer of the chain, for two reasons. A call the budget gate or the authorization handler refuses
/// is a call too, and only the outermost point sees every call that reaches the chain. And the result it keeps is
/// the one the tool-call loop and the model read: the sentence an explained failure or a denial turned into the
/// result, not the exception under it.
/// </para>
/// <para>
/// <strong>It records and changes nothing.</strong> The result, the exception and their order reach the loop exactly
/// as they would without this layer; the layer adds no wait. Its one <c>catch</c> notes an exception in the ledger
/// and throws it again as it is, so the exception travels on unchanged. A call that is cancelled stays open in the
/// ledger on purpose: the run ended before the result, and the run record says so. A cancellation is told from a
/// failure by the call's own token, the way the run recorder does (the same rule as
/// <see cref="OperationCancellation.IsFailure"/>).
/// </para>
/// <para>
/// The ledger and the call identity are both read in the synchronous part of the call, from
/// <see cref="TraconRunContext.Current"/> and <see cref="FunctionInvokingChatClient.CurrentContext"/>. Neither is
/// written here: a layer in the chain does not open a scope (an ambient value written in a helper does not flow
/// back to its caller). With no ledger (a call outside a recorded run) or no call identity (a call outside the
/// tool-call loop) the layer is a pass-through.
/// </para>
/// <para>
/// Internal, unlike its public siblings in the chain, for the reason <see cref="ExplainedFailureAIFunction"/>
/// gives: a consumer never builds or configures it.
/// </para>
/// </remarks>
/// <param name="innerFunction">The tool to wrap.</param>
internal sealed class CallRecordingAIFunction(AIFunction innerFunction)
    : DelegatingAIFunction(innerFunction)
{
    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var ledger = TraconRunContext.Current?.ToolCalls;

        if (ledger is null ||
            FunctionInvokingChatClient.CurrentContext?.CallContent is not { CallId: { Length: > 0 } } call ||
            ledger.Begin(call) is not { } entry)
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        object? result;

        try
        {
            result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        // The call's own cancellation leaves the entry open: the run ended before the result arrived.
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            ledger.Fail(entry, ex);

            throw;
        }

        ledger.Settle(entry, result);

        return result;
    }
}
