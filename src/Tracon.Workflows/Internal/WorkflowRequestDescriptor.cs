using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Converts a Microsoft Agent Framework external request object into a
/// summary that can be written to and read back from the event stream.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No separate table was opened</strong> for pending requests. A
/// request lives in the payload of a <see cref="RunEventType.WorkflowRequest"/>
/// event; the event stream is already append-only, tenant-filtered, and
/// pageable. A second recording path would keep the same information in two
/// places and let them drift apart over time.
/// </para>
/// <para>
/// The summary is enough to <em>display</em> the request, not to
/// <em>reconstruct</em> it. When answering, the real <see cref="ExternalRequest"/>
/// object is republished by the execution resumed from the checkpoint and
/// matched by id - measured: resuming reissues the same request
/// with the same <c>RequestId</c>.
/// </para>
/// </remarks>
internal static class WorkflowRequestDescriptor
{
    private static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Converts the request into an event payload.</summary>
    /// <param name="request">The Microsoft Agent Framework request.</param>
    /// <param name="presentation">
    /// How a tool approval request is shown, when a presenter gave one; ignored for any other request.
    /// </param>
    /// <param name="agentName">
    /// The agent that asked, when the caller could tell (<see cref="ExecutorIdentityOf"/> names it only by its
    /// identity); ignored for any other request.
    /// </param>
    /// <returns>A JSON summary.</returns>
    public static string Describe(ExternalRequest request, ToolApprovalPresentation? presentation = null, string? agentName = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var facts = ToolApprovalFacts(request, agentName);

        var snapshot = new Snapshot
        {
            RequestId = request.RequestId,
            PortId = request.PortInfo.PortId,
            RequestType = request.PortInfo.RequestType.TypeName,
            ResponseType = request.PortInfo.ResponseType.TypeName,
            ResponseAssembly = request.PortInfo.ResponseType.AssemblyName,
            Prompt = Prompt(request, presentation),
            Form = Form(request),
            ToolName = facts?.ToolName,
            AgentName = facts?.AgentName,
            ToolArgumentsHash = facts?.ArgumentsHash,
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>
    /// Reads what a response to the request recorded in a
    /// <see cref="RunEventType.WorkflowRequest"/> event must carry.
    /// </summary>
    /// <param name="runEvent">The event.</param>
    /// <returns>The recorded request; <see langword="null"/> if the event carries no request id.</returns>
    /// <remarks>
    /// <see cref="WorkflowResponseFactory.Check"/> reads this to refuse a response of the wrong
    /// shape before a run opens. The form is <see langword="null"/> when the payload
    /// lost it (only an event written before the form moved ahead of the prompt can), and the
    /// response type is <see langword="null"/> when the payload predates its assembly name: in
    /// both cases the early check says less, and the republished request is the judge.
    /// </remarks>
    public static RecordedRequest? ReadRecorded(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var (snapshot, requestId) = ReadSnapshot(runEvent);

        if (string.IsNullOrEmpty(requestId))
        {
            return null;
        }

        return new RecordedRequest(
            requestId,
            snapshot?.PortId ?? string.Empty,
            snapshot?.Form,
            snapshot is { ResponseType: { Length: > 0 } typeName, ResponseAssembly: { Length: > 0 } assembly }
                ? new TypeId(assembly, typeName)
                : null);
    }

    /// <summary>
    /// Converts a <see cref="RunEventType.WorkflowRequest"/> event into a pending request.
    /// </summary>
    /// <param name="runEvent">The event.</param>
    /// <returns>The pending request; <see langword="null"/> if the event carries no request id.</returns>
    /// <remarks>
    /// <para>
    /// A payload cut short by <c>MaxPayloadLength</c> is still a pending
    /// request. Answering needs only the request id: the real request
    /// is republished from the checkpoint and matched by it. The old rule
    /// dropped any payload that did not parse, and a long prompt — about 1,400
    /// non-ASCII characters is enough, each one escaped to six — made the
    /// request vanish from the list, and the respond endpoint refused it as
    /// "no pending request": the run could never be answered.
    /// </para>
    /// <para>
    /// The id therefore comes from the event's <see cref="RunEvent.Text"/>,
    /// which carries nothing but the id, and the payload is read as far as it
    /// goes. The payload writes the prompt LAST, so a cut costs only the
    /// prompt's tail, and that tail is marked.
    /// </para>
    /// </remarks>
    public static WorkflowPendingRequest? Parse(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var (snapshot, requestId) = ReadSnapshot(runEvent);

        if (string.IsNullOrEmpty(requestId))
        {
            return null;
        }

        return new WorkflowPendingRequest
        {
            RunId = runEvent.RunId,
            RequestId = requestId,
            PortId = snapshot?.PortId ?? string.Empty,
            RequestType = snapshot?.RequestType,
            ResponseType = snapshot?.ResponseType,
            Prompt = snapshot?.Prompt,
            Form = snapshot?.Form ?? FormFor(snapshot?.RequestType, snapshot?.ResponseType),
            RequestedAt = runEvent.Timestamp,
        };
    }

    /// <summary>
    /// Reads an event's payload and its request id. The id comes from the event's
    /// <see cref="RunEvent.Text"/>, which carries nothing else; see <see cref="Parse"/>.
    /// </summary>
    private static (Snapshot? Snapshot, string? RequestId) ReadSnapshot(RunEvent runEvent)
    {
        var snapshot = runEvent.Payload is { Length: > 0 } payload ? Read(payload) : null;

        return (snapshot, runEvent.Text is { Length: > 0 } text ? text : snapshot?.RequestId);
    }

    /// <summary>Reads a payload, a complete one or one cut short.</summary>
    private static Snapshot? Read(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<Snapshot>(payload, Options);
        }
        catch (JsonException)
        {
            return ReadCut(payload);
        }
    }

    /// <summary>
    /// Reads the properties of a payload that <c>MaxPayloadLength</c> cut
    /// short, up to the cut, and salvages the readable start of a cut prompt.
    /// </summary>
    private static Snapshot ReadCut(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
        var snapshot = new Snapshot();
        string? property = null;

        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    property = reader.GetString();

                    continue;
                }

                snapshot = Assign(snapshot, property, ref reader);
                property = null;
            }
        }
        catch (JsonException)
        {
            // The cut can also land between tokens; what was read so far stands.
        }

        // The reader stops at the first token it cannot finish. When that token
        // is the prompt's value, its readable start is still worth showing.
        if (string.Equals(property, "prompt", StringComparison.Ordinal))
        {
            snapshot = snapshot with { Prompt = SalvagePrompt(bytes, (int)reader.BytesConsumed) };
        }

        return snapshot;
    }

    private static Snapshot Assign(Snapshot snapshot, string? property, ref Utf8JsonReader reader)
        => property switch
        {
            "requestId" when reader.TokenType == JsonTokenType.String => snapshot with { RequestId = reader.GetString() },
            "portId" when reader.TokenType == JsonTokenType.String => snapshot with { PortId = reader.GetString() },
            "requestType" when reader.TokenType == JsonTokenType.String => snapshot with { RequestType = reader.GetString() },
            "responseType" when reader.TokenType == JsonTokenType.String => snapshot with { ResponseType = reader.GetString() },
            "responseAssembly" when reader.TokenType == JsonTokenType.String => snapshot with { ResponseAssembly = reader.GetString() },
            "prompt" when reader.TokenType == JsonTokenType.String => snapshot with { Prompt = reader.GetString() },
            "form" when reader.TokenType == JsonTokenType.String
                && Enum.TryParse<WorkflowRequestForm>(reader.GetString(), ignoreCase: false, out var form)
                && Enum.IsDefined(form) => snapshot with { Form = form },
            _ => snapshot,
        };

    /// <summary>
    /// Decodes the readable start of a string value cut short, and marks it as
    /// cut. An escape sequence the cut split in half is dropped.
    /// </summary>
    private static string? SalvagePrompt(byte[] bytes, int consumed)
    {
        var quote = Array.IndexOf(bytes, (byte)'"', consumed);

        if (quote < 0)
        {
            return null;
        }

        var raw = Encoding.UTF8.GetString(bytes, quote + 1, bytes.Length - quote - 1);

        if (raw.EndsWith(TruncationMarker, StringComparison.Ordinal))
        {
            raw = raw[..^TruncationMarker.Length];
        }

        // A cut can split an escape sequence (a lone backslash, or the "\u00" of
        // "\u00e7"). Trimming at most
        // the six characters an escape is long finds the last decodable end.
        for (var trim = 0; trim <= 6 && trim <= raw.Length; trim++)
        {
            try
            {
                var decoded = JsonSerializer.Deserialize<string>($"\"{raw[..^trim]}\"");

                return decoded is null ? null : decoded + TruncationMarker;
            }
            catch (JsonException)
            {
                // Still inside the split escape; trim one more character.
            }
        }

        return null;
    }

    /// <summary>
    /// The form for a request whose payload lost it to the cut — only an event
    /// written before the form moved ahead of the prompt can lack it.
    /// </summary>
    private static WorkflowRequestForm FormFor(string? requestType, string? responseType)
    {
        if (requestType?.Contains(nameof(MagenticPlanReviewRequest), StringComparison.Ordinal) == true)
        {
            return WorkflowRequestForm.PlanReview;
        }

        if (requestType?.Contains(nameof(ToolApprovalRequestContent), StringComparison.Ordinal) == true)
        {
            return WorkflowRequestForm.ToolApproval;
        }

        if (responseType is null)
        {
            return WorkflowRequestForm.Json;
        }

        if (responseType.StartsWith(typeof(bool).FullName!, StringComparison.Ordinal))
        {
            return WorkflowRequestForm.Boolean;
        }

        return responseType.StartsWith(typeof(string).FullName!, StringComparison.Ordinal) ||
               responseType.Contains(typeof(ChatMessage).FullName!, StringComparison.Ordinal)
            ? WorkflowRequestForm.Text
            : WorkflowRequestForm.Json;
    }

    /// <summary>The longest text a presenter adds to the prompt of a tool approval.</summary>
    private const int PresentationLimit = 500;

    /// <summary>
    /// The text shown with a tool approval request: the tool's name, then what a presenter said about the
    /// call. The arguments are never written; the presenter is the way to show the parts that matter.
    /// </summary>
    /// <param name="toolName">The tool.</param>
    /// <param name="presentation">The presenter's answer, or <see langword="null"/>.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// A presenter's text is bounded: the prompt is the last member of the payload and a payload that outgrows
    /// <c>MaxPayloadLength</c> is cut, and the facts the audit row needs come before it.
    /// </remarks>
    internal static string ToolApprovalPrompt(string toolName, ToolApprovalPresentation? presentation)
    {
        var detail = presentation switch
        {
            { Message: { Length: > 0 } message } => message,
            { EntityName: { Length: > 0 } name } => presentation.EntityType is { Length: > 0 } type ? $"{type} {name}" : name,
            _ => null,
        };

        if (detail is null)
        {
            return toolName;
        }

        // The cut keeps a whole character; the request is stored as JSON, and a lone
        // high surrogate in it is not text.
        var kept = SurrogateSafeText.Prefix(detail, PresentationLimit);

        return kept.Length < detail.Length
            ? $"{toolName}: {kept}…"
            : $"{toolName}: {detail}";
    }

    /// <summary>The marker <c>RunEventWriter</c> appends to a value it cut.</summary>
    private const string TruncationMarker = "…[truncated]";

    /// <summary>Reports whether the request is a Magentic plan review.</summary>
    /// <param name="request">The Microsoft Agent Framework request.</param>
    /// <returns><see langword="true"/> when the port declares the plan-review request type.</returns>
    /// <remarks>
    /// <para>
    /// Decided by the PORT'S declared request type, never by what the data deserializes into. A
    /// request that comes back from a checkpoint holds its data as JSON, and
    /// <c>ExternalRequest.TryGetDataAs</c> then <em>tries to deserialize it into whatever type it is asked
    /// for</em>: a code graph's own request record (<c>{"text": "...", "violation": null}</c>) reads into
    /// <see cref="MagenticPlanReviewRequest"/> without an error, because every member of it is optional. The
    /// run that answered was built a plan-review response, and Microsoft Agent Framework refused it with
    /// "Message type MagenticPlanReviewResponse does not match expected response type". The first run held the
    /// live object, so the defect showed only on the run that ANSWERS.
    /// </para>
    /// </remarks>
    internal static bool IsPlanReview(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.PortInfo.RequestType.IsMatch<MagenticPlanReviewRequest>();
    }

    /// <summary>Reports whether the request is a tool call waiting for approval.</summary>
    /// <param name="request">The Microsoft Agent Framework request.</param>
    /// <returns><see langword="true"/> when the port declares the tool-approval request type.</returns>
    /// <remarks>
    /// Decided by the PORT'S declared request type, for the reason <see cref="IsPlanReview"/> gives: data that
    /// came back from a checkpoint is JSON, and a read into a type answers by shape, not by what was published.
    /// </remarks>
    internal static bool IsToolApproval(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.PortInfo.RequestType.IsMatch<ToolApprovalRequestContent>();
    }

    /// <summary>Chooses the input form the UI should ask for, based on the port's response type.</summary>
    /// <param name="request">The Microsoft Agent Framework request.</param>
    /// <returns>The input form.</returns>
    public static WorkflowRequestForm Form(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsPlanReview(request))
        {
            return WorkflowRequestForm.PlanReview;
        }

        if (IsToolApproval(request))
        {
            return WorkflowRequestForm.ToolApproval;
        }

        // Every request answered with a message list - including declarative
        // workflows - goes through this UI; a text box is the right question.
        if (request.TryGetDataAs<IExternalRequestEnvelope>(out _))
        {
            return WorkflowRequestForm.Text;
        }

        if (request.PortInfo.ResponseType.IsMatch<bool>())
        {
            return WorkflowRequestForm.Boolean;
        }

        return request.PortInfo.ResponseType.IsMatch<string>()
            ? WorkflowRequestForm.Text
            : WorkflowRequestForm.Json;
    }

    /// <summary>
    /// Reads what a tool approval records about the call; <see langword="null"/> for any other request.
    /// </summary>
    private static WorkflowToolApprovalFacts? ToolApprovalFacts(ExternalRequest request, string? agentName)
        => IsToolApproval(request) && request.TryGetDataAs<ToolApprovalRequestContent>(out var approval)
            ? WorkflowToolApprovalFacts.Of(approval, agentName)
            : null;

    /// <summary>The suffix Microsoft Agent Framework gives the port of an agent host's tool approvals.</summary>
    private const string UserInputPortSuffix = "_UserInput";

    /// <summary>The length of the identity that follows the agent's name in an executor id: <c>_</c> and 32 hex digits.</summary>
    private const int ExecutorIdentityLength = 33;

    /// <summary>
    /// Reads the identity of the agent that asked from the port of its host. The port is
    /// <c>{executor id}_UserInput</c> and the executor id is <c>{agent name}_{32 hex digits}</c>
    /// (<see cref="WorkflowAgentIdentity"/>).
    /// </summary>
    /// <param name="portId">The port that published the request.</param>
    /// <returns>The 32 hexadecimal digits; <see langword="null"/> when the port does not have that form.</returns>
    /// <remarks>
    /// Only the identity is read, never the name before it: the framework replaces the characters it dislikes in
    /// the name, so <c>approval-agent</c> reads back as <c>approval_agent</c>. <see cref="WorkflowAgentCache.NameOf"/>
    /// turns the identity into the real name.
    /// </remarks>
    internal static string? ExecutorIdentityOf(string portId)
    {
        if (!portId.EndsWith(UserInputPortSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var executor = portId.AsSpan(0, portId.Length - UserInputPortSuffix.Length);

        if (executor.Length <= ExecutorIdentityLength ||
            executor[^ExecutorIdentityLength] != '_' ||
            !IsHexadecimal(executor[^(ExecutorIdentityLength - 1)..]))
        {
            return null;
        }

        return executor[^(ExecutorIdentityLength - 1)..].ToString();
    }

    private static bool IsHexadecimal(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Extracts the request text to show the user.</summary>
    private static string? Prompt(ExternalRequest request, ToolApprovalPresentation? presentation)
    {
        if (IsToolApproval(request) && request.TryGetDataAs<ToolApprovalRequestContent>(out var approval))
        {
            return ToolApprovalPrompt(WorkflowToolApprovalFacts.Of(approval, agentName: null).ToolName, presentation);
        }

        if (IsPlanReview(request) && request.TryGetDataAs<MagenticPlanReviewRequest>(out var review))
        {
            var plan = review.Plan.Text;

            return review.IsStalled
                ? $"Execution stalled and the plan was rebuilt.{Environment.NewLine}{plan}"
                : plan;
        }

        if (request.TryGetDataAs<IExternalRequestEnvelope>(out var envelope))
        {
            return envelope.GetInnerRequestContent() is TextContent text ? text.Text : null;
        }

        if (request.TryGetDataAs<string>(out var value))
        {
            return value;
        }

        if (request.PortInfo.RequestType.IsMatch<ChatMessage>() && request.TryGetDataAs<ChatMessage>(out var message))
        {
            return message.Text;
        }

        // Dumping an unknown payload with ToString() would bloat the event
        // table; the UI can still draw a meaningful card with just the type name.
        return request.PortInfo.RequestType.TypeName;
    }

    /// <summary>The wire format of the event payload.</summary>
    /// <remarks>
    /// The order is part of the format: <see cref="Prompt"/>, the only field
    /// whose length the request decides, is written LAST, so a payload cut at
    /// <c>MaxPayloadLength</c> loses nothing but the prompt's tail.
    /// <see cref="ResponseAssembly"/> is the assembly half of the port's
    /// response type: with <see cref="ResponseType"/> it resolves the type a
    /// <c>data</c> answer must read into, before a run opens. An event written
    /// before it existed has none, and only that check is skipped.
    /// </remarks>
    private sealed record Snapshot
    {
        [JsonPropertyOrder(0)]
        public string? RequestId { get; init; }

        [JsonPropertyOrder(1)]
        public string? PortId { get; init; }

        [JsonPropertyOrder(2)]
        public string? RequestType { get; init; }

        [JsonPropertyOrder(3)]
        public string? ResponseType { get; init; }

        [JsonPropertyOrder(4)]
        public string? ResponseAssembly { get; init; }

        [JsonPropertyOrder(5)]
        public WorkflowRequestForm? Form { get; init; }

        [JsonPropertyOrder(6)]
        [JsonPropertyName(WorkflowToolApprovalFacts.ToolNameMember)]
        public string? ToolName { get; init; }

        [JsonPropertyOrder(7)]
        [JsonPropertyName(WorkflowToolApprovalFacts.AgentNameMember)]
        public string? AgentName { get; init; }

        [JsonPropertyOrder(8)]
        [JsonPropertyName(WorkflowToolApprovalFacts.ArgumentsHashMember)]
        public string? ToolArgumentsHash { get; init; }

        [JsonPropertyOrder(9)]
        public string? Prompt { get; init; }
    }
}

/// <summary>What a response to a recorded request must carry.</summary>
/// <param name="RequestId">The request id.</param>
/// <param name="PortId">The port that raised it; empty when the payload lost it.</param>
/// <param name="Form">The answer form; <see langword="null"/> when the payload lost it.</param>
/// <param name="ResponseType">
/// The port's response type; <see langword="null"/> when the payload predates its assembly name.
/// </param>
internal sealed record RecordedRequest(string RequestId, string PortId, WorkflowRequestForm? Form, TypeId? ResponseType);
