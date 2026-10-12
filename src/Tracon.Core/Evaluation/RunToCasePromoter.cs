using Microsoft.Extensions.AI;

namespace Tracon;

// 🚨 F-343: phase 45 read the RunStarted text alone (K-300, decided before phase
// 47 recorded inputs in run_inputs). A query over MaxPayloadLength came out cut
// with the writer's marker inside it, and the documents and the parameter values
// the run carried were dropped - the eval then measured a different input than
// the run it stood for. The earlier-run check is K-301.

/// <summary>
/// Promotes a production run to an evaluation case.
/// </summary>
/// <remarks>
/// <para>
/// The case is built from the run's <strong>recorded input</strong>
/// (<see cref="IRunInputStore"/>) when one exists: the request's full text
/// becomes the query, the document channel messages ahead of it become
/// <see cref="EvalCaseDraft.Context"/> verbatim, and the recorded parameter
/// values become <see cref="EvalCaseDraft.Parameters"/>. The eval then sends the
/// messages the run sent (<see cref="EvalJobHandler.BuildCaseMessages"/>).
/// </para>
/// <para>
/// Without a recorded input - input recording off, the record removed by
/// retention, or a run recorded before inputs were - the query falls back to
/// the <see cref="RunEventType.RunStarted"/> text, the one other durable copy (a
/// session is saved only after successful completion, so a failed run's query
/// lives nowhere else). That text is cut at <c>MaxPayloadLength</c>; a cut text
/// keeps <see cref="RunEventWriter.TruncationMarker"/>, so the case shows that it
/// was cut. A run whose <see cref="RunEventType.DocumentAttached"/> events name
/// documents no record holds any more is refused.
/// </para>
/// <para>
/// A recorded input that a case cannot carry is refused rather than trimmed: a
/// non-text content in the request (an attachment), or a message besides the
/// documents and the request (a conversation sent as one run's input).
/// Multi-turn behavior across runs is detected by checking for an earlier run
/// in this run's session. One rule covers all three: a case that silently lost
/// part of its input measures something the run never did.
/// </para>
/// </remarks>
internal sealed class RunToCasePromoter
{
    private readonly IRunStore _runs;
    private readonly IRunScoreStore _scores;
    private readonly IEvalStore _evalStore;
    private readonly IRunInputStore _inputs;

    /// <summary>Initializes a promotion service.</summary>
    /// <param name="runs">The run store: the run row and its events.</param>
    /// <param name="scores">The score store, for inferring the promotion reason.</param>
    /// <param name="evalStore">The eval store the case is added to.</param>
    /// <param name="inputs">The input store the case's query, context and values are read from.</param>
    public RunToCasePromoter(IRunStore runs, IRunScoreStore scores, IEvalStore evalStore, IRunInputStore inputs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(evalStore);
        ArgumentNullException.ThrowIfNull(inputs);

        _runs = runs;
        _scores = scores;
        _evalStore = evalStore;
        _inputs = inputs;
    }

