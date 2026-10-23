using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// A request for user input (MCP elicitation) that suspended a tool call. The run that met it
/// closes <see cref="RunStatus.AwaitingInput"/>; the answer is the next turn's input.
/// </summary>
/// <remarks>
/// Written by the MCP tool wrapper into the run's <c>ToolInputLedger</c> and read by the run
/// recorder and the endpoint that announces it. It is read from the ledger, never parsed back
/// out of the tool result: a server can return any text as a result, and text that only looks
/// like a pending request must not close a run.
/// </remarks>
internal sealed record PendingToolInput
{
    /// <summary>Gets the identity Tracon gave the request; the next turn answers it by this value.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the call identity of the suspended tool call.</summary>
    public required string CallId { get; init; }

    /// <summary>
    /// Gets the call that runs the suspended tool call again once the user answers: the same tool and the
    /// same arguments under a new call identity, so the history never holds two results for one call.
    /// </summary>
    /// <remarks>
    /// Tracon records it with the session as a pending request when the run closes, which is what lets
    /// Microsoft Agent Framework's function-invocation loop run it on the answering turn
    /// (Phase 235, <c>ToolInputSessionBridge</c>).
    /// </remarks>
    public required FunctionCallContent ReplayCall { get; init; }

    /// <summary>Gets the qualified name of the tool that was suspended (<c>{server}_{tool}</c>).</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the name of the MCP server that asked.</summary>
    public required string ServerName { get; init; }

    /// <summary>Gets the text the server asks the user to read.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the flat JSON schema of the fields the server asks for.</summary>
    public JsonElement RequestedSchema { get; init; }

    /// <summary>
    /// Gets the digest of the message and the schema the server sent. The replayed call applies an
    /// answer only to a request with the same digest: the same tool can ask something else the second
    /// time, and an old answer must never reach a question it was not given to.
    /// </summary>
    public required string Fingerprint { get; init; }
}

/// <summary>The user's answer to one <see cref="PendingToolInput"/>.</summary>
/// <param name="Fingerprint">The <see cref="PendingToolInput.Fingerprint"/> of the request the user answered.</param>
/// <param name="Action"><c>accept</c>, <c>decline</c> or <c>cancel</c>.</param>
/// <param name="Content">The field values; only with <c>accept</c>.</param>
internal sealed record ToolInputAnswer(string Fingerprint, string Action, IReadOnlyDictionary<string, JsonElement>? Content);

/// <summary>The values of <see cref="ToolInputAnswer.Action"/>.</summary>
internal static class ToolInputActions
{
    /// <summary>The user filled in the form.</summary>
    public const string Accept = "accept";

    /// <summary>The user refused to answer.</summary>
    public const string Decline = "decline";

    /// <summary>The user dismissed the request.</summary>
    public const string Cancel = "cancel";
}

/// <summary>The shape of the result a suspended tool call returns, shared by the writer and the reader of it.</summary>
internal static class ToolInputPayload
{
    /// <summary>The <c>status</c> value of the result.</summary>
    public const string Status = "input_required";
}
