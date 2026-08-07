using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="JobKind.OnlineEval"/> islerini yurutur: orneklenmis bir uretim
/// calistirmasini kayitli her <see cref="IRunJudge"/> ile puanlar — Faz 49.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Yargicin basarisizligi puanlanan calistirmayi ETKILEMEZ. Calistirma
/// coktan bitmistir; bu is arka plandadir. Kural gozlemlenebilirlik
/// islevselligi bozmaz ilkesinin dogrudan uygulamasidir.
/// </para>
/// <para>
/// Puan satirinin <see cref="RunScore.Author"/> alani BILEREK <c>judge:{ad}</c>
/// ile dolu yazilir (insan puani gibi <see langword="null"/> DEGIL): boylece
/// <c>run_scores</c>'un <c>(tenant_id, run_id, message_id, author)</c> tekillik
/// kisiti devreye girer (K-239) ve bu isin yeniden denenmesi veya
/// <c>POST /api/runs/{id}/judge</c> ile elle tekrarlanmasi AYNI yargic icin
/// ikinci bir satir DEGIL, mevcut satirin guncellemesini uretir.
/// </para>
/// <para>
/// <see cref="JudgeRunAsync"/>, kuyruk isinin (<see cref="ExecuteAsync"/>) VE
/// elle puanlama ucunun (<c>POST /api/runs/{id}/judge</c>) paylastigi ortak
/// coz; orneklemeyi ATLAR, cagiranin zaten cozdugu bir <see cref="RunRecord"/>
/// bekler.
/// </para>
/// </remarks>
public sealed class OnlineEvalJobHandler(
    IRunStore runStore,
    IRunInputStore runInputStore,
    IRunScoreStore scoreStore,
    IEnumerable<IRunJudge> judges,
    ITenantContext tenantContext,
    AgentPrismMetrics? metrics = null,
    OnlineEvalSummaryService? summaryService = null,
    TimeProvider? timeProvider = null,
    ILogger<OnlineEvalJobHandler>? logger = null) : IJobHandler
{
    /// <inheritdoc />
    public JobKind Kind => JobKind.OnlineEval;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.Count == 0 || !Guid.TryParse(context.Items[0].Input, out var runId))
        {
            throw new AgentPrismException("Cevrimici degerlendirme isi gecerli bir calistirma kimligi tasimiyor.");
        }

        var run = await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        // Calistirma bulunamadi (ornegin saklama supurusu silmis olabilir) veya
        // baska bir kiraciya ait: hata degildir, sessizce biter.
        if (run is null || !string.Equals(run.TenantId, context.Job.TenantId, StringComparison.Ordinal))
        {
            await CompleteItemAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var (_, failures) = await JudgeRunAsync(run, cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            // Basarili yargiclarin puanlari zaten yazildi (JudgeRunAsync icinde,
            // upsert idempotent); yalniz BASARISIZ yargiclarin bir sonraki
            // denemede tekrar calismasi icin is geri adimli beklemeyle yeniden
            // kuyruklanir (K-160).
            var delay = BackoffFor(context.Job.Attempt);

            throw new JobRetryException(
                $"'{runId}' calistirmasi icin {failures.Count} yargic hata verdi: {string.Join("; ", failures)}")
            {
                RetryAfter = delay,
            };
        }

        await CompleteItemAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verilen calistirmayi kayitli her <see cref="IRunJudge"/> ile puanlar.
    /// </summary>
    /// <param name="run">Puanlanacak calistirma. Cagiran kiraci/varlik denetimini yapmis olmalidir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Yazilan puanlar ve hata veren yargiclarin adi/mesaji. <c>run_inputs</c>
    /// kaydi yoksa, kayitli yargic yoksa veya cikti okunamiyorsa ikisi de bostur
    /// (hata degildir).
    /// </returns>
    public async ValueTask<(IReadOnlyList<RunScore> Scores, IReadOnlyList<string> Failures)> JudgeRunAsync(
        RunRecord run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var judgeList = judges as IReadOnlyCollection<IRunJudge> ?? [.. judges];

        if (judgeList.Count == 0)
        {
            return ([], []);
        }

        var input = await runInputStore.GetAsync(tenantContext.TenantId, run.Id, cancellationToken).ConfigureAwait(false);

        // run_inputs kaydi yoksa yargic ciplak calisir; sessiz yarim puanlama
        // yapilmaz (Acik Soru 1).
        if (input is null)
        {
            return ([], []);
        }

        var events = await ReadEventsAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var output = ExtractOutputText(events);

        if (output is null)
        {
            return ([], []);
        }

        var invocations = await runStore.ListToolInvocationsAsync(run.Id, cancellationToken).ConfigureAwait(false);

        var judgeContext = new RunJudgeContext
        {
            RunId = run.Id,
            TenantId = tenantContext.TenantId,
            AgentName = run.AgentName,
            Input = input.Messages,
            Output = output,
            ToolNames = [.. invocations.Select(static invocation => invocation.ToolName).Distinct(StringComparer.Ordinal)],
        };

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var scores = new List<RunScore>();
        var failures = new List<string>();

        foreach (var judge in judgeList)
        {
            await JudgeOneAsync(judge, judgeContext, run, now, scores, failures, cancellationToken).ConfigureAwait(false);
        }

        return (scores, failures);
    }

    private async ValueTask JudgeOneAsync(
        IRunJudge judge,
        RunJudgeContext judgeContext,
        RunRecord run,
        DateTimeOffset now,
        List<RunScore> scores,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        RunJudgment judgment;

        try
        {
            judgment = await judge.JudgeAsync(judgeContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add($"{judge.Name}: {exception.Message}");

            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Yargic '{Judge}' calistirma {RunId} icin hata verdi.",
                    judge.Name,
                    judgeContext.RunId);
            }

            return;
        }

        // 🚨 Karar verilemedi: sessiz bir 0 YAZILMAZ. Sifir bir olcumdur,
        // olcum yoklugu degildir.
        if (judgment.Score is not { } score)
        {
            return;
        }

        var saved = await scoreStore.UpsertAsync(
            new RunScore
            {
                TenantId = tenantContext.TenantId,
                RunId = judgeContext.RunId,
                Kind = RunScoreKind.Numeric,
                Value = score,
                Comment = judgment.Reason,
                Source = $"judge:{judge.Name}",
                Author = $"judge:{judge.Name}",
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        scores.Add(saved);

        metrics?.RecordJudgeScore(judge.Name, run.AgentName, tenantContext.TenantId, score);

        if (summaryService is not null)
        {
            await summaryService.RecordScoreAsync(tenantContext.TenantId, score, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask CompleteItemAsync(JobContext context, CancellationToken cancellationToken)
        => await context.ReportItemAsync(
            new JobItemResult
            {
                JobId = context.Job.Id,
                Seq = context.Items[0].Seq,
                Status = JobItemStatus.Completed,
            },
            cancellationToken).ConfigureAwait(false);

    private static TimeSpan BackoffFor(int attempt)
    {
        var seconds = Math.Min(30 * Math.Pow(2, Math.Max(attempt - 1, 0)), 600);
        return TimeSpan.FromSeconds(seconds);
    }

    private async ValueTask<List<RunEvent>> ReadEventsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runStore.ReadEventsAsync(runId, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>
    /// Model ciktisini olay akisindan cikarir.
    /// </summary>
    /// <remarks>
    /// <see cref="RunToCasePromoter"/>'daki <c>ExtractOutputText</c> ile AYNI
    /// desen: <see cref="RunEventType.MessageCompleted"/> varsa (akissiz
    /// calistirma) o kullanilir; yoksa (akisli calistirma)
    /// <see cref="RunEventType.MessageDelta"/> parcalari birlestirilir.
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
