using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Wraps a remote MCP tool so a server's request for user input (elicitation) in the middle of a call
/// suspends the call instead of failing it.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper opens a <see cref="McpToolCallScope"/> in the body of <see cref="InvokeCoreAsync"/> and closes it
/// in <c>finally</c>. The SDK runs its elicitation handler inside the same asynchronous flow as the
/// <c>tools/call</c> request, so the handler (<see cref="McpElicitationHandler"/>) sees the scope of this call.
/// When the handler finds no answer it records the request in the scope and throws; the wrapper turns that
/// into the call's result, writes the request into the run's <see cref="ToolInputLedger"/> and tells the
/// function-invocation loop to stop (<c>FunctionInvocationContext.Terminate</c>). The run then closes
/// <see cref="RunStatus.AwaitingInput"/>.
/// </para>
/// <para>
/// It is the layer directly around the discovered tool, inside every Tracon wrapper
/// (<see cref="ToolWrapperChain"/>): authorization, validation, the timeout and the output limit all
/// apply to a suspended call exactly as to a finished one.
/// </para>
/// </remarks>
internal sealed class McpElicitingTool : DelegatingAIFunction
{
    private readonly string _serverName;

    /// <summary>Wraps a discovered tool.</summary>
    /// <param name="remote">The tool as the MCP client discovered it.</param>
    /// <param name="serverName">The server the tool belongs to.</param>
    public McpElicitingTool(AIFunction remote, string serverName)
        : base(remote)
    {
        _serverName = serverName;
    }

    /// <summary>Gets the tool as the MCP client discovered it.</summary>
    public AIFunction Remote => InnerFunction;

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var loop = FunctionInvokingChatClient.CurrentContext;
        var run = TraconRunContext.Current;
        var call = loop?.CallContent;
        var callId = call?.CallId;

        IReadOnlyList<ToolInputAnswer>? answers = null;

        if (callId is not null && run?.ToolInputAnswers is { } byCall && byCall.TryGetValue(callId, out var given))
        {
            answers = given;
        }

        var scope = new McpToolCallScope(_serverName, Name, run, call, canTerminate: loop is not null, answers);

        // 🚨 Opened HERE, in the body: an assignment made in a helper async method does not flow back to
        // this method, and the SDK's handler must see it (docs/hafiza/cekirdek-calistirma.md).
        McpToolCallScope.Open(scope);

        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        // Any exception, not only McpInputPendingException: the SDK is free to wrap what a handler throws,
        // and the scope is the source of truth. A caller's own cancellation still wins.
        catch (Exception) when (scope.Pending is not null && !cancellationToken.IsCancellationRequested)
        {
            return Suspend(scope, loop, run!);
        }
        finally
        {
            scope.Close();
        }
    }

    private static string Suspend(McpToolCallScope scope, FunctionInvocationContext? loop, AgentRunScope run)
    {
        var pending = scope.Pending!;

        // Ledger first, then the stop signal: the run recorder reads the ledger when the loop returns.
        run.ToolInputs!.Add(pending);

        loop?.Terminate = true;

        return new JsonObject
        {
            ["status"] = ToolInputPayload.Status,
            ["requestId"] = pending.RequestId,
            ["replayCallId"] = pending.ReplayCall.CallId,
            ["server"] = pending.ServerName,
            ["tool"] = pending.ToolName,
            ["message"] = pending.Message,
            ["fingerprint"] = pending.Fingerprint,
            ["requestedSchema"] = pending.RequestedSchema.ValueKind == JsonValueKind.Undefined
                ? null
                : JsonNode.Parse(pending.RequestedSchema.GetRawText()),
        }.ToJsonString();
    }
}
