namespace AgentPrism;

/// <summary>
/// Bir uretim calistirmasini bir eval vakasina terfi ettirir (F-53,
/// docs/45-URETIMDEN-EVAL-KUMESI.md).
/// </summary>
/// <remarks>
/// <para>
/// Sorgu metni <c>run_events</c>'teki <see cref="RunEventType.RunStarted"/>
/// olayindan okunur (bkz. <c>RunRecordingAgent.ExtractQuery</c>). Bu, girdi
/// metninin kalicilastigi TEK yerdir: oturum yalniz calistirma BASARIYLA
/// tamamlandiginda kaydedilir (<c>AgentEndpoints.AgentRunStream</c>, hem
/// akisli hem akissiz dalda <c>SaveSessionAsync</c> yalniz basari yolunda
/// cagrilir) — bu yuzden BASARISIZ bir calistirmanin sorgusu oturumdan asla
/// okunamaz. Olculdu (Faz 45): oturum tabanli bir ilk tasarim, basarisiz
/// calistirma terfisinde her zaman 422 dondu.
/// </para>
/// <para>
/// Cok turluluk, bu calistirmanin oturumunda DAHA ONCE baslamis baska bir
/// calistirma olup olmadigina bakilarak belirlenir: varsa, bu calistirmanin
/// <c>query</c>'si TEK BASINA orijinal davranisi yeniden uretmeye yetmez
/// (onceki turlarin baglami eksik kalir) — bkz. 45.4, K-034 (sessiz baglam
/// kaybi yasagi).
/// </para>
/// </remarks>
public sealed class RunToCasePromoter
{
    private readonly IRunStore _runs;
    private readonly IRunScoreStore _scores;
    private readonly IEvalStore _evalStore;

    /// <summary>Yeni bir terfi hizmeti olusturur.</summary>
    public RunToCasePromoter(IRunStore runs, IRunScoreStore scores, IEvalStore evalStore)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(evalStore);

        _runs = runs;
        _scores = scores;
        _evalStore = evalStore;
    }

    /// <summary>Bir calistirmayi verilen takima terfi ettirmeyi dener.</summary>
    /// <param name="tenantId">Isteği yapan kiracı.</param>
    /// <param name="suite">Hedef eval takimi.</param>
    /// <param name="runId">Terfi edilecek calistirma.</param>
    /// <param name="sourceKindOverride">
    /// Terfi sebebini ezer. <see langword="null"/> ise calistirmanin durumundan
    /// ve puanindan kendiliginden turetilir.
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Terfi sonucu.</returns>
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

        // "Yok" ile "baska kiraciya ait" AYNI sonucu doner; ayri bir sonuc
        // varlik sizdirirdi (RunEndpoints.SaveFeedbackAsync ile ayni gerekce).
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

        var query = events
            .FirstOrDefault(static runEvent => runEvent.Type == RunEventType.RunStarted)
            ?.Text;

        if (string.IsNullOrWhiteSpace(query))
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
            Query = query,
            ExpectedOutput = expectedOutput,
            ExpectedTools = expectedTools,
            SourceRunId = runId,
            SourceKind = sourceKind,
        };

        var added = await _evalStore.AddCaseAsync(suite.Id, draft, cancellationToken).ConfigureAwait(false);

        return new RunPromotionOutcome(
            added.Created ? RunPromotionStatus.Created : RunPromotionStatus.AlreadyExists,
            added.Case);
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

        // Yalnizca tamamlanmis calistirmalar icin kendiliginden turetilir;
        // Running/AwaitingInput/Canceled durumlari acik bir sourceKind ister
        // (bu durumlarin "basarili" mi "basarisiz" mi sayilacagi belirsizdir).
        if (run.Status != RunStatus.Completed)
        {
            return null;
        }

        var scores = await _scores.ListAsync(tenantId, run.Id, cancellationToken).ConfigureAwait(false);

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
    /// Model ciktisini olay akisindan cikarir.
    /// </summary>
    /// <remarks>
    /// <see cref="RunEventType.MessageCompleted"/> varsa (akissiz calistirma) o
    /// kullanilir; yoksa (akisli calistirma) <see cref="RunEventType.MessageDelta"/>
    /// parcalari birlestirilir. Ayni desen <c>AgentPrism.Testing.RunAssertions.ShouldHaveOutputContaining</c>'de
    /// kullanilir (K-296'nin komsu tuzagi, Faz 39): ikisi birden TOPLANMAZ,
    /// akissiz yolda metni mukerrer sayardi.
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

/// <summary><see cref="RunToCasePromoter.PromoteAsync"/>'in sonucu.</summary>
public enum RunPromotionStatus
{
    /// <summary>Calistirma yok veya baska bir kiraciya ait.</summary>
    RunNotFound,

    /// <summary>
    /// Calistirma ne <see cref="RunStatus.Failed"/> ne <see cref="RunStatus.Completed"/>;
    /// terfi sebebi acikca verilmelidir.
    /// </summary>
    AmbiguousSource,

    /// <summary>
    /// Calistirmanin sorgusu okunamadi: <c>RunStarted</c> olayi yok veya
    /// bos metin tasiyor (cok eski bir kayit ya da bos girdi).
    /// </summary>
    NoQuery,

    /// <summary>Bu calistirmanin oturumunda ONCEKI bir calistirma var.</summary>
    MultiTurn,

    /// <summary>Vaka bu cagriyla yeni olusturuldu.</summary>
    Created,

    /// <summary>Ayni calistirma daha once terfi edilmis; mevcut vaka dondu.</summary>
    AlreadyExists,
}

/// <summary><see cref="RunToCasePromoter.PromoteAsync"/>'in sonucu ve (varsa) urettigi vaka.</summary>
public sealed record RunPromotionOutcome(RunPromotionStatus Status, EvalCase? Case);
