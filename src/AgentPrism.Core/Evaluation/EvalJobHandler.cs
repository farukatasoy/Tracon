using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="JobKind.Eval"/> islerini yurutur: bir eval takimindaki her vakayi
/// olculen agent uzerinde calistirir ve sonuclari <see cref="IEvalStore"/>'a yazar.
/// </summary>
/// <remarks>
/// <para>
/// Agent'i cozmek ve calistirmak icin HTTP katmaninin ve <see cref="AgentBatchJobHandler"/>'in
/// kullandigi ayni HTTP-bagimsiz yol izlenir: her vaka <see cref="IAgentCatalog.ResolveAsync"/>
/// ile cozulen agent uzerinde <strong>yeni bir oturumda</strong> (session: null)
/// calistirilir. Boylece agent zaten calistirma kaydi dekoratoru ile sarili
/// oldugundan her vaka kendiliginden kendi <c>runs</c> satirini uretir
/// (docs/18-DEGERLENDIRME.md, bolum 18.3).
/// </para>
/// <para>
/// Genel is kuyrugu (<see cref="IJobStore"/>) yalnizca ilerlemeyi (kac vaka
/// bitti/basarisiz) ve kiralama/yeniden deneme durum makinesini tasir; zengin
/// sonuc (sorgu, cikti, skorlar) <see cref="IEvalStore"/>'da, is ogesinden
/// bagimsiz olarak saklanir. Is ogesinin <see cref="JobItemRecord.Input"/> alani
/// bir <see cref="EvalCase.Id"/> tasir.
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

        var (suiteName, modelOverride, numRepetitions) = ParsePayload(context.Job.Payload);

        var suite = await evalStore.GetSuiteAsync(context.Job.TenantId, suiteName, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{suiteName}' adinda bir eval takimi bulunamadi.");

        var evalRun = await evalStore
            .GetRunByJobIdAsync(context.Job.TenantId, context.Job.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{context.Job.Id}' is kimligi icin bir eval kosu kaydi bulunamadi.");

        try
        {
            await RunSuiteAsync(context, suite, evalRun, modelOverride, numRepetitions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Eval kosusu basarisiz oldu: kosu={EvalRunId} takim={SuiteName}",
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
        CancellationToken cancellationToken)
    {
        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.FirstOrDefault(
            candidate => string.Equals(candidate.Name, suite.AgentName, StringComparison.Ordinal));

        await evalStore
            .MarkRunRunningAsync(evalRun.Id, descriptor?.Version, modelOverride ?? descriptor?.Model?.Model, cancellationToken)
            .ConfigureAwait(false);

        var agent = await catalog.ResolveAsync(suite.AgentName, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"'{suite.AgentName}' adinda bir agent katalogda yok. Eval takiminin agent'i silinmis olabilir.");

        var cases = await evalStore.ListCasesAsync(suite.Id, cancellationToken).ConfigureAwait(false);
        var casesById = cases.ToDictionary(static evalCase => evalCase.Id);

        var checks = checkRegistry.BuildChecks(suite.Checks);

        if (checks.Count == 0)
        {
            throw new AgentPrismException(
                $"'{suite.Name}' takiminin hic denetimi yok; en az bir denetim gereklidir.");
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
                // Yeniden deneme senaryosu: kira suresi dolup is yeniden alindiginda
                // daha once islenmis ogeler tekrar calistirilmaz.
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
                        Error = "Vaka artik mevcut degil; takimdan silinmis olabilir.",
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
                    Error = casePassed ? null : "Bir veya daha fazla denetim basarisiz oldu.",
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

            // 🚨 LocalEvaluator.DetailedItems bos kalir (yalnizca raporlama
            // arka uclariyla doldurulur, olculdu); tek gercek kaynak
            // Items[0].Metrics'tir — batch boyutu 1 oldugu icin bu, iceriginde
            // bu vakanin tum kontrol sonuclarini tasir.
            var metrics = results.Items is [var evaluationResult, ..]
                ? evaluationResult.Metrics
                : throw new AgentPrismException("Degerlendirici hicbir sonuc uretmedi.");

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

    private static (string SuiteName, string? ModelId, int NumRepetitions) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("suiteName", out var suiteNameElement) ||
            suiteNameElement.ValueKind != JsonValueKind.String)
        {
            throw new AgentPrismException("Eval isi yuku bir 'suiteName' (metin) alani tasimalidir.");
        }

        var modelId = payload.TryGetProperty("modelId", out var modelElement) &&
            modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString()
            : null;

        var numRepetitions = payload.TryGetProperty("numRepetitions", out var repsElement) &&
            repsElement.ValueKind == JsonValueKind.Number
            ? Math.Max(1, repsElement.GetInt32())
            : 1;

        return (suiteNameElement.GetString()!, modelId, numRepetitions);
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

        return failing.Length > 0 ? failing : "Denetim basarisiz oldu.";
    }

    /// <summary>
    /// Denetim metriklerini jsonb sutununa yazilacak bir <see cref="JsonElement"/>'e cevirir.
    /// </summary>
    /// <remarks>
    /// <c>Utf8JsonWriter</c> ile elle yazilir; reflection tabanli
    /// <c>JsonSerializer.Serialize</c> kullanilmaz — <c>EvaluationMetric</c> icin
    /// kaynak uretilmis bir baglam olmadigindan IL2026/IL3050 uretirdi. Kutuphane
    /// kodu AOT uyumlu kalmalidir.
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
