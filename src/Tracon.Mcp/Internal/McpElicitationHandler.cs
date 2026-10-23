using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Tracon;

/// <summary>
/// Answers an MCP server's request for user input (elicitation) on one connection.
/// </summary>
/// <remarks>
/// <para>
/// The handler decides in a fixed order and the first matching row wins; no row ends in silence. A request
/// that cannot reach a user gets <c>decline</c>, which is a complete answer: the server decides what the tool
/// does next. A request that can reach one suspends the tool call (<see cref="McpInputPendingException"/>) and
/// the run ends waiting; the user's answer comes back as the next turn's input and the call runs again.
/// </para>
/// <list type="number">
/// <item>No call scope, or the call ended (the older protocol's direct request, delivered from the connection's
/// receive loop): <c>decline</c>.</item>
/// <item>The server asked to open an address (<c>url</c> mode): <c>decline</c>; Tracon asks for form fields only.</item>
/// <item>No run, an agent below the root of a tree, a run without a session, or a surface that cannot
/// carry the answer: <c>decline</c>.</item>
/// <item>The replayed call holds the answer to this very request: the answer.</item>
/// <item>The replayed call asked something else than the user was asked: <c>decline</c>.</item>
/// <item>Otherwise: suspend.</item>
/// </list>
/// </remarks>
internal sealed class McpElicitationHandler
{
    private readonly string _serverName;
    private readonly ILogger _logger;

    /// <summary>Creates the handler of one connection.</summary>
    /// <param name="serverName">The MCP server's name, for the log of a request that has no call scope.</param>
    /// <param name="logger">The logger a decline is reported to.</param>
    public McpElicitationHandler(string serverName, ILogger logger)
    {
        _serverName = serverName;
        _logger = logger;
    }

    /// <summary>The reason codes of <see cref="RunEventCustomTypes.ToolInputDeclined"/>.</summary>
    internal static class Reasons
    {
        public const string NoRun = "no_run";
        public const string UrlMode = "url_mode";
        public const string ChildRun = "child_run";
        public const string NoSession = "no_session";
        public const string NoChannel = "no_channel";
        public const string NoTermination = "no_termination";
        public const string Mismatch = "mismatch";
    }

    /// <summary>Handles one elicitation request.</summary>
    /// <param name="request">The server's request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer to send the server.</returns>
    /// <exception cref="McpInputPendingException">The call must be suspended until the user answers.</exception>
    public async ValueTask<ElicitResult> HandleAsync(ElicitRequestParams? request, CancellationToken cancellationToken)
    {
        var scope = McpToolCallScope.Current;

        if (scope is not { IsActive: true })
        {
            return await DeclineAsync(scope, Reasons.NoRun).ConfigureAwait(false);
        }

        if (request is null || !string.Equals(request.Mode, "form", StringComparison.Ordinal))
        {
            return await DeclineAsync(scope, Reasons.UrlMode).ConfigureAwait(false);
        }

        if (scope.Run is not { } run)
        {
            return await DeclineAsync(scope, Reasons.NoRun).ConfigureAwait(false);
        }

        if (run.Depth > 0)
        {
            return await DeclineAsync(scope, Reasons.ChildRun).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(run.SessionId))
        {
            return await DeclineAsync(scope, Reasons.NoSession).ConfigureAwait(false);
        }

        if (!run.AcceptsToolInput || run.ToolInputs is null)
        {
            return await DeclineAsync(scope, Reasons.NoChannel).ConfigureAwait(false);
        }

        if (!scope.CanTerminate || scope.Call is null)
        {
            return await DeclineAsync(scope, Reasons.NoTermination).ConfigureAwait(false);
        }

        var schema = SerializeSchema(request.RequestedSchema);
        var fingerprint = Fingerprint(request.Message, schema);

        if (scope.TryTakeAnswer(fingerprint, out var answer))
        {
            return new ElicitResult
            {
                Action = answer.Action,
                Content = answer.Content is null ? null : new Dictionary<string, JsonElement>(answer.Content, StringComparer.Ordinal),
            };
        }

        if (scope.HasAnswers)
        {
            // The call came with answers and still asks something they do not answer. Applying an old
            // answer to a different question could hand a user's "yes" to a request they never saw, and
            // asking again could loop forever; the server is told "no" and decides.
            return await DeclineAsync(scope, Reasons.Mismatch).ConfigureAwait(false);
        }

        var original = scope.Call!;

        scope.Suspend(new PendingToolInput
        {
            RequestId = TraconId.NewId().ToString("D"),
            CallId = original.CallId,
            ReplayCall = new FunctionCallContent(
                "call_" + TraconId.NewId().ToString("N"),
                original.Name,
                original.Arguments is null ? null : new Dictionary<string, object?>(original.Arguments, StringComparer.Ordinal)),
            ToolName = scope.ToolName,
            ServerName = scope.ServerName,
            Message = request.Message,
            RequestedSchema = schema,
            Fingerprint = fingerprint,
        });

        cancellationToken.ThrowIfCancellationRequested();

        throw new McpInputPendingException();
    }

    /// <summary>The digest of what the server asked: its message and its schema.</summary>
    /// <param name="message">The text shown to the user.</param>
    /// <param name="schema">The requested schema as the SDK serializes it.</param>
    /// <returns>The uppercase hex SHA-256.</returns>
    internal static string Fingerprint(string message, JsonElement schema)
    {
        var text = string.Concat(message, "\n", schema.ValueKind == JsonValueKind.Undefined ? string.Empty : schema.GetRawText());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static JsonElement SerializeSchema(ElicitRequestParams.RequestSchema? schema)
    {
        if (schema is null)
        {
            using var empty = JsonDocument.Parse("""{"type":"object","properties":{}}""");

            return empty.RootElement.Clone();
        }

        return JsonSerializer.SerializeToElement(schema, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ElicitRequestParams.RequestSchema)));
    }

    private async ValueTask<ElicitResult> DeclineAsync(McpToolCallScope? scope, string reason)
    {
        var server = scope?.ServerName ?? _serverName;

        _logger.LogWarning(
            "MCP server '{ServerName}' asked for user input and the request was declined ({Reason}).",
            server,
            reason);

        if (scope?.Run?.Writer is { } writer)
        {
            // Not the call's token: the decline already happened, and observability must never cost it.
            await writer.AppendReservedAsync(
                new RunEventDraft(RunEventType.Custom)
                {
                    CustomType = RunEventCustomTypes.ToolInputDeclined,
                    ToolName = scope.ToolName,
                    ToolCallId = scope.CallId,
                    Text = $"{server}: {reason}",
                    Payload = new JsonObject
                    {
                        ["server"] = server,
                        ["tool"] = scope.ToolName,
                        ["reason"] = reason,
                    }.ToJsonString(),
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        return new ElicitResult { Action = "decline" };
    }
}
