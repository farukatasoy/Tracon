using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Maps what an AG-UI front end sends onto what a Tracon run takes: the new message, the answers to pending calls and
/// requests, the documents, and the tools the front end can run.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The server's history is the truth.</strong> An AG-UI front end sends the whole conversation with every request;
/// Tracon keeps the conversation itself, in the session. Only the messages after the LAST assistant message are new input:
/// the user's text and the results of the client-side tool calls that message made. Everything before it is already in the
/// session, and adding it again would show the model every earlier turn twice.
/// </para>
/// <para>
/// <strong>Nothing the request says reaches the model as a tool, an instruction or a schema.</strong> The tools a run can
/// call are the ones registered in code; <c>tools</c> only says which client-side tools this front end can run, and the schema is
/// the code's. System and developer messages are not read for the same reason: the agent's instructions are the agent's.
/// </para>
/// </remarks>
internal static class AGUIInputMapper
{
    /// <summary>The longest thread identity accepted: the sessions table of SQL Server holds <c>nvarchar(200)</c>.</summary>
    internal const int MaxThreadIdLength = 200;

    /// <summary>The longest list of ignored tool names a run records.</summary>
    internal const int MaxIgnoredToolNames = 50;

    /// <summary>The longest ignored tool name a run records.</summary>
    internal const int MaxIgnoredToolNameLength = 128;

    /// <summary>The reason an approval carries when the user dismissed the interrupt without deciding.</summary>
    internal const string DismissedReason = "The user dismissed the approval request.";

