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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
        if (_runInputStore is null || !_options.RecordRunInput || messages.Count == 0)
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
                    CreatedAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        CancellationToken cancellationToken)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when _options.RecordMessageDeltas && !string.IsNullOrEmpty(text.Text):
                    await scope.Writer.AppendAsync(
                        new RunEventDraft(RunEventType.MessageDelta) { Text = text.Text },
                        cancellationToken).ConfigureAwait(false);
                    break;

                // Separate event type — never merged into MessageDelta (docs/70).
                case TextReasoningContent reasoning
                    when _options.RecordReasoningDeltas && !string.IsNullOrEmpty(reasoning.Text):
                    await scope.Writer.AppendAsync(
                        new RunEventDraft(RunEventType.ReasoningDelta) { Text = reasoning.Text },
                        cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionCallContent call:
                    await WriteToolCallAsync(scope, call, cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionResultContent result:
                    await WriteToolResultAsync(scope, result, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    break;
            }
        }
    }

    private static async ValueTask WriteToolCallAsync(
        RunScope scope,
        FunctionCallContent call,
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
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteToolResultAsync(
        RunScope scope,
        FunctionResultContent result,
        CancellationToken cancellationToken)
    {
        var record = scope.Tools.OnResult(result);

        await scope.Writer.AppendAsync(
            new RunEventDraft(result.Exception is null ? RunEventType.ToolInvoked : RunEventType.ToolFailed)
            {
                ToolName = record.ToolName,
                ToolCallId = result.CallId,
                Text = ToolFailureText.Get(result.Exception),
                Payload = ToolResultText.TryGetText(result.Result, out var text) ? text : null,
            },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.RecordToolInvocationAsync(record, cancellationToken).ConfigureAwait(false);

        _metrics?.RecordToolInvocation(record.ToolName, record.Succeeded, record.Duration);
    }

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
    // RunEventType.RunStarted event. run_events is the ONLY place where the input text is
    // PERSISTED — the session is saved only when the run completes SUCCESSFULLY
    // (AgentEndpoints.AgentRunStream), therefore the query of a failed run cannot be read
    // through any other path.
    //
    // 🚨 A document channel message is ChatRole.User too (it needs to sit
    // beside the actual message in a turn) and, when documents are given,
    // precedes the query in the list - a naive FirstOrDefault(User) would
    // make a document's OWN content look like the query it was attached to.
    private static string? ExtractQuery(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User && !IsDocumentChannelMessage(message))
            {
                return message.Text;
            }
        }

        return null;
    }

    private static bool IsDocumentChannelMessage(ChatMessage message)
        => message.Contents is [TextContent content] && DocumentChannelMessageBuilder.TryGetSummary(content, out _);
}
