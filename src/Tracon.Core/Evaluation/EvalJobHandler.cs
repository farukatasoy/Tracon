using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
    IOptionsMonitor<TraconSchedulingOptions>? schedulingOptions = null,
    TimeProvider? timeProvider = null,
    IRunStore? runStore = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

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

        SuiteOutcome outcome;

        // ONLY the cases sit in this try. The write that closes the
        // run sat in it too, so a store error while recording a finished eval
        // was caught here as the eval's own failure: the run was written Failed
        // and the error thrown on, and the worker ran the job again or dropped it.
        try
        {
            outcome = await RunSuiteAsync(context, suite, evalRun, modelOverride, numRepetitions, agentVersion, cancellationToken)
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

            // The write that records the failed attempt is tried again, and a
            // store error it still meets is logged, never thrown: thrown, it replaced
            // the attempt's own failure, and the worker judged the store error instead
            // (a final failure was run again).
            try
            {
                await new JobOutcomeWriteRetry(context.Job.Id, schedulingOptions?.CurrentValue, _clock, logger)
                    .RunAsync(
                        () => evalStore.CompleteRunAsync(
                            new EvalRunCompletion
                            {
                                EvalRunId = evalRun.Id,
                                Status = EvalRunStatus.Failed,
                                CompletedAt = DateTimeOffset.UtcNow,
                                Total = evalRun.Total,
                                Passed = 0,
                                Failed = evalRun.Total,
                            },
                            CancellationToken.None),
                        $"record eval run {evalRun.Id} as failed",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception storeException) when (OperationCancellation.IsFailure(storeException, cancellationToken))
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(storeException, "Eval run {EvalRunId} could not be recorded as failed.", evalRun.Id);
                }
            }

            // The job is retried after this throw while attempts remain, and a
            // retry that completes overwrites the row: only the last attempt's
            // failure is the eval's outcome. A final failure is the last
            // attempt whatever attempts are left: the worker does not run it again.
            if (FinalFailure.IsFinal(exception)
                || context.Job.Attempt >= (context.Job.MaxAttempts ?? schedulingOptions?.CurrentValue.MaxAttempts ?? 1))
            {
                await PublishCompletedAsync(
                    context.Job.TenantId,
                    evalRun with { Status = EvalRunStatus.Failed, Passed = 0, Failed = evalRun.Total },
                    suiteName,
                    suite.AgentName).ConfigureAwait(false);
            }

            throw;
        }

        // The cases are done; a store error from here on is the store's. The
        // write is tried again, and the completion is the same on every try. If
        // the store keeps refusing it, the error goes to the worker, and the
        // next attempt runs no case again: every case's item is already
        // reported, so it only repeats this write.
        await new JobOutcomeWriteRetry(context.Job.Id, schedulingOptions?.CurrentValue, _clock, logger)
            .RunAsync(
                () => evalStore.CompleteRunAsync(outcome.Completion, CancellationToken.None),
                $"record eval run {evalRun.Id}'s outcome",
                cancellationToken)
            .ConfigureAwait(false);

        await PublishCompletedAsync(context.Job.TenantId, outcome.Summary, suite.Name, suite.AgentName).ConfigureAwait(false);
    }

    /// <summary>Runs the suite's cases and returns what the eval run is closed with.</summary>
    /// <remarks>
    /// Writes nothing that closes the run: the caller writes the returned
    /// completion outside the try that guards the cases, so a store error on
    /// that write is never taken for a failed case.
    /// </remarks>
    private async ValueTask<SuiteOutcome> RunSuiteAsync(
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
        // Read BEFORE the row is marked running: a pinned version's
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
        // a fixed version, not a variant.
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

        var status = cancelled ? EvalRunStatus.Cancelled : EvalRunStatus.Completed;

        return new SuiteOutcome(
            new EvalRunCompletion
            {
                EvalRunId = evalRun.Id,
                Status = status,
                CompletedAt = DateTimeOffset.UtcNow,
                Total = cases.Count,
                Passed = passedCases,
                Failed = failedCases,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            },
            evalRun with
            {
                Status = status,
                AgentVersion = agentVersion ?? evalRun.AgentVersion,
                Total = cases.Count,
                Passed = passedCases,
                Failed = failedCases,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            });
    }

    /// <summary>What a finished suite closes its eval run with.</summary>
    /// <param name="Completion">The write that closes the run.</param>
    /// <param name="Summary">The run as <c>eval.completed</c> reports it.</param>
    private readonly record struct SuiteOutcome(EvalRunCompletion Completion, EvalRun Summary);

    /// <summary>Publishes <c>eval.completed</c> for an evaluation run that reached its final status.</summary>
    /// <remarks>
    /// The payload is a summary (counts and tokens), never a case's input or
    /// output. A failure here is logged and changes nothing about the run.
    /// </remarks>
    // eval.completed was documented and accepted on subscribe, but no code
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
        // The values the case run is bound with, defaults included - what every
        // other run path records (ParameterizedAgentFactory.BindAsync), so a
        // replay of this run, or a promotion of it, repeats them. Null for an
        // agent that declares no parameters.
        IReadOnlyDictionary<string, string>? boundValues = null;

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

            boundValues = ParameterValueSet.Resolve(schemaDefinition.Parameters, evalCase.Parameters);
        }

        var messages = BuildCaseMessages(evalCase);
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
                        messages,
                        session: null,
                        options: new TraconRunOptions
                        {
                            RunId = runId,
                            Kind = RunKind.Eval,

                            // The case run measures the PINNED version; without
                            // this its row fell back to the descriptor's version.
                            AgentVersion = agentVersion,
                            ParameterValues = boundValues,
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

            // A case runs without a session, so an approval it stops at can
            // never be answered. The case fails without being judged, and its run
            // is closed instead of waiting forever.
            if (SessionlessApprovalRun.StoppedAtApproval(response.Messages))
            {
                allRepetitionsPassed = false;

                await SessionlessApprovalRun
                    .CloseAsFailedAsync(runStore, runId, evalRun.TenantId, _clock.GetUtcNow(), logger, cancellationToken)
                    .ConfigureAwait(false);

                await evalStore.RecordCaseResultAsync(
                    new EvalCaseResult
                    {
                        EvalRunId = evalRun.Id,
                        CaseId = evalCase.Id,
                        RunId = runId,
                        Passed = false,
                        FailureReason = SessionlessApprovalRun.Message,
                    },
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            // The evaluator keeps getting the context as well: a groundedness
            // style check judges the answer against that reference text.
            var evalItem = new EvalItem(evalCase.Query, response.Text, [.. response.Messages])
            {
                ExpectedOutput = evalCase.ExpectedOutput,
                Context = evalCase.Context,
            };

            // The expected tool calls reach the item in two forms: MAF's, for an
            // evaluator a consumer registers, and the exact one the built-in
            // toolCallArgsMatch check compares by JSON meaning.
            ToolCallArgumentsCheck.Attach(evalItem, evalCase.ExpectedToolCalls);

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

    /// <summary>The document name a plain-text <see cref="EvalCase.Context"/> reaches the agent under.</summary>
    internal const string ContextDocumentName = "context";

    // The case used to run as RunAsync(Query) and the context reached
    // the evaluator only. The agent under test never saw the reference text a
    // production run carried, so a promoted case measured a different input
    // than the run it stood for.

    /// <summary>
    /// Builds the messages a case run sends: the case's context through the
    /// document channel, then the query - the order a production run with
    /// documents uses (<c>AgentEndpoints.BuildMessagesAsync</c>).
    /// </summary>
    /// <remarks>
    /// A promoted context holds the run's document blocks verbatim and goes back
    /// as one document each (<see cref="DocumentChannelMessageBuilder.BuildFromText"/>),
    /// so the case repeats the messages the run sent.
    /// </remarks>
    internal static IReadOnlyList<ChatMessage> BuildCaseMessages(EvalCase evalCase)
    {
        ArgumentNullException.ThrowIfNull(evalCase);

        var query = new ChatMessage(ChatRole.User, evalCase.Query);

        return string.IsNullOrWhiteSpace(evalCase.Context)
            ? [query]
            : [.. DocumentChannelMessageBuilder.BuildFromText(evalCase.Context, ContextDocumentName), query];
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
