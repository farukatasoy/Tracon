using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Executes <see cref="JobHandlerKeys.Eval"/> jobs: runs every case in an eval suite
/// against the agent being measured and writes the results to <see cref="IEvalStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same HTTP-independent path used by the HTTP layer and
/// <see cref="AgentBatchJobHandler"/> to resolve and run an agent is followed
/// here: each case is run in a <strong>new session</strong> (session: null) on
/// the agent resolved via <see cref="IAgentCatalog.ResolveAsync(string, string, CancellationToken)"/>.
/// Since the agent is already wrapped by the run-recording decorator, each
/// case naturally produces its own <c>runs</c> row.
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
    IAgentDefinitionStore definitionStore,
    AgentDefinitionCompiler compiler,
    EvalCheckRegistry checkRegistry,
    IEvalEvaluatorFactory evaluatorFactory,
    IOptions<TraconOptions> options,
    IEnumerable<IAgentDecorator> decorators,
    ILogger<EvalJobHandler>? logger = null,
    IEnumerable<CodeAgentRegistration>? codeRegistrations = null,
    IWebhookPublisher? webhookPublisher = null,
    IOptionsMonitor<TraconSchedulingOptions>? schedulingOptions = null) : IJobHandler
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (suiteName, modelOverride, numRepetitions, agentVersion) = ParsePayload(context.Job.Payload);

        var suite = await evalStore.GetSuiteAsync(context.Job.TenantId, suiteName, cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException($"No eval suite named '{suiteName}' was found.");

        var evalRun = await evalStore
            .GetRunByJobIdAsync(context.Job.TenantId, context.Job.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new TraconException($"No eval run record was found for job id '{context.Job.Id}'.");

        try
        {
            await RunSuiteAsync(context, suite, evalRun, modelOverride, numRepetitions, agentVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
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

            // The job is retried after this throw while attempts remain, and a
            // retry that completes overwrites the row: only the last attempt's
            // failure is the eval's outcome.
            if (context.Job.Attempt >= (context.Job.MaxAttempts ?? schedulingOptions?.CurrentValue.MaxAttempts ?? 1))
            {
                await PublishCompletedAsync(
                    context.Job.TenantId,
                    evalRun with { Status = EvalRunStatus.Failed, Passed = 0, Failed = evalRun.Total },
                    suiteName,
                    suite.AgentName).ConfigureAwait(false);
            }

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

        // 🚨 Read separately from the resolved AIAgent below: a parameterized
        // agent cannot be evaluated with ONE shared compiled instance - each
        // case can carry different AgentParameter values, so each case's
        // instructions text (after binding) can differ. Null for an agent with
        // no definition (a factory agent, an agent source's), exactly like
        // AgentParameterGate on the HTTP side; such an agent has no schema and
        // every case below runs against the shared `agent`.
        //
        // Read BEFORE the row is marked running (F-331): a pinned version's
        // model is that version's, not the current definition's the catalog
        // lists.
        //
        // The same lookup as the /run gate: a definition registered in code
        // wins over a stored one, as it does in the catalog.
        var (sourceDefinition, sourceOrigin) = await AgentDefinitionLookup
            .FindAsync(codeRegistrations ?? [], definitionStore, suite.AgentName, agentVersion, cancellationToken)
            .ConfigureAwait(false);

        // The version is pinned: an eval run must answer "how good is this
        // version", even if the definition is updated while the run is in
        // progress.
        await evalStore
            .MarkRunRunningAsync(
                evalRun.Id,
                agentVersion ?? descriptor?.Version,
                modelOverride ?? sourceDefinition?.Model.Model ?? descriptor?.Model?.Model,
                cancellationToken)
            .ConfigureAwait(false);

        // Eval is independent of the concept of an experiment: it runs against
        // a fixed version, not a variant. Rationale: docs/arsiv/fazlar/19-SURUM-KARSILASTIRMA-VE-AB.md,
        // open question 2.
        var agent = agentVersion is { } version
            ? await catalog.ResolveAsync(suite.AgentName, version, culture: null, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException($"Version {version} of agent '{suite.AgentName}' was not found.")
            : await catalog.ResolveAsync(suite.AgentName, culture: null, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconException(
                    $"No agent named '{suite.AgentName}' exists in the catalog. The eval suite's agent may have been deleted.");

        var cases = await evalStore.ListCasesAsync(suite.Id, cancellationToken).ConfigureAwait(false);
        var casesById = cases.ToDictionary(static evalCase => evalCase.Id);

        var checks = checkRegistry.BuildChecks(suite.Checks);

        if (checks.Count == 0)
        {
            throw new TraconException(
                $"Suite '{suite.Name}' has no checks; at least one check is required.");
        }

        // The seam, not a hard-coded LocalEvaluator: the default factory still
        // builds one from these very checks, so registering nothing keeps the
        // previous behaviour exactly (IEvalEvaluatorFactory).
        var evaluator = evaluatorFactory.Create(checks)
            ?? throw new TraconException(
                $"{evaluatorFactory.GetType().Name} returned no evaluator for suite '{suite.Name}'.");

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
                sourceDefinition,
                sourceOrigin,
                agentVersion,
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

        await PublishCompletedAsync(
            context.Job.TenantId,
            evalRun with
            {
                Status = cancelled ? EvalRunStatus.Cancelled : EvalRunStatus.Completed,
                AgentVersion = agentVersion ?? evalRun.AgentVersion,
                Total = cases.Count,
                Passed = passedCases,
                Failed = failedCases,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            },
            suite.Name,
            suite.AgentName).ConfigureAwait(false);
    }

    /// <summary>Publishes <c>eval.completed</c> for an evaluation run that reached its final status.</summary>
    /// <remarks>
    /// The payload is a summary (counts and tokens), never a case's input or
    /// output. A failure here is logged and changes nothing about the run.
    /// </remarks>
    // F-333: eval.completed was documented and accepted on subscribe, but no code
    // published it.
    private async ValueTask PublishCompletedAsync(string tenantId, EvalRun evalRun, string suiteName, string? agentName)
    {
        if (webhookPublisher is null)
        {
            return;
        }

        try
        {
            await webhookPublisher.PublishAsync(
                tenantId,
                WebhookEvents.EvalCompleted,
                new WebhookEventPayload
                {
                    OccurredAt = DateTimeOffset.UtcNow,
                    Eval = new WebhookEvalSummary
                    {
                        EvalRunId = evalRun.Id.ToString(),
                        SuiteName = suiteName,
                        AgentName = agentName,
                        AgentVersion = evalRun.AgentVersion,
                        ModelId = evalRun.ModelId,
                        Status = evalRun.Status.ToString(),
                        Total = evalRun.Total,
                        Passed = evalRun.Passed,
                        Failed = evalRun.Failed,
                        InputTokens = evalRun.InputTokens,
                        OutputTokens = evalRun.OutputTokens,
                    },
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The eval.completed webhook of eval run {EvalRunId} could not be published.", evalRun.Id);
            }
        }
    }

    private async ValueTask<(bool Passed, long InputTokens, long OutputTokens)> RunCaseAsync(
        EvalRun evalRun,
        EvalCase evalCase,
        AIAgent agent,
        AgentDefinition? sourceDefinition,
        AgentDefinitionOrigin sourceOrigin,
        int? agentVersion,
        IAgentEvaluator evaluator,
        string evalName,
        int numRepetitions,
        CancellationToken cancellationToken)
    {
        // 🚨 Checked BEFORE any call reaches the provider - the same
        // "missing/unknown parameter never runs" rule
        // POST /api/agents/{name}/run applies (AgentParameterGate). A
        // parameterless case run against a parameterless agent (the
        // overwhelming majority) never reaches AgentParameterValidator at
        // all: schema.Count == 0 short-circuits inside ValidateValues.
        if (sourceDefinition is { Parameters.Count: > 0 } schemaDefinition)
        {
            var validation = AgentParameterValidator.ValidateValues(
                schemaDefinition.Parameters,
                evalCase.Parameters,
                options.Value.MaxParameterValueLength);

            if (!validation.IsValid)
            {
                var reason = string.Join(
                    " ",
                    validation.Errors.Select(static error =>
                    {
                        var kind = error.Code switch
                        {
                            AgentParameterValidator.MissingParameterCode => "Missing",
                            AgentParameterValidator.ValueTooLongCode => "Too long",
                            _ => "Unknown",
                        };

                        return $"{kind} parameter '{error.ParameterName}'.";
                    }));

                await evalStore.RecordCaseResultAsync(
                    new EvalCaseResult
                    {
                        EvalRunId = evalRun.Id,
                        CaseId = evalCase.Id,
                        RunId = null,
                        Passed = false,
                        FailureReason = reason,
                    },
                    cancellationToken).ConfigureAwait(false);

                return (false, 0, 0);
            }

            // 🚨 Never CompiledAgentCache - see CompileParameterizedAsync's
            // remarks. Each case in the suite can carry different values, so
            // this compiles fresh once per case, not once per suite the way
            // the shared `agent` above does. This bypasses IAgentCatalog
            // entirely (not just the cache), so AgentDecoratorPipeline is
            // applied by hand below - otherwise this case's run goes out
            // undecorated: no run recording, no telemetry.
            agent = await compiler
                .CompileParameterizedAsync(schemaDefinition, culture: null, evalCase.Parameters, cancellationToken)
                .ConfigureAwait(false);

            agent = AgentDecoratorPipeline.Apply(
                agent,
                AgentDescriptorProjection.FromDefinition(
                    schemaDefinition,
                    sourceOrigin,
                    AgentDefinitionLookup.SourceName(sourceOrigin)),
                decorators);
        }

        var allRepetitionsPassed = true;
        long inputTokens = 0;
        long outputTokens = 0;

        for (var repetition = 0; repetition < numRepetitions; repetition++)
        {
            var runId = TraconId.NewId();
            AgentResponse response;

            try
            {
                response = await agent
                    .RunAsync(
                        evalCase.Query,
                        session: null,
                        options: new TraconRunOptions
                        {
                            RunId = runId,
                            Kind = RunKind.Eval,

                            // F-331: the case run measures the PINNED version; without
                            // this its row fell back to the descriptor's version.
                            AgentVersion = agentVersion,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                allRepetitionsPassed = false;

                var correlationId = SafeErrorText.NewCorrelationId();

                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Eval case {CaseId} failed. (ref: {CorrelationId})", evalCase.Id, correlationId);
                }

                await evalStore.RecordCaseResultAsync(
                    new EvalCaseResult
                    {
                        EvalRunId = evalRun.Id,
                        CaseId = evalCase.Id,
                        RunId = runId,
                        Passed = false,
                        FailureReason = SafeErrorText.ForPersistence(exception, correlationId),
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

            // 🚨 AgentEvaluationResults.DetailedItems stays empty (populated only by
            // reporting backends, measured); the single source of truth is
            // Items[0].Metrics - since the batch size is 1, it carries all of
            // this case's check results.
            var metrics = results.Items is [var evaluationResult, ..]
                ? evaluationResult.Metrics
                : throw new TraconException("The evaluator produced no result.");

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
            throw new TraconException("The eval job payload must carry a 'suiteName' (string) field.");
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
    /// <para>
    /// Written by hand with <c>Utf8JsonWriter</c>; the reflection-based
    /// <c>JsonSerializer.Serialize</c> is not used - since there is no
    /// source-generated context for <c>EvaluationMetric</c>, it would produce
    /// IL2026/IL3050. Library code must stay AOT-compatible. The concrete
    /// metric shape is told apart by pattern matching, never by reflection.
    /// </para>
    /// </remarks>
    internal static JsonElement SerializeScores(IDictionary<string, EvaluationMetric> metrics)
    {
        // 🚨 Metadata is DELIBERATELY NOT written. It is a scope boundary, not
        // an omission: EvaluationMetricExtensions.AddOrUpdateChatMetadata can
        // put model response metadata there, and EvalCaseResult.Scores is
        // served to clients. Context is left out for the same reason -- it
        // carries the evaluator's own input, which can quote the run.
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();

            foreach (var metric in metrics.Values)
            {
                writer.WriteStartObject();
                writer.WriteString("name", metric.Name);
                WriteMetricValue(writer, metric);

                if (metric.Interpretation is { } interpretation)
                {
                    writer.WriteBoolean("passed", !interpretation.Failed);
                    writer.WriteString("rating", interpretation.Rating.ToString());
                }
                else
                {
                    writer.WriteNull("passed");
                }

                if (metric.Reason is { Length: > 0 } reason)
                {
                    writer.WriteString("reason", reason);
                }

                WriteDiagnostics(writer, metric);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        buffer.Position = 0;
        using var document = JsonDocument.Parse(buffer);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Writes the metric's <c>kind</c> and <c>value</c>, matched on the concrete
    /// metric shape.
    /// </summary>
    /// <remarks>
    /// The three shapes are the ones <see cref="RunScoreKind"/> is aligned with.
    /// A metric of an unknown shape gets a <c>kind</c> and no <c>value</c>
    /// rather than being dropped silently, so a new shape is visible in the
    /// stored result instead of vanishing from it.
    /// </remarks>
    private static void WriteMetricValue(Utf8JsonWriter writer, EvaluationMetric metric)
    {
        switch (metric)
        {
            case BooleanMetric { Value: { } boolean }:
                writer.WriteString("kind", "boolean");
                writer.WriteBoolean("value", boolean);
                break;

            case BooleanMetric:
                writer.WriteString("kind", "boolean");
                writer.WriteNull("value");
                break;

            case NumericMetric { Value: { } number }:
                writer.WriteString("kind", "numeric");
                writer.WriteNumber("value", number);
                break;

            case NumericMetric:
                writer.WriteString("kind", "numeric");
                writer.WriteNull("value");
                break;

            case StringMetric { Value: { } text }:
                writer.WriteString("kind", "string");
                writer.WriteString("value", text);
                break;

            case StringMetric:
                writer.WriteString("kind", "string");
                writer.WriteNull("value");
                break;

            default:
                writer.WriteString("kind", "unknown");
                break;
        }
    }

    /// <summary>Writes the metric's diagnostics, if it carries any.</summary>
    private static void WriteDiagnostics(Utf8JsonWriter writer, EvaluationMetric metric)
    {
        if (metric.Diagnostics is not { Count: > 0 } diagnostics)
        {
            return;
        }

        writer.WriteStartArray("diagnostics");

        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("severity", diagnostic.Severity.ToString());
            writer.WriteString("message", diagnostic.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
