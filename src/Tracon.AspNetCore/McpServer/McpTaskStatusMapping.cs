using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace Tracon;

/// <summary>Maps a run's status and output to the MCP task shape a poller sees.</summary>
/// <remarks>
/// <para>
/// <see cref="RunStatus.AwaitingApproval"/> and <see cref="RunStatus.AwaitingInput"/> map
/// to <see cref="McpTaskStatus.Completed"/>, not <see cref="McpTaskStatus.Failed"/>: in
/// this fallback path (used when <see cref="RunBackedMcpTaskStore"/>'s same-instance cache
/// missed — see its remarks), a synthesized error <c>CallToolResult</c> is built here,
/// which is exactly the shape <see cref="CatalogToolCallHandler"/>'s own approval rejection
/// produces on the cache-hit path. Never mapping either status to
/// <see cref="McpTaskStatus.InputRequired"/> is the actual safety boundary; which terminal
/// bucket carries the rejection is a wire-shape detail, not the safety property.
/// </para>
/// </remarks>
internal static class McpTaskStatusMapping
{
    /// <summary>Builds the <see cref="McpTaskInfo"/> a poller should see for a run.</summary>
    /// <param name="run">The run backing the task.</param>
    /// <param name="cachedOutcome">
    /// The exact payload <c>SetCompletedAsync</c>/<c>SetFailedAsync</c> received on THIS
    /// instance for this task, if any. Preferred over reconstruction because it is
    /// byte-identical to what a synchronous call would have returned.
    /// </param>
    /// <param name="runStore">Used to reconstruct output text from the event stream when there is no cache hit.</param>
    /// <param name="options">Supplies the advertised time-to-live and poll interval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<McpTaskInfo> ToTaskInfoAsync(
        RunRecord run,
        (McpTaskStatus Status, JsonElement Payload)? cachedOutcome,
        IRunStore runStore,
        TraconMcpServerOptions options,
        CancellationToken cancellationToken)
    {
        var lastUpdatedAt = run.CompletedAt ?? run.StartedAt;

        // A cache hit is authoritative over run.Status, not just an
        // optimization: CatalogToolCallHandler can return an ERROR
        // CallToolResult (an approval rejection — the K-103 case below — or a
        // caught non-cancellation exception) for a run whose OWN RunStatus
        // still ends up AwaitingApproval or Failed. The cache holds exactly
        // what the SDK already told THIS instance's client, in the wire shape
        // that goes with THAT call (a CallToolResult, always — never a
        // JsonRpcErrorDetail, since SetFailedAsync is reached only when the
        // pipeline never got as far as a CallToolResult at all). Branching on
        // run.Status here would return the RIGHT text in the WRONG field for
        // exactly the cases this store exists to get right.
        if (cachedOutcome is { } cached)
        {
            var info = new McpTaskInfo(
                run.Id.ToString(), cached.Status, run.StartedAt, lastUpdatedAt,
                options.TaskTimeToLive, (long)options.TaskPollInterval.TotalMilliseconds);

            return cached.Status == McpTaskStatus.Failed
                ? info with { Error = cached.Payload }
                : info with { Result = cached.Payload };
        }

        switch (run.Status)
        {
            case RunStatus.Queued or RunStatus.Running:
                return new McpTaskInfo(
                    run.Id.ToString(), McpTaskStatus.Working, run.StartedAt, lastUpdatedAt,
                    options.TaskTimeToLive, (long)options.TaskPollInterval.TotalMilliseconds);

            case RunStatus.Completed:
                {
                    var result = await BuildCompletedResultAsync(run, runStore, cancellationToken).ConfigureAwait(false);

                    return new McpTaskInfo(
                        run.Id.ToString(), McpTaskStatus.Completed, run.StartedAt, lastUpdatedAt,
                        options.TaskTimeToLive, (long)options.TaskPollInterval.TotalMilliseconds)
                    {
                        Result = result,
                    };
                }

            case RunStatus.Failed:
                return new McpTaskInfo(
                    run.Id.ToString(), McpTaskStatus.Failed, run.StartedAt, lastUpdatedAt,
                    options.TaskTimeToLive, (long)options.TaskPollInterval.TotalMilliseconds)
                {
                    Error = BuildFailedError(run),
                };

            case RunStatus.Canceled:
                return new McpTaskInfo(
                    run.Id.ToString(), McpTaskStatus.Cancelled, run.StartedAt, lastUpdatedAt,
                    options.TaskTimeToLive, (long)options.TaskPollInterval.TotalMilliseconds);

            case RunStatus.AwaitingApproval or RunStatus.AwaitingInput:
                // 🚨 K-103: never McpTaskStatus.InputRequired. On a cache hit
                // (the common case — see above) CatalogToolCallHandler's own
                // check already produced the real rejection text. This branch
                // is the cross-instance / narrow-race fallback, where only a
                // GENERIC rejection (no tool name — it is not recoverable from
                // IRunStore alone) is reconstructable; still never Failed and
                // never InputRequired, which is the actual safety property.
                return new McpTaskInfo(
                    run.Id.ToString(), McpTaskStatus.Completed, run.StartedAt, lastUpdatedAt,
                    options.TaskTimeToLive, (long)options.TaskPollInterval.TotalMilliseconds)
                {
                    Result = BuildApprovalRejectedResult(run),
                };

            default:
                throw new TraconException($"Unhandled run status '{run.Status}' for MCP task '{run.Id}'.");
        }
    }

    private static async Task<JsonElement> BuildCompletedResultAsync(
        RunRecord run, IRunStore runStore, CancellationToken cancellationToken)
    {
        var text = await ExtractOutputTextAsync(run.Id, runStore, cancellationToken).ConfigureAwait(false);

        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = text ?? "" }],
        };

        return JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
    }

    private static JsonElement BuildApprovalRejectedResult(RunRecord run)
    {
        var result = new CallToolResult
        {
            IsError = true,
            Content =
            [
                new TextContentBlock
                {
                    Text = $"Agent '{run.AgentName}' could not complete: a tool call requires user " +
                           "approval. An externally-invoked agent cannot respond to an approval request.",
                },
            ],
        };

        return JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
    }

    private static JsonElement BuildFailedError(RunRecord run)
    {
        var detail = new JsonRpcErrorDetail
        {
            Code = -32603,
            Message = run.Error?.Message ?? $"Run '{run.Id}' failed.",
        };

        return JsonSerializer.SerializeToElement(detail, McpJsonUtilities.DefaultOptions);
    }

    /// <summary>
    /// Same rule as <c>RunToCasePromoter.ExtractOutputText</c>: use
    /// <see cref="RunEventType.MessageCompleted"/> when present (non-streaming run — the only
    /// kind an MCP tool call ever produces), otherwise join
    /// <see cref="RunEventType.MessageDelta"/> fragments. Never both; that would duplicate text.
    /// </summary>
    private static async Task<string?> ExtractOutputTextAsync(Guid runId, IRunStore runStore, CancellationToken cancellationToken)
    {
        List<string>? completed = null;
        List<string>? delta = null;

        await foreach (var runEvent in runStore.ReadEventsAsync(runId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            switch (runEvent.Type)
            {
                case RunEventType.MessageCompleted:
                    (completed ??= []).Add(runEvent.Text ?? "");
                    break;
                case RunEventType.MessageDelta:
                    (delta ??= []).Add(runEvent.Text ?? "");
                    break;
            }
        }

        var text = completed is { Count: > 0 }
            ? string.Concat(completed)
            : delta is { Count: > 0 } ? string.Concat(delta) : null;

        return string.IsNullOrEmpty(text) ? null : text;
    }
}
