using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Checks a run's response against the agent's requested
/// <see cref="AgentResponseFormat"/> before letting it through.
/// </summary>
/// <remarks>
/// <para>
/// Installed as the <strong>innermost</strong> <see cref="IAgentDecorator"/>
/// (<c>Order = 30</c>, see <see cref="StructuredResponseValidatingAgentDecorator"/>) —
/// closer to the compiled agent than the tool-approval gate and run recording,
/// so a rejection propagates as an ordinary exception through both outer
/// wrappers exactly like every other run-ending error.
/// </para>
/// <para>
/// Runs only when all three hold: <see cref="TraconStructuredResponseOptions.Enabled"/>
/// is <see langword="true"/>, the agent asked for <see cref="AgentResponseFormatKind.Json"/>
/// or <see cref="AgentResponseFormatKind.JsonSchema"/>, and the run actually
/// produced a response (it did not already end in error or cancellation). With
/// any of the three missing, this wrapper calls straight through and today's
/// behaviour does not change at all.
/// </para>
/// <para>
/// <strong>A response that waits for the caller is not judged.</strong> A
/// turn that stops at a tool approval or at a client-side tool call has no answer
/// yet: the answer comes in the turn that carries the decision or the tool result,
/// and that turn is judged. Before, such a turn was rejected as empty and the run
/// failed before it could wait.
/// </para>
/// <para>
/// <strong>Well-formedness first.</strong> Before <see cref="IStructuredResponseValidator"/>
/// is ever called, the response must be non-empty and parse as JSON. Tracon
/// checks this itself — <c>System.Text.Json</c> is already a dependency, no
/// reflection is involved, and it closes the two failure modes a consumer
/// would otherwise have to check by hand in every validator. This is
/// deliberately NOT schema validation; see the interface's own remarks for
/// why Tracon ships no built-in schema validator.
/// </para>
/// <para>
/// <strong>Bounded repair.</strong> When <see cref="TraconStructuredResponseOptions.MaxRepairAttempts"/>
/// is greater than zero, a rejected response does not fail the run immediately.
/// Instead a REPAIR turn runs: the same compiled agent is called again with the
/// invalid response plus a short correction message appended, through the same
/// <c>base.RunCoreAsync</c> call this class always used — so budget, deadline,
/// cancellation and the fallback chain apply exactly as they do to every other
/// model call, with no separate wiring. The repair call passes <c>session: null</c>
/// deliberately: passing the caller's own session would let the correction
/// exchange leak into the durable conversation history. This mirrors how
/// Tracon already runs a genuinely session-less turn today: the underlying
/// agent framework opens a throw-away <see cref="AgentSession"/> for that one
/// call and never links it back, so the caller's own session gains nothing
/// from the repair round.
/// </para>
/// <para>
/// <strong>A response cut off at the output token limit is not repaired.</strong>
/// The pipeline's finish-reason detector ends that call with
/// <see cref="TraconOutputTruncatedException"/> before this class sees a
/// response; a repair turn would hit the same limit. The rejection event
/// carries <c>RepairSkippedTruncated</c> when a repair attempt was left.
/// </para>
/// <para>
/// <strong>A run that carries a durable session gets no repair budget.</strong>
/// The underlying agent framework persists a turn's own request and response
/// as soon as that ONE model call completes, entirely inside its own call —
/// this class only sees the result afterward and has no hook to hold that
/// write back or replace it. Repairing anyway would let the run SUCCEED while
/// its session still ends in the rejected draft, so the next turn would read a
/// response the caller never received. Instead, when the run's <c>session</c>
/// is non-null the attempt budget collapses to one: the rejection fails the
/// run exactly as it would with repair disabled, and the run's result and the
/// session's history keep saying the same thing. The rejection event carries
/// <c>RepairSuppressedBySession</c> so this is distinguishable from a run that
/// simply had no repair configured.
/// </para>
/// <para>
/// This limitation is <strong>not</strong> a permanent contract. MAF's
/// <c>ChatHistoryProvider</c> does expose store-side seams (a response-message
/// filter and an overridable <c>StoreChatHistoryAsync</c>); what is missing is
/// a way to defer the history commit until the run's validation verdict
/// exists, since the store runs the moment the model call returns and
/// <c>InvokedContext.ResponseMessages</c> is read-only. Making the commit
/// conditional on the verdict is a design in its own right, not a patch to
/// this class.
/// </para>
/// <para>
/// <strong>Streaming.</strong> Content is forwarded to the caller as it
/// arrives — validation runs only after the last update, so a rejection can
/// still close the run as <see cref="RunStatus.Failed"/> but cannot un-send
/// what the client already received. Repair therefore never runs on the
/// streaming path (see <see cref="RunCoreStreamingAsync"/>): a second turn
/// would stream a second, different response into a client that has already
/// received the first one. A structured-output agent used as a gate should
/// not stream.
/// </para>
/// </remarks>
internal sealed class StructuredResponseValidatingAgent : DelegatingAIAgent
{
    private readonly AgentDescriptor _descriptor;
    private readonly IStructuredResponseValidator _validator;
    private readonly TraconStructuredResponseOptions _options;
    private readonly ILogger<StructuredResponseValidatingAgent> _logger;

