using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Run-start, input, attachment, and tool-event writing — everything that
/// turns the messages and updates the wrapped agent produces into
/// <see cref="RunEvent"/> rows and <see cref="IRunInputStore"/> records.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>
    /// Opens the run record: writes the <c>runs</c> row and the first <c>RunStarted</c>
    /// event, and saves the input into <see cref="IRunInputStore"/>.
    /// </summary>
    /// <remarks>
    /// The caller must call this method INSIDE ITS OWN try/finally safety net (see the note
    /// of <see cref="CreateScope"/>) — otherwise an <see cref="OperationCanceledException"/>
    /// that occurs in this step disappears without ever moving the run to a terminal status.
    /// </remarks>
    private async ValueTask WriteRunStartAsync(
        RunStart start,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // The input list is materialized ONCE: both the query text and the input record
        // read the same collection.
        var input = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        // 🚨 The input that goes into the record passes through the guard pipeline
        // separately: otherwise masked or blocked content stays RAW in the RunStarted event
        // and in IRunInputStore — the text that goes to the model is already masked inside
        // ContentGuardingChatClient, but this record NEVER passes through that client
        // (HATA-S3-006). ContentGuardPipeline.PreviewAsync is used, not InspectAsync,
        // because the run row (runs) does not exist yet at this point — see the
        // documentation of that method. The `messages` variable that is passed on to the
        // caller is DELIBERATELY left unchanged; the text that goes to the model must not
        // be affected by this masking.
        // A guard FAILURE leaves the verdict on this text unknown, and unknown
        // content does not pass through - the exception is rethrown below and the
        // run never reaches the model. But the run must still leave a record:
        // before HATA-S4-003 the row was never opened, so the client saw an error
        // frame on the stream while GET /api/runs/{id} answered 404 forever, and
        // audit, retry and idempotency could none of them find the run.
        //
        // The record is opened with NO query text: the guard did not finish, so
        // its verdict on that text is unknown and recording it anyway would
        // publish exactly the content the guard exists to hold back.
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? guardFailure = null;

        if (_contentGuardPipeline is { HasGuards: true } guardPipeline && guardPipeline.Options.InspectInput)
        {
            try
            {
                input = await ContentGuardMessageMasker
                    .PreviewAsync(guardPipeline, ContentGuardDirection.Input, input, _modelId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                // A cancellation is deliberately NOT caught here: the caller's
                // finally already writes Canceled for it (HATA-S4-012), and that
                // is the truthful status for a run the caller gave up on.
                guardFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                input = [];
            }
        }

        await start.Writer.StartAsync(
            new RunStartInfo
            {
                RunId = start.Scope.RunId,
                AgentName = start.Scope.AgentName!,
                Kind = start.Kind,
                StartedAt = _timeProvider.GetUtcNow(),
                TenantId = start.Scope.TenantId,
                UserId = start.UserId,
                Labels = start.Labels,
                SessionId = start.SessionId,
                ModelId = _modelId,
                ModelProvider = _modelProvider,
                IsStreaming = start.IsStreaming,
                ParentRunId = start.ParentRunId,

                // On a root run the field stays empty: writing a value that points to the
                // root itself would turn the question "root or child?" into a second
                // condition in the query.
                RootRunId = start.Scope.Depth == 0 ? null : start.Scope.RootRunId,
                Depth = start.Scope.Depth,
                AgentVersion = start.AgentVersion,
                ExperimentId = start.ExperimentId,
                Variant = start.Variant,
                ReplayOfRunId = start.ReplayOfRunId,
                ContinuedFromRunId = start.ContinuedFromRunId,
            },
            ExtractQuery(input),
            cancellationToken).ConfigureAwait(false);

        // The row now exists, so the caller can close the run as Failed. Nothing
        // else is written: the input never passed a guard. The ORIGINAL exception
        // is rethrown with its stack intact - the caller and the guard's own
        // contract both expect that exact exception.
        guardFailure?.Throw();

        await WriteDocumentAttachedEventsAsync(start.Writer, input, cancellationToken).ConfigureAwait(false);
        await SaveInputAsync(start, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a <see cref="RunEventType.DocumentAttached"/> event for every
    /// document channel message in the run's input, keeping the document
    /// visibly apart from the instructions text in the run record.
    /// </summary>
    /// <remarks>
    /// The event carries only the document's name, size, and hash - never its
    /// content: the content already lives in <see cref="IRunInputStore"/>
    /// (subject to the same recording settings as any other input), and
    /// duplicating it into <c>run_events</c> would both grow that table
    /// unboundedly and widen the at-rest encryption scope for no added value.
    /// </remarks>
    private static async ValueTask WriteDocumentAttachedEventsAsync(
        RunEventWriter writer,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (!DocumentChannelMessageBuilder.TryGetSummary(content, out var summary))
                {
                    continue;
                }

                await writer.AppendAsync(
                    new RunEventDraft(RunEventType.DocumentAttached)
                    {
                        Text = summary.Name,
                        Payload = $$"""{"sizeBytes":{{summary.SizeBytes}},"sha256":"{{summary.Sha256}}"}""",
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Writes the input messages of the run into <see cref="IRunInputStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record is written for <strong>every</strong> run — root, child run, eval and
    /// workflow included. A child agent call must also be replayable on its own; limiting
    /// this to the root would lose as many rows as the depth of the tree.
    /// </para>
    /// <para>
    /// The error is <strong>swallowed</strong>: observability does not break
    /// functionality (the same contract as <see cref="IRunStore"/>). A run whose input
    /// could not be written still works; it only cannot be replayed. The loss is
    /// counted on <see cref="TraconDiagnostics.RunRecordingFailureCounterName"/> with
    /// the <c>input</c> stage, so that it is visible without reading logs.
    /// </para>
    /// </remarks>
    private async ValueTask SaveInputAsync(
        RunStart start,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // A run with values and no message still records its values: a workflow
        // starts from an empty message list in a resume, and its values must
        // survive the chain (F-294).
        if (_runInputStore is null ||
            !_options.RecordRunInput ||
            (messages.Count == 0 && start.ParameterValues is null))
        {
            return;
        }

        try
        {
            await _runInputStore.SaveAsync(
                new RunInputRecord
                {
                    RunId = start.Scope.RunId,
                    TenantId = start.Scope.TenantId!,
                    Messages = messages,
                    Parameters = start.ParameterValues,
                    CreatedAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            // Same signal as every other lost write, on the same counter: the run
            // survived but its evidence did not. This one is outside RunEventWriter,
            // so it is counted here rather than through Disable.
            _metrics?.RecordRunRecordingFailure(start.Scope.TenantId, RunRecordingStages.Input);

            _logger.LogWarning(
                ex,
                "Failed to save the Tracon run input. Run {RunId} continues normally " +
                "but will not be replayable.",
                start.Scope.RunId);
        }
    }

    private async ValueTask WriteContentsAsync(
        RunScope scope,
        IEnumerable<AIContent> contents,
        CitationTally citations,
        string? messageId,
        ChatRole? role,
        CancellationToken cancellationToken)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                // 🚨 The option and the empty check live INSIDE the case, not in a
                // `when` clause: a clause that fails sends the content to `default`,
                // and `default` records what Tracon does not recognize. Text with the
                // option off is recognized and deliberately not recorded.
                case TextContent text:
                    if (_options.RecordMessageDeltas && !string.IsNullOrEmpty(text.Text))
                    {
                        await scope.Writer.AppendAsync(
                            new RunEventDraft(RunEventType.MessageDelta) { Text = text.Text },
                            cancellationToken).ConfigureAwait(false);
                    }

                    break;

                // Separate event type — never merged into MessageDelta (docs/70).
                case TextReasoningContent reasoning:
                    if (_options.RecordReasoningDeltas && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        await scope.Writer.AppendAsync(
                            new RunEventDraft(RunEventType.ReasoningDelta) { Text = reasoning.Text },
                            cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case FunctionCallContent call:
                    await WriteToolCallAsync(scope, call, at: null, cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionResultContent result:
                    await WriteToolResultAsync(scope, result, at: null, cancellationToken).ConfigureAwait(false);
                    break;

                // Recognized and accounted for elsewhere: usage is summed into the
                // run's usage, and an approval is a pending request on the run
                // (ChildRunApproval), not an event of its own. A second record here
                // would count the same thing twice.
                case UsageContent:
                case ToolApprovalRequestContent:
                case ToolApprovalResponseContent:
                case AlwaysApproveToolApprovalResponseContent:
                    break;

                default:
                    await WriteHostedOrUnrecognizedContentAsync(scope, content, cancellationToken).ConfigureAwait(false);
                    break;
            }

            // After the switch, for every content: an annotation sits on the base type, so the content
            // that carries a citation is not always the text.
            await WriteCitationsAsync(scope, content, citations, messageId, role, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes each citation a content carries as a <see cref="RunEventCustomTypes.Citation"/> event.
    /// </summary>
    /// <remarks>
    /// Independent of <c>RecordMessageDeltas</c>: a citation is not message text. A failure to describe
    /// one is observability's failure: it is logged and the run goes on. A cancellation is not swallowed.
    /// </remarks>
    private async ValueTask WriteCitationsAsync(
        RunScope scope,
        AIContent content,
        CitationTally citations,
        string? messageId,
        ChatRole? role,
        CancellationToken cancellationToken)
    {
        if (content.Annotations is not { Count: > 0 } annotations)
        {
            return;
        }

        foreach (var annotation in annotations)
        {
            if (annotation is not CitationAnnotation citation || !citations.TryClaim(messageId, role, citation))
            {
                continue;
            }

            RunEventDraft draft;

            try
            {
                draft = CitationEventPayload.Describe(citation, _options.RecordCitationUrls, _options.MaxPayloadLength);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                _logger.LogWarning(
                    ex,
                    "Failed to describe a citation for the Tracon run record. Run {RunId} continues normally " +
                    "but the record has no event for it.",
                    scope.Writer.RunId);

                continue;
            }

            await scope.Writer.AppendReservedAsync(draft, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the call of a registered hosted tool as that tool's call, and everything else no event covers
    /// as a content event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cost ring learned the registered name from the options of the model call and left it in the run's
    /// ledger; a call it did not name belongs to a tool Tracon did not register, such as the harness's
    /// web search, and stays a content event. The result of a registered call is part of the same
    /// call: the provider ran the tool inside the model call, so the call's own pair of events is written
    /// when its content arrives and the result adds no event.
    /// </para>
    /// </remarks>
    private async ValueTask WriteHostedOrUnrecognizedContentAsync(
        RunScope scope,
        AIContent content,
        CancellationToken cancellationToken)
    {
        if (scope.HostedCalls is { } ledger)
        {
            // The ledger holds only calls of registered tools, by the identifier the provider assigned, so
            // the content's own type does not matter here: a file search has no type of its own.
            if (content is ToolCallContent call && ledger.TryGetName(call.CallId, out var toolName))
            {
                // One call, however many contents a stream shows it in.
                if (ledger.TryClaimWrite(call.CallId))
                {
                    await WriteHostedToolCallAsync(scope, toolName, call.CallId, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            if (content is ToolResultContent result && ledger.TryGetName(result.CallId, out _))
            {
                return;
            }
        }

        await WriteUnrecognizedContentAsync(scope, content, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteHostedToolCallAsync(
        RunScope scope,
        string toolName,
        string callId,
        CancellationToken cancellationToken)
    {
        var record = scope.Tools.OnHostedCall(callId, toolName);

        await scope.Writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolInvoking) { ToolName = toolName, ToolCallId = callId },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolInvoked) { ToolName = toolName, ToolCallId = callId },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.RecordToolInvocationAsync(record, cancellationToken).ConfigureAwait(false);

        _metrics?.RecordToolInvocation(toolName, record.Succeeded, record.Duration);
    }

    /// <summary>
    /// Records content no other event covers as a <see cref="RunEventCustomTypes.Content"/> event.
    /// </summary>
    /// <remarks>
    /// The description is built from the content object itself, which a provider adapter or a
    /// future content type controls, so a failure to describe it is observability's failure:
    /// it is logged and the run goes on (the rule that observability does not break
    /// functionality). A cancellation is not swallowed.
    /// </remarks>
    private async ValueTask WriteUnrecognizedContentAsync(
        RunScope scope,
        AIContent content,
        CancellationToken cancellationToken)
    {
        RunEventDraft draft;

        try
        {
            draft = ContentEventPayload.Describe(content, _options.MaxPayloadLength);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "Failed to describe a {ContentType} for the Tracon run record. Run {RunId} continues normally " +
                "but the record has no event for it.",
                content.GetType().Name,
                scope.Writer.RunId);

            return;
        }

        await scope.Writer.AppendReservedAsync(draft, cancellationToken).ConfigureAwait(false);
    }

    // `at` is the time the call really started, for a call written from the tool-call ledger after the run;
    // null for a call a message reports, which is stamped as it is written.
    private static async ValueTask WriteToolCallAsync(
        RunScope scope,
        FunctionCallContent call,
        DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        var arguments = FormatArguments(call);

        scope.Tools.OnCall(call, source: null, arguments);

        await scope.Writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolInvoking)
            {
                ToolName = call.Name,
                ToolCallId = call.CallId,
                Payload = arguments,
                Timestamp = at,
            },
            cancellationToken).ConfigureAwait(false);
    }

    // `at` is the time the call really ended, for a call written from the tool-call ledger after the run;
    // null for a call a message reports, which is stamped as it is written.
    private async ValueTask WriteToolResultAsync(
        RunScope scope,
        FunctionResultContent result,
        DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        var record = scope.Tools.OnResult(result);

        await scope.Writer.AppendAsync(
            // The RECORD decides, not the result: a call whose explanation became
            // its result carries no exception, and reading the result alone
            // reported it as an ordinary success.
            new RunEventDraft(record.Succeeded ? RunEventType.ToolInvoked : RunEventType.ToolFailed)
            {
                ToolName = record.ToolName,
                ToolCallId = result.CallId,
                Text = record.Error,
                Payload = ToolResultText.TryGetText(result.Result, out var text) ? text : null,
                Timestamp = at,
            },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.RecordToolInvocationAsync(record, cancellationToken).ConfigureAwait(false);

        _metrics?.RecordToolInvocation(record.ToolName, record.Succeeded, record.Duration);
    }

    /// <summary>
    /// Writes the calls the tool chain ran and no message reported, when the run closes without an answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run that does not stream reads its calls from the messages of the model's answer, and a run that ended in
    /// an exception has none: the budget refused its next model call, the provider failed, the run was cancelled.
    /// Without this step such a run kept no row and no event for the calls it had made. A call the tool chain
    /// recorded in the run's <see cref="ToolCallLedger"/> is written here exactly as a message would have written
    /// it (the same code, so the same events, the same row, the same metric), stamped with the time it ran.
    /// </para>
    /// <para>
    /// Only a run that closes <see cref="RunStatus.Failed"/> or <see cref="RunStatus.Canceled"/> comes here (the caller
    /// decides). A run that answered has its rows from its messages. A call that only the ledger knows then is one
    /// that an earlier model link ran and a later link had answered again from the recorded result
    /// (<c>ModelBinding.Fallbacks</c>), or the first attempt of a repaired structured answer; a row for it would
    /// read as a second execution of the call.
    /// </para>
    /// <para>
    /// The calls are written in the order they happened, so a start and an end that overlap another call's are
    /// not grouped by call. A call that had not ended when the run did (a cancellation reached it) gets its start
    /// event here and is closed with the others by <see cref="ToolInvocationTracker.DrainUnfinished"/>, which runs
    /// next. A call the tracker already holds as started is left to it: a streaming run saw that call's content
    /// while it ran, wrote its start event then, and closes it the same way.
    /// </para>
    /// <para>
    /// The step is observability, so a failure in it is counted and logged and the run closes. A cancellation is
    /// not swallowed.
    /// </para>
    /// </remarks>
    private async ValueTask WriteLedgerCallsAsync(RunScope scope, CancellationToken cancellationToken)
    {
        if (scope.ToolCalls is not { } ledger)
        {
            return;
        }

        // Read without claiming: a row claims its call as it is written.
        var unwritten = ledger.Unclaimed()
            .Where(call => !scope.Tools.IsPending(call.Call.CallId))
            .ToList();

        if (unwritten.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var step in LedgerSteps(unwritten))
            {
                if (step.Ends)
                {
                    await WriteToolResultAsync(
                        scope,
                        new FunctionResultContent(step.Call.Call.CallId, step.Call.Result) { Exception = step.Call.Failure },
                        step.At,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteToolCallAsync(scope, step.Call.Call, step.At, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _metrics?.RecordRunRecordingFailure(scope.TenantId, RunRecordingStages.ToolInvocation);

            _logger.LogWarning(
                ex,
                "Failed to write the tool calls the tool chain recorded. Run {RunId} closes normally " +
                "but its record may miss some of them.",
                scope.RunId);
        }
    }

    /// <summary>
    /// Lays the start and the end of each call out in the order they happened: by time, a start before an end at the
    /// same time, calls that started together in the order they started.
    /// </summary>
    private static IEnumerable<LedgerStep> LedgerSteps(IReadOnlyList<ToolCallSnapshot> calls)
        => calls
            .SelectMany(static (call, index) => call.EndedAtUtc is { } ended
                ? new[] { new LedgerStep(call.StartedAtUtc, Ends: false, index, call), new LedgerStep(ended, Ends: true, index, call) }
                : [new LedgerStep(call.StartedAtUtc, Ends: false, index, call)])
            .OrderBy(static step => step.At)
            .ThenBy(static step => step.Ends)
            .ThenBy(static step => step.Index);

    /// <summary>One event of a call written from the tool-call ledger: its start or its end.</summary>
    private readonly record struct LedgerStep(DateTimeOffset At, bool Ends, int Index, ToolCallSnapshot Call);

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // The arguments are formatted by hand to stay AOT compatible; JSON serialization
        // that relies on reflection is not used.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    // AgentSessionManager stamps the session identifier onto the session. When the stamp is
    // absent, the session was opened outside Tracon; instead of writing a placeholder
    // value into the record, the field is left empty.
    private static string? GetSessionId(AgentSession session) => AgentSessionIdentity.GetId(session);

    // Phase 45 (F-53): the first user message that triggered this run is written into the
    // RunEventType.RunStarted event, so the run list can show what a run was asked. The
    // text is cut at MaxPayloadLength like any event text; the full input lives in
    // IRunInputStore (phase 47), which is where promotion reads it first (F-343).
    //
    // 🚨 A document channel message is ChatRole.User too (it needs to sit
    // beside the actual message in a turn) and, when documents are given,
    // precedes the query in the list - a naive FirstOrDefault(User) would
    // make a document's OWN content look like the query it was attached to.
    // The check reads the marker even after a JSON round trip: a replay or a
    // continuation re-sends a stored input whose marker values are JsonElement.
    private static string? ExtractQuery(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User && !DocumentChannelMessageBuilder.IsDocumentMessage(message))
            {
                return message.Text;
            }
        }

        return null;
    }
}
