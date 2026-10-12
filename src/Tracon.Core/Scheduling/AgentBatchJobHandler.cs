using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Handles <see cref="JobHandlerKeys.AgentBatch"/> jobs. It runs a registered agent in
/// sequence for each input in the job items.
/// </summary>
/// <remarks>
/// <para>
/// It follows the same HTTP-independent path as the HTTP layer to resolve and run
/// agents. An agent resolved by <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>
/// is already wrapped in the run-recording decorator, so every run writes a normal
/// <c>runs</c> row through <see cref="IRunStore"/>. It processes items <strong>in sequence</strong>
/// because provider rate limits would constrain parallelism. Parallelism between jobs
/// is already provided by <see cref="TraconSchedulingOptions.MaxConcurrentJobs"/>.
/// </para>
/// <para>
/// A structured item (<see cref="BatchItemInput"/>) carries its own parameter
/// values and documents. The values are checked per item against the agent's schema, so
/// one invalid item fails ALONE and the rest of the batch runs. A batch in which no item
/// carries a value keeps the earlier rule: an agent whose required parameter has no
/// default fails the job once, up front, instead of once per item with the same error.
/// </para>
/// </remarks>
internal sealed class AgentBatchJobHandler(
    IAgentCatalog catalog,
    ILogger<AgentBatchJobHandler>? logger = null,
    ParameterizedAgentFactory? parameterFactory = null) : IJobHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Resolved once up front so a job naming no agent fails as a whole, as before.
        var probe = await catalog.ResolveAsync(context.Job.TargetName, culture: null, cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException(
                $"The agent named '{context.Job.TargetName}' was not found. The job will be marked as failed.");

        // Each item is read once: the shape decides whether it carries values,
        // and the loop below runs on the same reading.
        var inputs = new Dictionary<long, BatchItemInput>();
        var anyItemCarriesValues = false;

        foreach (var item in context.Items)
        {
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            var parsed = BatchItemInput.Parse(item.Input);

            inputs[item.Seq] = parsed;
            anyItemCarriesValues |= parsed.HasParameterValues;
        }

        // F-330: a batch whose items carry no parameter values binds every
        // default. An agent whose required parameter has no default fails the
        // job once, here, rather than once per item with the same error. When an
        // item DOES carry values the check moves to the item (below): the batch
        // must not fall because one item forgot a value another supplied.
        if (!anyItemCarriesValues && probe.GetService<UnboundParameterGuard>() is { RequiresValues: true } unbound)
        {
            throw unbound.Failure();
        }

        foreach (var item in context.Items)
        {
            // Retry scenario: when the lease expires and the job is claimed again,
            // items already processed successfully do not run again.
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            var runId = TraconId.NewId();
            string? error = null;

            try
            {
                var input = inputs[item.Seq];

                if (input.Problem is { } problem)
                {
                    // A malformed structured item fails alone, with its reason;
                    // it is never run as plain text with its JSON as the message.
                    throw new TraconException(problem);
                }

                // F-310: resolved again for every item. A batch can run for hours, and an
                // agent resolved once kept its MCP tools bound to a connection that a
                // server edit or delete had closed - every later item called it. A
                // cache hit while nothing changed. An item whose agent no longer
                // resolves fails on its own, like an item whose run fails.
                var agent = await catalog.ResolveAsync(context.Job.TargetName, culture: null, cancellationToken).ConfigureAwait(false)
                    ?? throw new TraconException($"The agent named '{context.Job.TargetName}' no longer exists.");

                IReadOnlyDictionary<string, string>? recordedValues = null;

                // F-294: an agent that declares a schema carries the guard the
                // compiler puts on it (no store read to find out); a plain agent
                // and an item without values skip the lookup entirely.
                if (input.HasParameterValues || agent.GetService<UnboundParameterGuard>() is not null)
                {
                    if (parameterFactory is null)
                    {
                        throw new TraconException(
                            "This host cannot bind run parameters: the parameter factory is not registered.");
                    }

                    var binding = await parameterFactory
                        .BindAsync(context.Job.TargetName, version: null, culture: null, input.Parameters, cancellationToken)
                        .ConfigureAwait(false);

                    agent = binding.Agent ?? agent;
                    recordedValues = binding.Values;
                }

                // Documents first, one message each, then the request: the order
                // the synchronous run builds.
                List<ChatMessage> messages =
                [
                    .. input.Documents.Select(DocumentChannelMessageBuilder.Build),
                    new(ChatRole.User, input.Message),
                ];

                await agent
                    .RunAsync(
                        messages,
                        session: null,
                        options: new TraconRunOptions { RunId = runId, ParameterValues = recordedValues },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                var correlationId = SafeErrorText.NewCorrelationId();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(
                        exception,
                        "Batch job item failed: job={JobId} sequence={Seq} agent={AgentName} (ref: {CorrelationId})",
                        context.Job.Id,
                        item.Seq,
                        context.Job.TargetName,
                        correlationId);
                }

                error = SafeErrorText.ForPersistence(exception, correlationId);
            }

            // 🚨 F-348: the report sits OUTSIDE the run's try, as in
            // WorkflowJobHandler. Inside it, a store error while reporting a
            // completed run was caught as the run's failure, and the item was
            // recorded as Failed. A report error now ends the attempt, and
            // the worker decides whether the job is tried again.
            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = item.Seq,
                    Status = error is null ? JobItemStatus.Completed : JobItemStatus.Failed,
                    RunId = runId,
                    Error = error,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