    /// <summary>Creates a new structured response validation wrapper.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="descriptor">The catalog summary of the agent, carrying its requested response format.</param>
    /// <param name="validator">The validation policy.</param>
    /// <param name="options">The structured response settings.</param>
    /// <param name="logger">The logger for a faulting validator.</param>
    public StructuredResponseValidatingAgent(
        AIAgent innerAgent,
        AgentDescriptor descriptor,
        IStructuredResponseValidator validator,
        TraconStructuredResponseOptions options,
        ILogger<StructuredResponseValidatingAgent> logger)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _descriptor = descriptor;
        _validator = validator;
        _options = options;
        _logger = logger;
    }

    /// <summary>The safe rejection reason for a response cut off at the output token limit.</summary>
    internal const string TruncatedReason =
        "The model stopped at its output token limit before the response was complete.";

    private AgentResponseFormat? RequestedFormat => _descriptor.Model?.ResponseFormat;

    private bool ShouldValidate
        => _options.Enabled && RequestedFormat?.Kind is AgentResponseFormatKind.Json or AgentResponseFormatKind.JsonSchema;

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldValidate)
        {
            return await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }

        // Materialized ONCE, up front: it is read again below to build every
        // repair turn's message list, and an IEnumerable<ChatMessage> a caller
        // passed in is not guaranteed to be safely re-enumerable.
        var originalMessages = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        var format = RequestedFormat!;

        // "Attempt" counts every MODEL CALL this run makes for this response,
        // 1-based: 1 is the original turn, 2.. are repairs. maxAttempts is the
        // total call budget - one more than MaxRepairAttempts, since the
        // original turn also counts as a call.
        //
        // 🚨 A run that carries a durable session gets NO repair budget. The
        // underlying framework has already persisted the first attempt's own
        // response by the time this class sees it, and nothing here can hold
        // that write back or replace it (see the class remarks). Repairing
        // anyway would let the run SUCCEED while the session it belongs to
        // still ends in the rejected draft - the next turn would then read a
        // response the caller never received. Failing closed keeps the run's
        // result and the session's history saying the same thing.
        // 🚨 `session is not null` is NOT the test: the run path hands the agent a
        // session object even when the caller asked for no durable conversation.
        // Only a session Tracon itself stamped carries an identity, and only
        // such a session outlives the run - that is the one repair would desync.
        var repairSuppressed = _options.MaxRepairAttempts > 0
            && session is not null
            && AgentSessionIdentity.GetId(session) is not null;
        var maxAttempts = repairSuppressed ? 1 : _options.MaxRepairAttempts + 1;

        var response = await RunAttemptAsync(
                originalMessages, session, options, format, attempt: 1, maxAttempts, repairSuppressed, cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            if (WaitsForCaller(response.Messages))
            {
                return response;
            }

            var answer = FinalAnswerText(response.Messages);
            var reason = await ValidationReasonAsync(format, answer, cancellationToken).ConfigureAwait(false);

            if (reason is null)
            {
                return response;
            }

            // 🚨 Every response that is NOT the one this method returns must
            // fold its usage into the side channel here - the returned
            // response's usage is already counted through the normal path,
            // but a response that is superseded by a repair, or that is the
            // last one before this method throws, has no other way to reach
            // the run's recorded usage.
            TraconRunContext.Current?.ExtraUsage?.Add(response.Usage);

            await RejectedAsync(format, reason, attempt, maxAttempts, repairSuppressed, cancellationToken).ConfigureAwait(false);

            if (attempt >= maxAttempts)
            {
                throw new TraconStructuredResponseException(reason);
            }

            await RepairAttemptedAsync(format, attempt + 1, maxAttempts, cancellationToken).ConfigureAwait(false);

            var repairMessages = BuildRepairMessages(originalMessages, answer, reason);

            // session: null is deliberate - see the class remarks on why the
            // repair exchange must not reach the caller's own session.
            response = await RunAttemptAsync(
                    repairMessages, session: null, options, format, attempt + 1, maxAttempts, repairSuppressed: false, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Runs one model call of this response and records an output-limit stop as a rejection.</summary>
    /// <remarks>
    /// <para>
    /// A response the model cut off at its output token limit never
    /// reaches the validator. The pipeline's finish-reason detector throws
    /// <see cref="TraconOutputTruncatedException"/> for a structured request, and
    /// this method lets it end the run: a repair turn asks the same model with
    /// the same limit for a document at least as long, so it would be cut off
    /// again. Before, the cut-off JSON was rejected as "not valid JSON" and every
    /// repair turn spent another full output limit on the same failure.
    /// </para>
    /// <para>
    /// The rejection event still records the attempt, so the run's timeline says
    /// why it ended. <c>RepairSkippedTruncated</c> is set only when a repair
    /// attempt was left: a run without repair budget did not skip anything. The
    /// truncated call's tokens are already folded into the run by the detector;
    /// nothing is added here.
    /// </para>
    /// </remarks>
    private async Task<AgentResponse> RunAttemptAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AgentResponseFormat format,
        int attempt,
        int maxAttempts,
        bool repairSuppressed,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconOutputTruncatedException)
        {
            await RejectedAsync(
                    format,
                    TruncatedReason,
                    attempt,
                    maxAttempts,
                    repairSuppressed,
                    cancellationToken,
                    repairSkippedTruncated: attempt < maxAttempts)
                .ConfigureAwait(false);

            throw;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!ShouldValidate)
        {
            await foreach (var passthrough in base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        var updates = new List<AgentResponseUpdate>();
        var format = RequestedFormat!;

        var enumerator = base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (TraconOutputTruncatedException)
                {
                    // F-320: the stream already reached the caller; the rejection
                    // only says why the run ends. Repair never runs here anyway.
                    await RejectedAsync(format, TruncatedReason, attempt: 1, maxAttempts: 1, repairSuppressed: false, cancellationToken)
                        .ConfigureAwait(false);

                    throw;
                }

                if (!hasNext)
                {
                    break;
                }

                updates.Add(enumerator.Current);
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        // Validation runs AFTER the whole stream has already reached the
        // caller — see the class remarks on why content cannot be un-sent.
        // Repair never runs here (see the class remarks): a rejection is
        // always the FIRST and ONLY attempt on the streaming path, regardless
        // of MaxRepairAttempts.
        var streamed = AgentResponseExtensions.ToAgentResponse(updates).Messages;

        if (WaitsForCaller(streamed))
        {
            yield break;
        }

        var text = FinalAnswerText(streamed);
        var reason = await ValidationReasonAsync(format, text, cancellationToken).ConfigureAwait(false);

        if (reason is null)
        {
            yield break;
        }

        await RejectedAsync(format, reason, attempt: 1, maxAttempts: 1, repairSuppressed: false, cancellationToken).ConfigureAwait(false);

        throw new TraconStructuredResponseException(reason);
    }

    /// <summary>
    /// Whether the response stops to wait for the caller: a tool approval request, or a
    /// client-side tool call the response carries no result for.
    /// </summary>
    /// <param name="messages">The response's messages.</param>
    /// <returns><see langword="true"/> when the response is a pause, not an answer.</returns>
    /// <remarks>
    /// A server tool's call always comes with its result: on its last tool round
    /// Microsoft.Extensions.AI's function-invoking client takes every function tool
    /// away from the model (10.9.0, read from the decompiled client), so a call with no
    /// result is a client-side tool's.
    /// </remarks>
    internal static bool WaitsForCaller(IList<ChatMessage> messages)
    {
        HashSet<string>? answered = null;
        List<string>? called = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent:
                        return true;
                    case FunctionResultContent result:
                        (answered ??= new HashSet<string>(StringComparer.Ordinal)).Add(result.CallId);
                        break;
                    // A call the provider handled itself is informational: it waits for nobody.
                    case FunctionCallContent { InformationalOnly: false } call:
                        (called ??= []).Add(call.CallId);
                        break;
                }
            }
        }

        return called is not null && called.Exists(callId => answered is null || !answered.Contains(callId));
    }

    /// <summary>
    /// The text to validate: the last assistant message that carries text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A response is not one message. A tool round leaves the text
    /// the model wrote before its call ahead of the answer, and a loop leaves
    /// one reply per iteration (and, on a stream, the loop's own feedback).
    /// <see cref="AgentResponse.Text"/> joins them all, so a valid final JSON
    /// was rejected as "not valid JSON" because an earlier draft or a remark
    /// before a tool call came with it.
    /// </para>
    /// <para>
    /// A response with no assistant text at all falls back to the joined text,
    /// so it is still judged — empty is rejected as empty.
    /// </para>
    /// </remarks>
    internal static string FinalAnswerText(IList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];

            if (message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
            {
                return message.Text;
            }
        }

        return string.Concat(messages.Select(static message => message.Text));
    }

    /// <summary>
    /// Checks well-formedness first, then (only if well-formed) the
    /// configured <see cref="IStructuredResponseValidator"/>.
    /// </summary>
    /// <returns>The rejection reason, or <see langword="null"/> when the response is valid.</returns>
    private async ValueTask<string?> ValidationReasonAsync(
        AgentResponseFormat format, string responseText, CancellationToken cancellationToken)
        => WellFormednessReason(responseText)
           ?? await CallValidatorAsync(format, responseText, cancellationToken).ConfigureAwait(false);

    private async ValueTask<string?> CallValidatorAsync(
        AgentResponseFormat format, string responseText, CancellationToken cancellationToken)
    {
        var scope = TraconRunContext.Current;

        var context = new StructuredResponseValidationContext
        {
            AgentName = _descriptor.Name,
            RunId = scope?.RunId ?? Guid.Empty,
            SessionId = scope?.SessionId,
            Provider = _descriptor.Model?.Provider,
            Model = _descriptor.Model?.Model,
            Kind = format.Kind,
            Schema = format.Schema,
            SchemaName = format.SchemaName,
            ResponseText = responseText,
        };

        StructuredResponseValidationResult result;

        try
        {
            result = await _validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogError(
                ex,
                "The structured response validator threw while checking agent '{AgentName}'; the response was " +
                "rejected (fail-closed).",
                _descriptor.Name);

            result = StructuredResponseValidationResult.Invalid(
                "The structured response validation check failed. This run was rejected (fail-closed).");
        }

        return result.IsValid ? null : result.Reason ?? "The response was rejected by the structured response validator.";
    }

    /// <summary>Checks that the response is non-empty and parses as JSON.</summary>
    private static string? WellFormednessReason(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "The response is empty.";
        }

        try
        {
            using var _ = JsonDocument.Parse(responseText);
            return null;
        }
        catch (JsonException)
        {
            return "The response is not valid JSON.";
        }
    }

    private async ValueTask RejectedAsync(
        AgentResponseFormat format,
        string reason,
        int attempt,
        int maxAttempts,
        bool repairSuppressed,
        CancellationToken cancellationToken,
        bool repairSkippedTruncated = false)
    {
        if (TraconRunContext.Current?.Writer is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new StructuredResponseRejectedEventPayload
            {
                Attempt = attempt,
                MaxAttempts = maxAttempts,
                Kind = format.Kind.ToString(),
                SchemaName = format.SchemaName,
                Reason = reason,
                Provider = _descriptor.Model?.Provider,
                Model = _descriptor.Model?.Model,
                RepairSuppressedBySession = repairSuppressed ? true : null,
                RepairSkippedTruncated = repairSkippedTruncated ? true : null,
            },
            TraconCoreJsonContext.Default.StructuredResponseRejectedEventPayload);

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.StructuredResponseRejected) { Text = reason, Payload = payload },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the <see cref="RunEventType.StructuredResponseRepairAttempted"/> event.</summary>
    private static async ValueTask RepairAttemptedAsync(
        AgentResponseFormat format, int attempt, int maxAttempts, CancellationToken cancellationToken)
    {
        if (TraconRunContext.Current?.Writer is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new StructuredResponseRepairAttemptedEventPayload
            {
                Attempt = attempt,
                MaxAttempts = maxAttempts,
                Kind = format.Kind.ToString(),
                SchemaName = format.SchemaName,
            },
            TraconCoreJsonContext.Default.StructuredResponseRepairAttemptedEventPayload);

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.StructuredResponseRepairAttempted)
            {
                Text = $"{attempt}/{maxAttempts}",
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the message list for one repair turn: the original turn's own
    /// messages, the model's rejected response, and a dedicated correction
    /// message carrying the safe rejection reason.
    /// </summary>
    /// <remarks>
    /// The raw response text goes back to the model here — it needs to see
    /// its own mistake to correct it — but never into a <c>run</c> event; see
    /// <see cref="RejectedAsync"/>, which writes only <paramref name="reason"/>.
    /// </remarks>
    private static List<ChatMessage> BuildRepairMessages(
        IReadOnlyList<ChatMessage> originalMessages, string invalidResponseText, string reason)
    {
        var messages = new List<ChatMessage>(originalMessages.Count + 2);
        messages.AddRange(originalMessages);
        messages.Add(new ChatMessage(ChatRole.Assistant, invalidResponseText));
        messages.Add(new ChatMessage(
            ChatRole.User,
            $"Your previous response did not satisfy the required output format: {reason} " +
            "Reply again with ONLY a response that satisfies the format - no explanation, no code fences."));

        return messages;
    }
}