    /// <summary>Attempts to promote a run to the supplied suite.</summary>
    /// <param name="tenantId">The requesting tenant.</param>
    /// <param name="suite">The target evaluation suite.</param>
    /// <param name="runId">The run to promote.</param>
    /// <param name="sourceKindOverride">
    /// Overrides the promotion reason. When <see langword="null"/>, it is
    /// inferred from the run's status and score.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The promotion outcome.</returns>
    public async ValueTask<RunPromotionOutcome> PromoteAsync(
        string tenantId,
        EvalSuite suite,
        Guid runId,
        EvalCaseSource? sourceKindOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(suite);

        var run = await _runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // "Not found" and "belongs to another tenant" return the same outcome.
        // A distinct outcome would leak existence, as with RunEndpoints.SaveFeedbackAsync.
        if (run is null || !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
        {
            return new RunPromotionOutcome(RunPromotionStatus.RunNotFound, null);
        }

        var sourceKind = await ResolveSourceKindAsync(tenantId, run, sourceKindOverride, cancellationToken).ConfigureAwait(false);

        if (sourceKind is null)
        {
            return new RunPromotionOutcome(RunPromotionStatus.AmbiguousSource, null);
        }

        if (await HasEarlierRunInSameSessionAsync(tenantId, run, cancellationToken).ConfigureAwait(false))
        {
            return new RunPromotionOutcome(RunPromotionStatus.MultiTurn, null);
        }

        var events = await ReadEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var record = await _inputs.GetAsync(tenantId, runId, cancellationToken).ConfigureAwait(false);

        // A record with no message is a workflow's (its values only); an agent
        // run always records the messages it was sent.
        var input = record is { Messages.Count: > 0 } ? ReadRecordedInput(record) : ReadRunStartedInput(events);

        if (input.Problem is { } problem)
        {
            return new RunPromotionOutcome(RunPromotionStatus.UnsupportedInput, null) { Detail = problem };
        }

        if (string.IsNullOrWhiteSpace(input.Query))
        {
            return new RunPromotionOutcome(RunPromotionStatus.NoQuery, null);
        }

        string? expectedOutput = null;
        IReadOnlyList<string> expectedTools = [];

        if (sourceKind == EvalCaseSource.ReferenceRun)
        {
            expectedOutput = ExtractOutputText(events);

            var invocations = await _runs.ListToolInvocationsAsync(runId, cancellationToken).ConfigureAwait(false);
            expectedTools = [.. invocations.Select(static invocation => invocation.ToolName).Distinct(StringComparer.Ordinal)];
        }

        var draft = new EvalCaseDraft
        {
            Query = input.Query,
            ExpectedOutput = expectedOutput,
            ExpectedTools = expectedTools,
            Context = input.Context,
            Parameters = input.Parameters,
            SourceRunId = runId,
            SourceKind = sourceKind,
        };

        var added = await _evalStore.AddCaseAsync(suite.Id, draft, cancellationToken).ConfigureAwait(false);

        return new RunPromotionOutcome(
            added.Created ? RunPromotionStatus.Created : RunPromotionStatus.AlreadyExists,
            added.Case)
        {
            InputSource = input.Source,
            QueryTruncated = input.QueryTruncated,
        };
    }

    /// <summary>Reads the case's input from the run's recorded messages and values.</summary>
    /// <remarks>
    /// Every production path appends the request last, after the documents
    /// (<c>AgentEndpoints.BuildMessagesAsync</c>, the queued and the batch
    /// handler), so the request is read as the last message - not as the first
    /// non-document one: a document whose marker a content guard dropped would
    /// otherwise pass for the query.
    /// </remarks>
    private static PromotedInput ReadRecordedInput(RunInputRecord record)
    {
        var messages = record.Messages;
        var request = messages[^1];

        if (request.Role != ChatRole.User || IsDocument(request, out _))
        {
            // Documents with no request, or a turn that ends on something else:
            // there is no query to read.
            return new PromotedInput(null, null, null, RunPromotionInputSource.RecordedInput, QueryTruncated: false, Problem: null);
        }

        var nonText = request.Contents.Count(static content => content is not TextContent);

        if (nonText > 0)
        {
            return PromotedInput.Unsupported(
                $"its request carries {nonText} non-text content item(s), such as an attachment, and an eval case carries text only.");
        }

        List<string> documents = [];
        var others = 0;

        for (var index = 0; index < messages.Count - 1; index++)
        {
            if (IsDocument(messages[index], out var text))
            {
                documents.Add(text);
            }
            else
            {
                others++;
            }
        }

        if (others > 0)
        {
            return PromotedInput.Unsupported(
                $"its recorded input carries {others} message(s) besides its documents and its request, and an eval case carries a single request.");
        }

        return new PromotedInput(
            request.Text,
            documents.Count > 0 ? string.Join('\n', documents) : null,
            record.Parameters is { Count: > 0 } parameters ? parameters : null,
            RunPromotionInputSource.RecordedInput,
            QueryTruncated: false,
            Problem: null);
    }

    /// <summary>Reads the case's input from the <c>RunStarted</c> event, for a run with no recorded input.</summary>
    private static PromotedInput ReadRunStartedInput(IReadOnlyList<RunEvent> events)
    {
        var documents = events.Count(static runEvent => runEvent.Type == RunEventType.DocumentAttached);

        if (documents > 0)
        {
            return PromotedInput.Unsupported(
                $"it carried {documents} document(s) and its input was not recorded (input recording is off, or the record was removed), so an eval case cannot include them.");
        }

        var query = events.FirstOrDefault(static runEvent => runEvent.Type == RunEventType.RunStarted)?.Text;

        return new PromotedInput(
            query,
            null,
            null,
            RunPromotionInputSource.RunStartedEvent,
            QueryTruncated: query?.EndsWith(RunEventWriter.TruncationMarker, StringComparison.Ordinal) == true,
            Problem: null);
    }

    /// <summary>
    /// Reports whether a message carries a document: marked by the document
    /// channel, or - when a content guard rewrote its text and dropped the
    /// marker - exactly one whole document block.
    /// </summary>
    private static bool IsDocument(ChatMessage message, out string text)
    {
        if (message.Role == ChatRole.User &&
            message.Contents is [TextContent { Text: { } content } textContent] &&
            (DocumentChannelMessageBuilder.TryGetSummary(textContent, out _) || DocumentChannelMessageBuilder.IsSingleBlock(content)))
        {
            text = content;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private async ValueTask<EvalCaseSource?> ResolveSourceKindAsync(
        string tenantId,
        RunRecord run,
        EvalCaseSource? sourceKindOverride,
        CancellationToken cancellationToken)
    {
        if (sourceKindOverride is { } overrideKind)
        {
            return overrideKind;
        }

        if (run.Status == RunStatus.Failed)
        {
            return EvalCaseSource.FailedRun;
        }

        // Only completed runs infer the source automatically. Running,
        // AwaitingInput, and Canceled require an explicit sourceKind because it
        // is ambiguous whether they are successful or failed.
        if (run.Status != RunStatus.Completed)
        {
            return null;
        }

        var scores = await _scores.ListAsync(tenantId, run.Id, cancellationToken).ConfigureAwait(false);

        // A null Value means no measurement was made, so neither lifted
        // comparison holds and the run is not treated as negatively scored.
        var isNegative = scores.Any(static score =>
            (score.Kind == RunScoreKind.Binary && score.Value == 0) ||
            (score.Kind == RunScoreKind.Stars && score.Value <= 2));

        return isNegative ? EvalCaseSource.NegativeScore : EvalCaseSource.ReferenceRun;
    }

    private async ValueTask<bool> HasEarlierRunInSameSessionAsync(
        string tenantId, RunRecord run, CancellationToken cancellationToken)
    {
        if (run.SessionId is not { Length: > 0 } sessionId)
        {
            return false;
        }

        var siblings = await _runs.QueryRunsAsync(
            new RunQuery
            {
                TenantId = tenantId,
                SessionId = sessionId,
                OnlyRootRuns = false,
                Take = 200,
            },
            cancellationToken).ConfigureAwait(false);

        return siblings.Any(sibling => sibling.Id != run.Id && sibling.StartedAt < run.StartedAt);
    }

    private async ValueTask<List<RunEvent>> ReadEventsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in _runs.ReadEventsAsync(runId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>
    /// Extracts model output from the event stream.
    /// </summary>
    /// <remarks>
    /// When <see cref="RunEventType.MessageCompleted"/> exists, it is used for a
    /// non-streaming run. Otherwise, <see cref="RunEventType.MessageDelta"/>
    /// fragments are joined for a streaming run. The same pattern appears in
    /// <c>Tracon.Testing.RunAssertions.ShouldHaveOutputContaining</c>. Do not
    /// combine both, because that duplicates text for a non-streaming run.
    /// </remarks>
    private static string? ExtractOutputText(IReadOnlyList<RunEvent> events)
    {
        var completed = events
            .Where(static runEvent => runEvent.Type == RunEventType.MessageCompleted)
            .Select(static runEvent => runEvent.Text ?? string.Empty)
            .ToList();

        var output = completed.Count > 0
            ? string.Concat(completed)
            : string.Concat(events
                .Where(static runEvent => runEvent.Type == RunEventType.MessageDelta)
                .Select(static runEvent => runEvent.Text ?? string.Empty));

        return output.Length > 0 ? output : null;
    }
}

/// <summary>Defines the outcome of <see cref="RunToCasePromoter.PromoteAsync"/>.</summary>
internal enum RunPromotionStatus
{
    /// <summary>The run does not exist or belongs to another tenant.</summary>
    RunNotFound,

    /// <summary>
    /// The run is neither <see cref="RunStatus.Failed"/> nor
    /// <see cref="RunStatus.Completed"/>, so the promotion reason must be explicit.
    /// </summary>
    AmbiguousSource,

    /// <summary>
    /// The run query could not be read: neither the recorded input nor the
    /// <c>RunStarted</c> event carries a request text, such as for an old record,
    /// an empty input, or a run that sent only documents.
    /// </summary>
    NoQuery,

    /// <summary>An earlier run exists in this run's session.</summary>
    MultiTurn,

    /// <summary>The case was created by this call.</summary>
    Created,

    /// <summary>The same run was already promoted, so the existing case was returned.</summary>
    AlreadyExists,

    /// <summary>
    /// The run's input holds something an eval case cannot carry, and promoting
    /// it would drop that part silently; <see cref="RunPromotionOutcome.Detail"/>
    /// says what.
    /// </summary>
    UnsupportedInput,
}

/// <summary>Where a promoted case's input was read from.</summary>
internal enum RunPromotionInputSource
{
    /// <summary>Nothing was read: the promotion stopped before the input.</summary>
    None,

    /// <summary>The run's recorded input (<see cref="IRunInputStore"/>): whole, with its documents and values.</summary>
    RecordedInput,

    /// <summary>The <c>RunStarted</c> event's text: the query only, cut at <c>MaxPayloadLength</c>.</summary>
    RunStartedEvent,
}

/// <summary>Represents the <see cref="RunToCasePromoter.PromoteAsync"/> outcome and its case, when present.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Case">The created or existing case; <see langword="null"/> for a refusal.</param>
internal sealed record RunPromotionOutcome(RunPromotionStatus Status, EvalCase? Case)
{
    /// <summary>For <see cref="RunPromotionStatus.UnsupportedInput"/>: what the case cannot carry. Counts only, never content.</summary>
    public string? Detail { get; init; }

    /// <summary>Where the case's input was read from.</summary>
    public RunPromotionInputSource InputSource { get; init; }

    /// <summary>
    /// Whether the case's query is the <c>RunStarted</c> text cut at
    /// <c>MaxPayloadLength</c>; the query then ends with
    /// <see cref="RunEventWriter.TruncationMarker"/>.
    /// </summary>
    public bool QueryTruncated { get; init; }
}

/// <summary>The input a promotion read for a case, or why it cannot make one.</summary>
/// <param name="Query">The request text; <see langword="null"/> when there is none.</param>
/// <param name="Context">The run's document blocks, joined by a line break; <see langword="null"/> for none.</param>
/// <param name="Parameters">The run's recorded parameter values; <see langword="null"/> for none.</param>
/// <param name="Source">Where it was read from.</param>
/// <param name="QueryTruncated">Whether <paramref name="Query"/> is a cut event text.</param>
/// <param name="Problem">What a case cannot carry, if anything.</param>
internal readonly record struct PromotedInput(
    string? Query,
    string? Context,
    IReadOnlyDictionary<string, string>? Parameters,
    RunPromotionInputSource Source,
    bool QueryTruncated,
    string? Problem)
{
    /// <summary>Builds the refusal for an input a case cannot carry.</summary>
    /// <param name="problem">What the case cannot carry.</param>
    /// <returns>The refusal.</returns>
    public static PromotedInput Unsupported(string problem)
        => new(null, null, null, RunPromotionInputSource.None, QueryTruncated: false, problem);
}