    /// <summary>Refuses a request whose thread cannot be a session or whose lists carry a null element.</summary>
    /// <param name="input">The request body.</param>
    /// <returns>The refusal, or <see langword="null"/> when the request can be mapped.</returns>
    public static IResult? Validate(RunAgentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ThreadId))
        {
            return Results.Problem(
                title: "Thread required",
                detail: "'threadId' is required: the thread is the session the run belongs to.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (input.ThreadId.Length > MaxThreadIdLength)
        {
            return Results.Problem(
                title: "Thread identity too long",
                detail: $"'threadId' cannot exceed {MaxThreadIdLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The protocol SDK's serializer does not reject a null list element, and the mapping below reads every element.
        // The management API refuses a null where its contract has none (RespectNullableAnnotations); so does this.
        foreach (var (name, hasNull) in new (string, bool)[]
                 {
                     ("messages", input.Messages?.Any(static message => message is null) == true),
                     ("tools", input.Tools?.Any(static tool => tool is null) == true),
                     ("context", input.Context?.Any(static context => context is null) == true),
                     ("resume", input.Resume?.Any(static entry => entry is null) == true),
                 })
        {
            if (hasNull)
            {
                return Results.Problem(
                    title: RequestBodyBinding.ProblemTitle,
                    detail: $"'{name}' cannot contain a null element.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return null;
    }

    /// <summary>Reads the new input of the request: the message, the client-side tool results and the documents.</summary>
    /// <param name="input">The request body.</param>
    /// <returns>The run request without the answers to approvals and requests for input.</returns>
    public static AgentRunRequest MapQueue(RunAgentInput input)
    {
        var messages = input.Messages ?? [];

        var tailStart = 0;

        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index] is AGUIAssistantMessage)
            {
                tailStart = index + 1;

                break;
            }
        }

        var texts = new List<string>();
        var results = new List<ClientToolResult>();

        for (var index = tailStart; index < messages.Count; index++)
        {
            switch (messages[index])
            {
                case AGUIUserMessage user when TextOf(user.Content) is { Length: > 0 } text:
                    texts.Add(text);

                    break;

                case AGUIToolMessage { ToolCallId: { Length: > 0 } callId } tool:
                    results.Add(new ClientToolResult
                    {
                        CallId = callId,
                        Result = string.IsNullOrEmpty(tool.Error) ? TextOf(tool.Content) ?? string.Empty : null,
                        ErrorMessage = string.IsNullOrEmpty(tool.Error) ? null : tool.Error,
                    });

                    break;

                default:
                    // System, developer, activity and reasoning messages are the front end's own. The agent's
                    // instructions are the agent's.
                    break;
            }
        }

        var documents = (input.Context ?? [])
            .Where(static context => !string.IsNullOrWhiteSpace(context.Value))
            .Select(static context => new AgentRunDocument
            {
                Name = string.IsNullOrWhiteSpace(context.Description) ? "context" : context.Description,
                Content = context.Value,
            })
            .ToArray();

        return new AgentRunRequest
        {
            SessionId = input.ThreadId,
            Message = texts.Count == 0 ? null : string.Join("\n\n", texts),
            ToolResults = results,
            Documents = documents,
        };
    }

    /// <summary>
    /// Checks the tools the front end declared against the client-side tools the agent carries.
    /// </summary>
    /// <param name="agentName">The agent.</param>
    /// <param name="input">The request body.</param>
    /// <param name="httpContext">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The refusal when the front end does not declare a client-side tool the agent carries, otherwise the declared names the
    /// agent does not carry.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The model sees a client-side tool because the agent's definition names it and the code registered it
    /// (<c>AddClientTool</c>), not because a request declared it: an AG-UI request cannot add a tool or change a schema.
    /// What the request decides is only whether this front end can RUN the tool. A tool the agent carries and the front end
    /// did not declare would reach the model and then wait for a result nobody can produce, and a run cannot hide a tool of
    /// its definition from the model, so the request is refused instead and names the missing tools.
    /// </para>
    /// <para>
    /// A declared tool the agent does not carry is ignored: a front end sends all of its tools with every request, and
    /// refusing it would break the common case. The run records the names.
    /// </para>
    /// </remarks>
    public static async ValueTask<ToolBoundary> CheckToolsAsync(
        string agentName,
        RunAgentInput input,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;

        var descriptor = await AgentEndpoints
            .FindDescriptorAsync(services.GetRequiredService<IAgentCatalog>(), agentName, cancellationToken)
            .ConfigureAwait(false);

        // An unknown agent is answered 404 by the turn preparation that follows.
        if (descriptor is null)
        {
            return ToolBoundary.None;
        }

        var carried = services.GetRequiredService<IToolRegistry>().List()
            .Where(tool => tool.RunsOnClient && descriptor.ToolNames.Contains(tool.Name, StringComparer.Ordinal))
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        var declared = (input.Tools ?? [])
            .Select(static tool => tool.Name)
            .Where(static name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal);

        var missing = carried.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal).ToArray();

        if (missing.Length > 0)
        {
            return ToolBoundary.Refused(Results.Problem(
                title: "Client tools not declared",
                detail: $"Agent '{agentName}' carries client-side tools that run on the front end: " +
                        $"{string.Join(", ", missing.Select(static name => $"'{name}'"))}. Declare them in 'tools', " +
                        "or the model would call a tool nobody can run.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var ignored = declared.Where(name => !carried.Contains(name)).Order(StringComparer.Ordinal).ToArray();

        return ToolBoundary.Allowed(ignored);
    }

    /// <summary>
    /// Maps the <c>resume</c> entries onto approval decisions and tool input answers.
    /// </summary>
    /// <param name="input">The request body.</param>
    /// <param name="httpContext">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decisions and answers, or the refusal.</returns>
    /// <remarks>
    /// An interrupt is answered by its identity alone, so the session says which kind it was: the tool approvals and the tool
    /// input requests it waits for are read from its history. An identity that is neither is refused, which is also what a
    /// second answer to an interrupt that was answered already is.
    /// </remarks>
    public static async ValueTask<ResumeMapping> MapResumeAsync(
        RunAgentInput input,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var resume = input.Resume ?? [];

        if (resume.Count == 0)
        {
            return ResumeMapping.None;
        }

        var pending = await PendingInterruptReader
            .ReadAsync(httpContext, input.ThreadId, cancellationToken)
            .ConfigureAwait(false);

        var approvals = new List<ToolApprovalDecision>();
        var answers = new List<ToolInputResponse>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in resume)
        {
            if (string.IsNullOrEmpty(entry.InterruptId))
            {
                return ResumeMapping.Refused("Every 'resume' entry needs an 'interruptId'.");
            }

            if (!seen.Add(entry.InterruptId))
            {
                return ResumeMapping.Refused($"The interrupt '{entry.InterruptId}' is answered more than once in this request.");
            }

            var cancelled = string.Equals(entry.Status, ResumeStatus.Cancelled, StringComparison.Ordinal);

            if (!cancelled && !string.Equals(entry.Status, ResumeStatus.Resolved, StringComparison.Ordinal))
            {
                return ResumeMapping.Refused(
                    $"The 'status' of the interrupt '{entry.InterruptId}' must be '{ResumeStatus.Resolved}' or '{ResumeStatus.Cancelled}'.");
            }

            if (pending.ApprovalRequestIds.Contains(entry.InterruptId))
            {
                if (cancelled)
                {
                    approvals.Add(new ToolApprovalDecision { RequestId = entry.InterruptId, Approved = false, Reason = DismissedReason });

                    continue;
                }

                if (ReadApproval(entry) is not { } decision)
                {
                    return ResumeMapping.Refused(
                        $"The answer to the approval '{entry.InterruptId}' must be an object with a boolean 'approved'; " +
                        "'reason' must be a string, and 'remember' and 'rememberArgumentsOnly' must be booleans when given.");
                }

                approvals.Add(decision);
            }
            else if (pending.InputRequestIds.Contains(entry.InterruptId))
            {
                if (cancelled)
                {
                    answers.Add(new ToolInputResponse { RequestId = entry.InterruptId, Action = ToolInputAction.Cancel });

                    continue;
                }

                if (entry.Payload is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) })
                {
                    return ResumeMapping.Refused(
                        $"The answer to the request '{entry.InterruptId}' must be an object of the requested fields.");
                }

                answers.Add(new ToolInputResponse
                {
                    RequestId = entry.InterruptId,
                    Action = ToolInputAction.Accept,
                    Content = ReadFields(entry.Payload),
                });
            }
            else
            {
                return ResumeMapping.Refused(
                    $"There is no pending interrupt with id '{entry.InterruptId}' in this thread, or it was answered already.");
            }
        }

        return ResumeMapping.Mapped(approvals, answers);
    }

    /// <summary>
    /// Reads the answer to an approval; <see langword="null"/> when a field has the wrong type.
    /// </summary>
    /// <remarks>
    /// A flag that is present and not a boolean is refused, never read as <see langword="false"/>: a
    /// <c>"rememberArgumentsOnly": "true"</c> that became <see langword="false"/> would save a standing rule that covers
    /// EVERY call of the tool, where the caller asked for one bound to these arguments.
    /// </remarks>
    private static ToolApprovalDecision? ReadApproval(AGUIResume entry)
    {
        if (entry.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("approved", out var approved) ||
            approved.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !TryReadText(payload, "reason", out var reason) ||
            !TryReadFlag(payload, "remember", out var remember) ||
            !TryReadFlag(payload, "rememberArgumentsOnly", out var rememberArgumentsOnly))
        {
            return null;
        }

        return new ToolApprovalDecision
        {
            RequestId = entry.InterruptId,
            Approved = approved.GetBoolean(),
            Reason = reason,
            Remember = remember,
            RememberArgumentsOnly = rememberArgumentsOnly,
        };
    }

    private static Dictionary<string, JsonElement>? ReadFields(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } fields)
        {
            return null;
        }

        var content = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var field in fields.EnumerateObject())
        {
            content[field.Name] = field.Value.Clone();
        }

        return content;
    }

    private static bool TryReadText(JsonElement payload, string name, out string? value)
    {
        value = null;

        if (!payload.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();

        return true;
    }

    private static bool TryReadFlag(JsonElement payload, string name, out bool value)
    {
        value = false;

        if (!payload.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();

        return true;
    }

    private static string? TextOf(AGUIContent content)
    {
        if (content.IsText)
        {
            return content.Value as string;
        }

        // Only the text parts are read; an image, audio, video or document part has no meaning for a text run here.
        StringBuilder? text = null;

        foreach (var part in content)
        {
            if (part is AGUITextInputContent { Text: { Length: > 0 } partText })
            {
                (text ??= new StringBuilder()).Append(partText);
            }
        }

        return text?.ToString();
    }
}

/// <summary>The outcome of <see cref="AGUIInputMapper.CheckToolsAsync"/>.</summary>
internal readonly struct ToolBoundary
{
    private ToolBoundary(IResult? problem, IReadOnlyList<string> ignored)
    {
        Problem = problem;
        Ignored = ignored;
    }

    /// <summary>Gets the outcome of a request that declares nothing worth recording.</summary>
    public static ToolBoundary None { get; } = new(null, []);

    /// <summary>Gets the refusal; <see langword="null"/> when the request may run.</summary>
    public IResult? Problem { get; }

    /// <summary>Gets the declared tool names the agent does not carry as client-side tools.</summary>
    public IReadOnlyList<string> Ignored { get; }

    /// <summary>Creates the outcome of a request that may run.</summary>
    /// <param name="ignored">The declared names the agent does not carry.</param>
    /// <returns>The outcome.</returns>
    public static ToolBoundary Allowed(IReadOnlyList<string> ignored) => new(null, ignored);

    /// <summary>Creates the outcome of a refused request.</summary>
    /// <param name="problem">The refusal.</param>
    /// <returns>The outcome.</returns>
    public static ToolBoundary Refused(IResult problem) => new(problem, []);
}

/// <summary>The outcome of <see cref="AGUIInputMapper.MapResumeAsync"/>.</summary>
internal readonly struct ResumeMapping
{
    private ResumeMapping(IResult? problem, IReadOnlyList<ToolApprovalDecision> approvals, IReadOnlyList<ToolInputResponse> inputs)
    {
        Problem = problem;
        Approvals = approvals;
        Inputs = inputs;
    }

    /// <summary>Gets the outcome of a request that answers nothing.</summary>
    public static ResumeMapping None { get; } = new(null, [], []);

    /// <summary>Gets the refusal; <see langword="null"/> when the answers were mapped.</summary>
    public IResult? Problem { get; }

    /// <summary>Gets the approval decisions.</summary>
    public IReadOnlyList<ToolApprovalDecision> Approvals { get; }

    /// <summary>Gets the answers to requests for user input.</summary>
    public IReadOnlyList<ToolInputResponse> Inputs { get; }

    /// <summary>Creates the outcome of answers that were mapped.</summary>
    /// <param name="approvals">The approval decisions.</param>
    /// <param name="inputs">The answers to requests for input.</param>
    /// <returns>The outcome.</returns>
    public static ResumeMapping Mapped(IReadOnlyList<ToolApprovalDecision> approvals, IReadOnlyList<ToolInputResponse> inputs)
        => new(null, approvals, inputs);

    /// <summary>Creates the outcome of an answer that cannot be applied.</summary>
    /// <param name="detail">What is wrong with it.</param>
    /// <returns>The outcome.</returns>
    public static ResumeMapping Refused(string detail)
        => new(
            Results.Problem(title: "Invalid resume", detail: detail, statusCode: StatusCodes.Status400BadRequest),
            [],
            []);
}
