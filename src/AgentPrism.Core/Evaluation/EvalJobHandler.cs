using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Executes <see cref="JobKind.Eval"/> jobs: runs every case in an eval suite
/// against the agent being measured and writes the results to <see cref="IEvalStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same HTTP-independent path used by the HTTP layer and
/// <see cref="AgentBatchJobHandler"/> to resolve and run an agent is followed
/// here: each case is run in a <strong>new session</strong> (session: null) on
/// the agent resolved via <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>.
/// Since the agent is already wrapped by the run-recording decorator, each
/// case naturally produces its own <c>runs</c> row
/// (docs/18-DEGERLENDIRME.md, section 18.3).
/// </para>
/// <para>
/// The general job queue (<see cref="IJobStore"/>) carries only progress
/// (how many cases done/failed) and the lease/retry state machine; the rich
/// result (query, output, scores) is stored in <see cref="IEvalStore"/>,
/// independent of the job item. The job item's <see cref="JobItemRecord.Input"/>
/// field carries an <see cref="EvalCase.Id"/>.
/// </para>
/// </remarks>
internal sealed class EvalJobHandler(
    IEvalStore evalStore,
    IAgentCatalog catalog,
    EvalCheckRegistry checkRegistry,
    ILogger<EvalJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public JobKind Kind => JobKind.Eval;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (suiteName, modelOverride, numRepetitions, agentVersion) = ParsePayload(context.Job.Payload);

        var suite = await evalStore.GetSuiteAsync(context.Job.TenantId, suiteName, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"No eval suite named '{suiteName}' was found.");

        var evalRun = await evalStore
            .GetRunByJobIdAsync(context.Job.TenantId, context.Job.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AgentPrismException($"No eval run record was found for job id '{context.Job.Id}'.");

        try
        {
            await RunSuiteAsync(context, suite, evalRun, modelOverride, numRepetitions, agentVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Eval run failed: run={EvalRunId} suite={SuiteName}",
                    evalRun.Id,
                    suiteName);
            }

            await evalStore.CompleteRunAsync(
                new EvalRunCompletion
                {
                    EvalRunId = evalRun.Id,
                    Status = EvalRunStatus.Failed,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Total = evalRun.Total,
                    Passed = 0,
                    Failed = evalRun.Total,
                },
                CancellationToken.None).ConfigureAwait(false);

            throw;
        }
    }

    private async ValueTask RunSuiteAsync(
        JobContext context,
        EvalSuite suite,
        EvalRun evalRun,
        string? modelOverride,
        int numRepetitions,
        int? agentVersion,
        CancellationToken cancellationToken)
    {
        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.FirstOrDefault(
            candidate => string.Equals(candidate.Name, suite.AgentName, StringComparison.Ordinal));

        // The version is pinned: an eval run must answer "how good is this
        // version", even if the definition is updated while the run is in
        // progress.
        await evalStore
            .MarkRunRunningAsync(evalRun.Id, agentVersion ?? descriptor?.Version, modelOverride ?? descriptor?.Model?.Model, cancellationToken)
            .ConfigureAwait(false);

        // Eval is independent of the concept of an experiment: it runs against
        // a fixed version, not a variant. Rationale: docs/19-SURUM-KARSILASTIRMA-VE-AB.md,
        // open question 2.
        var agent = agentVersion is { } version
            ? await catalog.ResolveAsync(suite.AgentName, version, culture: null, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException($"Version {version} of agent '{suite.AgentName}' was not found.")
            : await catalog.ResolveAsync(suite.AgentName, culture: null, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"No agent named '{suite.AgentName}' exists in the catalog. The eval suite's agent may have been deleted.");

        var cases = await evalStore.ListCasesAsync(suite.Id, cancellationToken).ConfigureAwait(false);
        var casesById = cases.ToDictionary(static evalCase => evalCase.Id);

        var checks = checkRegistry.BuildChecks(suite.Checks);

        if (checks.Count == 0)
        {
            throw new AgentPrismException(
                $"Suite '{suite.Name}' has no checks; at least one check is required.");
        }

        var evaluator = new LocalEvaluator([.. checks]);

        var passedCases = 0;
        var failedCases = 0;
        long inputTokens = 0;
        long outputTokens = 0;
        var cancelled = false;

        foreach (var item in context.Items)
        {
            if (item.Status != JobItemStatus.Pending)
            {
                // Retry scenario: previously processed items are not re-run
                // when the lease expires and the job is picked up again.
                if (item.Status == JobItemStatus.Completed)
                {
                    passedCases++;
                }
                else if (item.Status == JobItemStatus.Failed)
                {
                    failedCases++;
                }

                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken).ConfigureAwait(false))
            {
                cancelled = true;
                break;
            }

            if (!Guid.TryParse(item.Input, out var caseId) || !casesById.TryGetValue(caseId, out var evalCase))
            {
                failedCases++;
                await context.ReportItemAsync(
                    new JobItemResult
                    {
                        JobId = context.Job.Id,
                        Seq = item.Seq,
                        Status = JobItemStatus.Failed,
                        Error = "The case no longer exists; it may have been deleted from the suite.",
                    },
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var (casePassed, caseInputTokens, caseOutputTokens) = await RunCaseAsync(
                evalRun,
                evalCase,
                agent,
                evaluator,
                suite.Name,
                numRepetitions,
                cancellationToken).ConfigureAwait(false);

            inputTokens += caseInputTokens;
            outputTokens += caseOutputTokens;

            if (casePassed)
            {
                passedCases++;
            }
            else
            {
                failedCases++;
            }

            await context.ReportItemAsync(
                new JobItemResult
                {
                    JobId = context.Job.Id,
                    Seq = item.Seq,
                    Status = casePassed ? JobItemStatus.Completed : JobItemStatus.Failed,
                    Error = casePassed ? null : "One or more checks failed.",
                },
                cancellationToken).ConfigureAwait(false);
        }

        await evalStore.CompleteRunAsync(
            new EvalRunCompletion
            {
                EvalRunId = evalRun.Id,
                Status = cancelled ? EvalRunStatus.Cancelled : EvalRunStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
                Total = cases.Count,
                Passed = passedCases,
                Failed = failedCases,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<(bool Passed, long InputTokens, long OutputTokens)> RunCaseAsync(
        EvalRun evalRun,
        EvalCase evalCase,
        AIAgent agent,
        LocalEvaluator evaluator,
        string evalName,
        int numRepetitions,
        CancellationToken cancellationToken)
    {
        var allRepetitionsPassed = true;
        long inputTokens = 0;
        long outputTokens = 0;

        for (var repetition = 0; repetition < numRepetitions; repetition++)
        {
            var runId = AgentPrismId.NewId();
            AgentResponse response;

            try
            {
                response = await agent
                    .RunAsync(
                        evalCase.Query,
                        session: null,
                        options: new AgentPrismRunOptions { RunId = runId, Kind = RunKind.Eval },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                allRepetitionsPassed = false;

                await evalStore.RecordCaseResultAsync(
                    new EvalCaseResult
                    {
                        EvalRunId = evalRun.Id,
                        CaseId = evalCase.Id,
                        RunId = runId,
                        Passed = false,
                        FailureReason = exception.Message,
                    },
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            inputTokens += response.Usage?.InputTokenCount ?? 0;
            outputTokens += response.Usage?.OutputTokenCount ?? 0;

            var evalItem = new EvalItem(evalCase.Query, response.Text, [.. response.Messages])
            {
                ExpectedOutput = evalCase.ExpectedOutput,
                Context = evalCase.Context,
            };

            var results = await evaluator.EvaluateAsync([evalItem], evalName, cancellationToken).ConfigureAwait(false);

            // 🚨 LocalEvaluator.DetailedItems stays empty (populated only by
            // reporting backends, measured); the single source of truth is
            // Items[0].Metrics - since the batch size is 1, it carries all of
            // this case's check results.
            var metrics = results.Items is [var evaluationResult, ..]
                ? evaluationResult.Metrics
                : throw new AgentPrismException("The evaluator produced no result.");

            var passed = results.AllPassed;

            if (!passed)
            {
                allRepetitionsPassed = false;
            }

            await evalStore.RecordCaseResultAsync(
                new EvalCaseResult
                {
                    EvalRunId = evalRun.Id,
                    CaseId = evalCase.Id,
                    RunId = runId,
                    Passed = passed,
                    Output = response.Text,
                    Scores = SerializeScores(metrics),
                    FailureReason = DescribeFailure(passed, metrics),
                },
                cancellationToken).ConfigureAwait(false);
        }

        return (allRepetitionsPassed, inputTokens, outputTokens);
    }

    private static (string SuiteName, string? ModelId, int NumRepetitions, int? AgentVersion) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("suiteName", out var suiteNameElement) ||
            suiteNameElement.ValueKind != JsonValueKind.String)
        {
            throw new AgentPrismException("The eval job payload must carry a 'suiteName' (string) field.");
        }

        var modelId = payload.TryGetProperty("modelId", out var modelElement) &&
            modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString()
            : null;

        var numRepetitions = payload.TryGetProperty("numRepetitions", out var repsElement) &&
            repsElement.ValueKind == JsonValueKind.Number
            ? Math.Max(1, repsElement.GetInt32())
            : 1;

        var agentVersion = payload.TryGetProperty("agentVersion", out var versionElement) &&
            versionElement.ValueKind == JsonValueKind.Number
            ? versionElement.GetInt32()
            : (int?)null;

        return (suiteNameElement.GetString()!, modelId, numRepetitions, agentVersion);
    }

    private static string? DescribeFailure(bool passed, IDictionary<string, EvaluationMetric> metrics)
    {
        if (passed)
        {
            return null;
        }

        var failing = string.Join(
            "; ",
            metrics.Values
                .Where(static metric => metric.Interpretation?.Failed == true)
                .Select(static metric => metric.Reason is { Length: > 0 } reason ? $"{metric.Name}: {reason}" : metric.Name));

        return failing.Length > 0 ? failing : "A check failed.";
    }

    /// <summary>
    /// Converts check metrics into a <see cref="JsonElement"/> to be written
    /// to the jsonb column.
    /// </summary>
    /// <remarks>
    /// Written by hand with <c>Utf8JsonWriter</c>; the reflection-based
    /// <c>JsonSerializer.Serialize</c> is not used - since there is no
    /// source-generated context for <c>EvaluationMetric</c>, it would produce
    /// IL2026/IL3050. Library code must stay AOT-compatible.
    /// </remarks>
    private static JsonElement SerializeScores(IDictionary<string, EvaluationMetric> metrics)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();

            foreach (var metric in metrics.Values)
            {
                writer.WriteStartObject();
                writer.WriteString("name", metric.Name);

                if (metric.Interpretation is { } interpretation)
                {
                    writer.WriteBoolean("passed", !interpretation.Failed);
                }
                else
                {
                    writer.WriteNull("passed");
                }

                if (metric.Reason is { Length: > 0 } reason)
                {
                    writer.WriteString("reason", reason);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        buffer.Position = 0;
        using var document = JsonDocument.Parse(buffer);
        return document.RootElement.Clone();
    }
}