/// <summary>The <see cref="RunEventType.StructuredResponseRejected"/> event payload.</summary>
internal sealed record StructuredResponseRejectedEventPayload
{
    /// <summary>
    /// Gets the 1-based number of the model call this rejection belongs to.
    /// <c>1</c> is the original turn; <c>2</c> and above are repair turns.
    /// </summary>
    public required int Attempt { get; init; }

    /// <summary>Gets the total number of model calls this run allows for this response.</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>Gets the requested response format kind's wire name.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the requested schema's name, if the agent's definition supplied one.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Gets the safe rejection reason. Never the model's raw response text.</summary>
    public required string Reason { get; init; }

    /// <summary>Gets the model provider that produced the rejected response, if known.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the model that produced the rejected response, if known.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets <see langword="true"/> when repair was configured but deliberately
    /// not attempted because the run carries a durable session;
    /// <see langword="null"/> otherwise, so a run that simply has no repair
    /// budget is not confused with one whose budget was withheld.
    /// </summary>
    public bool? RepairSuppressedBySession { get; init; }

    /// <summary>
    /// Gets <see langword="true"/> when a repair attempt was left but deliberately
    /// not run because the model stopped at its output token limit;
    /// <see langword="null"/> otherwise. A repair turn would hit the same limit.
    /// </summary>
    public bool? RepairSkippedTruncated { get; init; }
}

/// <summary>The <see cref="RunEventType.StructuredResponseRepairAttempted"/> event payload.</summary>
internal sealed record StructuredResponseRepairAttemptedEventPayload
{
    /// <summary>Gets the 1-based number of the model call the repair turn about to run will make.</summary>
    public required int Attempt { get; init; }

    /// <summary>Gets the total number of model calls this run allows for this response.</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>Gets the requested response format kind's wire name.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the requested schema's name, if the agent's definition supplied one.</summary>
    public string? SchemaName { get; init; }
}
