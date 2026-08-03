namespace AgentPrism;

/// <summary>
/// Workflow katalogunu okur ve workflow'lari calistirir.
/// </summary>
/// <remarks>
/// <para>
/// Soyutlama <c>AgentPrism.Abstractions</c> icindedir cunku HTTP katmani
/// workflow calistirir ancak <c>AgentPrism.Workflows</c> paketine bagli
/// <strong>degildir</strong>. Bu, MCP'de <see cref="IMcpToolRefresher"/> ile
/// kurulan desenin aynisidir: workflow motoru istege bagli bir paket olarak
/// kalir ve kullanmayan tuketici 130 tipli bir yurutme motorunu cekmez.
/// </para>
/// <para>
/// Sozlesme Microsoft Agent Framework tipi tasimaz. Olaylar AgentPrism'in kendi
/// <see cref="RunEvent"/> tipiyle akar; MAF olaylarindan cevrim
/// <c>AgentPrism.Workflows</c> icinde yapilir.
/// </para>
/// </remarks>
public interface IWorkflowRunner
{
    /// <summary>Katalogdaki workflow'lari listeler (kodda tanimli + veritabani).</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ozetler.</returns>
    ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Tek bir workflow'un ozetini getirir.</summary>
    /// <param name="name">Workflow adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ozet; yoksa <see langword="null"/>.</returns>
    ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir workflow'u derler ve grafini cikarir.
    /// </summary>
    /// <param name="name">Workflow adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Graf; workflow katalogda yoksa <see langword="null"/>.</returns>
    /// <remarks>
    /// Graf <strong>gercekten derlenir</strong>: hazir desenlerin ekledigi
    /// yardimci dugumler ancak derlemeden sonra gorulur ve calistirma
    /// olaylarindaki executor kimlikleri de oradan gelir. Derleme bir agent
    /// cagrisi yapmaz, yalnizca sarmalayicilari baglar.
    /// </remarks>
    ValueTask<WorkflowGraph?> GetGraphAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir calistirmanin bekleyen insan girdisi isteklerini listeler.
    /// </summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Bekleyen istekler; yoksa bos liste.</returns>
    /// <exception cref="AgentPrismException">
    /// Calistirma yoksa veya baska bir kiraciya aitse.
    /// </exception>
    ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListPendingRequestsAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bekleyen bir istegi yanitlar ve calistirmayi kontrol noktasindan sürdürur.
    /// </summary>
    /// <param name="request">Yanit.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sirali olay akisi.</returns>
    /// <remarks>
    /// Sürdürme <strong>yeni bir calistirma kaydi</strong> acar;
    /// <see cref="ResumeStreamingAsync"/> ile ayni kuraldir. Yanit, kontrol
    /// noktasindan yeniden yayinlanan istekle kimlik uzerinden eslestirilir.
    /// </remarks>
    IAsyncEnumerable<RunEvent> RespondStreamingAsync(
        WorkflowRespondRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir workflow'u calistirir ve olaylarini akitir.
    /// </summary>
    /// <param name="request">Calistirma istegi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sirali olay akisi.</returns>
    /// <remarks>
    /// Ilk olay her zaman <see cref="RunEventType.RunStarted"/>'dir ve
    /// <see cref="RunEvent.RunId"/> alani calistirma kimligini tasir; cagiran
    /// taraf akisin ilk cercevesinden kimligi ogrenir.
    /// </remarks>
    IAsyncEnumerable<RunEvent> RunStreamingAsync(
        WorkflowRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir kontrol noktasindan devam eder ve olaylarini akitir.
    /// </summary>
    /// <param name="request">Sürdürme istegi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sirali olay akisi.</returns>
    /// <remarks>
    /// Sürdürme <strong>yeni bir calistirma kaydi</strong> acar. Ayni satiri
    /// yeniden acmak, olay akisinin append-only olma kuralini (K-014) bozardi
    /// ve "bu calistirma ne zaman bitti" sorusunu cevapsiz birakirdi.
    /// </remarks>
    IAsyncEnumerable<RunEvent> ResumeStreamingAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Bir workflow'u calistirmak icin gereken bilgiler.</summary>
public sealed record WorkflowRunRequest
{
    /// <summary>Calistirilacak workflow'un adi.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Grafa girecek kullanici mesaji.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Yurutme oturumunun kimligi. Bos birakilirsa uretilir.
    /// </summary>
    /// <remarks>
    /// 🚨 Deger <strong>istemciden gelir ve guvenilmez girdidir</strong>. Kontrol
    /// noktalari bu deger altinda gruplandigi icin dogrulanmadan kullanilmasi,
    /// baska bir yurutmenin durumuna erisim demektir.
    /// </remarks>
    public string? SessionId { get; init; }

    /// <summary>
    /// Calistirma kimligi. Verilirse kayit bu kimlikle acilir; akisli bir uc,
    /// ilk cerceveyi yazmadan once kimligi bilmek icin bunu kullanir.
    /// </summary>
    public Guid? RunId { get; init; }
}

/// <summary>Bir workflow'u kontrol noktasindan sürdürmek icin gereken bilgiler.</summary>
public sealed record WorkflowResumeRequest
{
    /// <summary>Devam edilecek calistirmanin kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Devam edilecek kontrol noktasinin kimligi. Bos birakilirsa o calistirmanin
    /// <em>en son</em> kontrol noktasi kullanilir.
    /// </summary>
    public string? CheckpointId { get; init; }

    /// <summary>Yeni calistirmanin kimligi. Verilirse kayit bu kimlikle acilir.</summary>
    public Guid? NewRunId { get; init; }
}
