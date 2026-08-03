namespace AgentPrism;

/// <summary>Degerlendirme (eval) takimlarinin, vakalarinin ve kosularinin deposu.</summary>
public interface IEvalStore
{
    /// <summary>Kiracinin tum takimlarini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Adina gore siralanmis takimlar.</returns>
    ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Kiracida verilen adla eslesen takimi getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Takim adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Takim; yoksa <see langword="null"/>.</returns>
    ValueTask<EvalSuite?> GetSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Takimi olusturur veya gunceller.</summary>
    /// <param name="suite">Kaydedilecek takim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kimlik ve zaman damgalari atanmis takim.</returns>
    ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default);

    /// <summary>Takimi ve tum vakalarini/kosularini siler (cascade).</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Takim adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Takim silindiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Bir takimin vakalarini sira numarasina gore listeler.</summary>
    /// <param name="suiteId">Takim kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Vakalar.</returns>
    ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(Guid suiteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir takimin tum vakalarini verilen listeyle degistirir.
    /// </summary>
    /// <param name="suiteId">Takim kimligi.</param>
    /// <param name="cases">Yeni vaka listesi. Sira numaralari liste sirasina gore atanir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kimlik atanmis vakalar.</returns>
    ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(
        Guid suiteId,
        IReadOnlyList<EvalCase> cases,
        CancellationToken cancellationToken = default);

    /// <summary>Yeni bir kosu kaydi olusturur (durum <see cref="EvalRunStatus.Pending"/>).</summary>
    /// <param name="run">Kosu kaydi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kimlik atanmis kosu kaydi.</returns>
    ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir kosuyu <see cref="EvalRunStatus.Running"/> durumuna gecirir ve
    /// olculen agent surumu ile model kimligini kaydeder.
    /// </summary>
    /// <param name="evalRunId">Kosu kimligi.</param>
    /// <param name="agentVersion">Olculen agent tanim surumu.</param>
    /// <param name="modelId">Olculen model kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask MarkRunRunningAsync(
        Guid evalRunId,
        int? agentVersion,
        string? modelId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kosuyu sonlandirir ve ozet sayaclarini yazar.</summary>
    /// <param name="completion">Sonlandirma bilgileri.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>Tek bir kosu kaydini getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="evalRunId">Kosu kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa veya baska bir kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<EvalRun?> GetRunAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir is kaydinin urettigi kosuyu getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa <see langword="null"/>.</returns>
    ValueTask<EvalRun?> GetRunByJobIdAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Kosulari filtreleyerek listeler. En yeni kayit basta doner.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<EvalRun>> QueryRunsAsync(
        EvalRunQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Bir vaka sonucunu kaydeder.</summary>
    /// <param name="result">Vaka sonucu.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default);

    /// <summary>Bir kosunun vaka sonuclarini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="evalRunId">Kosu kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sonuclar.</returns>
    ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default);
}

/// <summary>Kosu listesini filtrelemek icin sorgu.</summary>
public sealed record EvalRunQuery
{
    /// <summary>Yalnizca bu kiracinin kosularini getirir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Yalnizca bu takimin kosularini getirir.</summary>
    public Guid? SuiteId { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Getirilecek ust kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>Bir kosuyu sonlandirmak icin gereken bilgiler.</summary>
public sealed record EvalRunCompletion
{
    /// <summary>Kosu kimligi.</summary>
    public required Guid EvalRunId { get; init; }

    /// <summary>Son durum.</summary>
    public required EvalRunStatus Status { get; init; }

    /// <summary>Bitis zamani (UTC).</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Toplam vaka sayisi.</summary>
    public required int Total { get; init; }

    /// <summary>Gecen vaka sayisi.</summary>
    public required int Passed { get; init; }

    /// <summary>Kalan (basarisiz) vaka sayisi.</summary>
    public required int Failed { get; init; }

    /// <summary>Toplam girdi token sayisi.</summary>
    public long? InputTokens { get; init; }

    /// <summary>Toplam cikti token sayisi.</summary>
    public long? OutputTokens { get; init; }
}
