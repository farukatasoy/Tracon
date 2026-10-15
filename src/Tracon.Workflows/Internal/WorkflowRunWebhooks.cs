namespace Tracon;

/// <summary>
/// Builds the <c>run.completed</c> and <c>run.failed</c> payload of a workflow root, for every place
/// that closes one.
/// </summary>
/// <remarks>
/// <para>
/// A run is closed in two places: the runner, when the graph ends, and the request expiry service, when a
/// human request nobody answered runs out of time (F-539). Before this helper the payload existed only
/// in the runner, and a run closed by the service published nothing - the receiver of the webhook never
/// learnt that a request it was told to wait for had expired. Two places that build the same payload
/// by hand would let the second drift from the first, as the summing of cost already did once (K-483);
/// they share this one.
/// </para>
/// <para>
/// The summary carries the tree's tokens and cost: the workflow row has none of its own. The amount the
/// tree reported beyond tokens is added to the token cost in ONE place (<see cref="RunSpend"/>) and also
/// given on its own.
/// </para>
/// </remarks>
internal static class WorkflowRunWebhooks
{
    /// <summary>Gets the webhook event a closed workflow root publishes.</summary>
    /// <param name="status">How the run closed.</param>
    /// <returns>The event name; <see langword="null"/> for a status that publishes none (a run that was cancelled).</returns>
    public static string? EventFor(RunStatus status)
        => status switch
        {
            RunStatus.Completed => WebhookEvents.RunCompleted,
            RunStatus.Failed => WebhookEvents.RunFailed,
            _ => null,
        };

    /// <summary>Builds the payload of a closed workflow root.</summary>
    /// <param name="occurredAt">When the run closed.</param>
    /// <param name="runId">The run.</param>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="workflowName">The workflow's name.</param>
    /// <param name="status">How the run closed.</param>
    /// <param name="duration">How long the run lasted.</param>
    /// <param name="record">The run's row as the store holds it after the close; <see langword="null"/> when the store cannot answer.</param>
    /// <param name="nonTokenCost">The run's own sealed snapshot held in memory; <see langword="null"/> when the caller has none.</param>
    /// <param name="error">The run's error message; <see langword="null"/> when it ended without one.</param>
    /// <returns>The payload.</returns>
    public static WebhookEventPayload RunPayload(
        DateTimeOffset occurredAt,
        Guid runId,
        string sessionId,
        string workflowName,
        RunStatus status,
        TimeSpan duration,
        RunRecord? record,
        RunNonTokenCost? nonTokenCost,
        string? error)
    {
        var treeNonTokenCost = WorkflowTreeSpend.NonTokenCost(record, nonTokenCost);

        return new WebhookEventPayload
        {
            OccurredAt = occurredAt,
            Run = new WebhookRunSummary
            {
                RunId = runId.ToString(),
                RootRunId = runId.ToString(),
                SessionId = sessionId,
                AgentName = workflowName,
                Kind = nameof(RunKind.Workflow),
                Status = status.ToString(),
                DurationMs = (long)duration.TotalMilliseconds,
                InputTokens = record?.TreeUsage?.InputTokens,
                OutputTokens = record?.TreeUsage?.OutputTokens,
                Cost = RunSpend.Total(record?.TreeCost, treeNonTokenCost),
                NonTokenCost = treeNonTokenCost?.Amount,
                Currency = record?.TreeCost?.Currency ?? treeNonTokenCost?.Currency,
                Error = error,
            },
        };
    }
}
