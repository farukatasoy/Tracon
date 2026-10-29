using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Validation for the <c>CustomType</c> string that qualifies a
/// <see cref="RunEventType.Custom"/> event.
/// </summary>
/// <remarks>
/// Repeats the <see cref="JobHandlerKeys"/> pattern: a namespaced, lowercase
/// ASCII string with a reserved prefix Tracon itself never emits, so a
/// future built-in custom type can never collide with a consumer's own.
/// </remarks>
public static partial class RunEventCustomTypes
{
    /// <summary>The prefix reserved for Tracon's own use.</summary>
    /// <remarks>
    /// A consumer's <c>CustomType</c> starting with this prefix is rejected
    /// at write time — not because Tracon ships a built-in custom type
    /// today, but so one could be added later without ever colliding with a
    /// consumer's own.
    /// </remarks>
    public const string ReservedPrefix = "tracon.";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the quota threshold
    /// notice a run's completion writes into its own event stream when its consumption
    /// crossed the threshold: the root run, or a child run of its call tree.
    /// </summary>
    /// <remarks>
    /// Falls under <see cref="ReservedPrefix"/>: a consumer cannot write an
    /// event under this type, so a client can trust that every event carrying
    /// it came from Tracon itself, not from the agent's own tool code.
    /// Off by default (<c>TraconQuotaOptions.PublishThresholdToRunStream</c>).
    /// </remarks>
    public const string QuotaThreshold = ReservedPrefix + "quota.threshold";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the notice a run writes when
    /// the model stopped a plain-text response at its output token limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run does not fail: a partial text answer is still useful, so the run
    /// keeps its status and this event says the answer is incomplete. A structured
    /// (JSON) response cut off the same way fails its run with
    /// <see cref="RunErrorClass.OutputTruncated"/> instead, because a partial JSON
    /// document has no use.
    /// </para>
    /// <para>
    /// The payload carries the provider name, what cut the answer off
    /// (<c>cause</c>: <c>output_limit</c> or <c>context_window</c>) and, when the
    /// binding set one, the output token limit. It never carries the response text.
    /// Written only for the agent's own model calls, not for a side-channel call such
    /// as a context summarizer or a loop judge.
    /// </para>
    /// <para>
    /// The live stream of an agent run (<c>POST /api/agents/{name}/run</c> without
    /// <c>Idempotency-Key</c>) also carries this event, always: it arrives as a
    /// <c>custom</c> frame right before <c>done</c>, with the same data
    /// <c>GET /api/runs/{id}/events</c> serves. Each recorded event is one frame, so a
    /// harness loop whose iterations were cut sends more than one. No event is
    /// written, and no frame is sent, when run recording is off.
    /// </para>
    /// </remarks>
    public const string OutputTruncated = ReservedPrefix + "output.truncated";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the notice a run writes when the
    /// provider's content filter stopped the model in the middle of an answer that already
    /// has text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run does not fail: the partial answer is still useful, so the run keeps its
    /// status and this event says the answer is incomplete. A filtered answer with
    /// <strong>no</strong> content at all fails its run with
    /// <see cref="RunErrorClass.ContentFiltered"/> instead, because there is nothing to keep.
    /// A structured (JSON) response filtered the same way gets this event too; whether
    /// the run then fails is still the structured-response validator's decision, as it is
    /// for any other response.
    /// </para>
    /// <para>
    /// The payload is a JSON object with <c>provider</c>, the provider of the agent's model
    /// binding (behind a fallback chain this is the binding's own provider, not necessarily
    /// the link that answered). It never carries the response text. The filter's own
    /// word (Anthropic <c>refusal</c>, Gemini <c>SAFETY</c>) is not in it: the provider
    /// adapters report every filter stop as the one common finish reason before Tracon sees
    /// it. Written only for the agent's own model calls, not for a side-channel call such
    /// as a context summarizer or a loop judge.
    /// </para>
    /// <para>
    /// The live stream of an agent run (<c>POST /api/agents/{name}/run</c> without
    /// <c>Idempotency-Key</c>) also carries this event, always: it arrives as a
    /// <c>custom</c> frame right before <c>done</c>, with the same data
    /// <c>GET /api/runs/{id}/events</c> serves, and the same list holds
    /// <see cref="OutputTruncated"/>. No event is written, and no frame is sent, when run
    /// recording is off. On <c>POST /v1/responses</c> the same stop makes the response
    /// <c>incomplete</c> with the reason <c>content_filter</c>.
    /// </para>
    /// </remarks>
    public const string OutputFiltered = ReservedPrefix + "output.filtered";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the event a run writes for model
    /// content that no other event type covers: a hosted tool call or result, a provider
    /// <c>ErrorContent</c>, generated or attached data, a file reference, and any content
    /// type Tracon does not know yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Text</c> is a short summary such as <c>web_search call</c> or <c>error: rate_limited</c>.
    /// The payload is a JSON object with a <c>type</c> key and metadata, plus one bounded text:
    /// <c>hosted_tool_call</c> and <c>hosted_tool_result</c> carry <c>tool</c>
    /// (<c>web_search</c>, <c>code_interpreter</c>, <c>image_generation</c> or
    /// <c>mcp_server</c>) and <c>callId</c>; <c>error</c> carries <c>errorCode</c> and
    /// <c>message</c>; <c>data</c> carries <c>mediaType</c> and <c>length</c> in bytes;
    /// <c>uri</c> carries <c>mediaType</c>; <c>hosted_file</c> and
    /// <c>hosted_vector_store</c> carry <c>id</c>; <c>unrecognized</c> carries
    /// <c>clrType</c>. It never carries the content itself: no data bytes, no address of
    /// a <c>uri</c> (a signed URL can hold a credential), no tool inputs or outputs. The one
    /// free text is the provider's <c>message</c> of an <c>error</c>, cut at 2 048 characters
    /// (and shorter when <c>MaxPayloadLength</c> needs it) so the JSON stays whole.
    /// </para>
    /// <para>
    /// Unlike <see cref="QuotaThreshold"/> and <see cref="OutputTruncated"/>, the payload
    /// follows <c>TraconRunRecordingOptions.RecordToolPayloads</c>: with it off the event
    /// stays and <c>Text</c> stays, but <c>Payload</c> is <see langword="null"/>. Plain
    /// model text and reasoning, tool calls and results, and usage and approval content
    /// keep their own events or accounting and never produce this event. The live stream
    /// of an agent run does not carry it; read it from <c>GET /api/runs/{id}/events</c>.
    /// </para>
    /// </remarks>
    public const string Content = ReservedPrefix + "content";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the event a workflow run writes for a
    /// <c>WorkflowEvent</c> that Tracon has no event type for, such as a custom event type
    /// an executor of a code workflow raises.
    /// </summary>
    /// <remarks>
    /// <c>Text</c> is the CLR type name of the event. The payload is a JSON object with
    /// <c>eventType</c> (the full type name), <c>executorId</c> when the event type derives
    /// from the framework's executor event, and <c>data</c>: the text of a string or chat
    /// message payload (cut at 2 048 characters), or only the type name of any other payload,
    /// never its <c>ToString()</c>. The payload follows
    /// <c>TraconRunRecordingOptions.RecordToolPayloads</c> like <see cref="Content"/>. A
    /// workflow run's stream sends the event as an <c>event</c> frame like every recorded
    /// event.
    /// </remarks>
    public const string WorkflowEvent = ReservedPrefix + "workflow.event";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the event a run writes when an MCP server asked
    /// the user for input in the middle of a tool call and Tracon answered the server with
    /// <c>decline</c> instead of asking the user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payload is a JSON object with <c>server</c> (the MCP server's name), <c>tool</c> (the
    /// qualified tool name) and <c>reason</c>: <c>no_run</c> (the request reached the client outside
    /// a tool call that is still running), <c>url_mode</c> (the server asked to open an address;
    /// Tracon asks only for form fields), <c>child_run</c> (an agent called by another agent cannot
    /// ask the user, the same as for approvals), <c>no_session</c> (the run has no <c>sessionId</c>,
    /// so the answer has nowhere to go; an OpenAI-compatible Chat Completions call and an MCP tool
    /// call never have one), <c>no_channel</c> (the run has a session, but the surface that started
    /// it cannot carry the answer: an A2A task, a Responses call with a conversation, a queued run),
    /// <c>no_termination</c> (the tool was not called by the function-invocation loop) or
    /// <c>mismatch</c> (the replayed call asked something other than the user was asked). It never
    /// carries the requested fields or any answer. Declining is a complete answer to the server; the
    /// server decides what the tool does next. A request that reaches the client outside any run
    /// (a server on the earlier protocol sends one that way) has no run to hold the event and is
    /// only logged.
    /// </para>
    /// </remarks>
    public const string ToolInputDeclined = ReservedPrefix + "tool.input.declined";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the notice a run records when the front end of an AG-UI request
    /// declared tools the agent does not carry as client-side tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An AG-UI front end sends all of its tools with every request. Tracon runs only the client-side tools that
    /// are registered in code and named in the agent's definition (<c>AddClientTool</c>); the schema comes from the
    /// code, never from the request. A declared tool outside that set is ignored, and this event says so: the model
    /// never saw it.
    /// </para>
    /// <para>
    /// The payload is a JSON object with <c>tools</c>, the ignored names, capped at 50 names of at most 128
    /// characters each. It never carries a schema or a description from the request.
    /// </para>
    /// </remarks>
    public const string AGUIToolsIgnored = ReservedPrefix + "agui.tools.ignored";

    /// <summary>
    /// Checks whether <paramref name="customType"/> is a valid custom event
    /// type: 1-128 characters, lowercase ASCII letters, digits, <c>.</c>,
    /// <c>_</c>, or <c>-</c>, starting with a letter or digit.
    /// </summary>
    /// <param name="customType">The candidate type.</param>
    /// <returns><see langword="true"/> if the type is valid.</returns>
    /// <remarks>
    /// Uppercase letters are rejected, not normalized — the same reasoning as
    /// <see cref="JobHandlerKeys.IsValidKey"/>. A consumer that read back its
    /// own event compares the type ordinally, so allowing case variants would
    /// let <c>"Contoso.Preview"</c> and <c>"contoso.preview"</c> silently
    /// become two different types.
    /// </remarks>
    public static bool IsValidType(string? customType) => customType is not null && TypePattern().IsMatch(customType);

    /// <summary>
    /// Checks whether <paramref name="customType"/> falls inside the
    /// <see cref="ReservedPrefix"/> namespace.
    /// </summary>
    /// <param name="customType">The candidate type.</param>
    /// <returns><see langword="true"/> if the type is reserved for Tracon.</returns>
    public static bool IsReserved(string? customType)
        => customType is not null && customType.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TypePattern();
}
