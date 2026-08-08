namespace AgentPrism;

/// <summary>
/// Bu ornekte suren calistirmalarin bellek ici defteri; disaridan gelen bir
/// iptal istegini suren calistirmanin <see cref="System.Threading.CancellationTokenSource"/>'una baglar.
/// </summary>
/// <remarks>
/// <para>
/// Defter <strong>sureç içidir</strong>. Cok ornekli bir dagitimda bir istek
/// yanlis ornege dusebilir; bu durumda iptal ucu <c>409 Conflict</c> doner
/// (bkz. <c>docs/32-CALISTIRMA-IPTALI.md</c>, "Kapsam siniri"). Dagitik bir
/// defter isteyen bir kurulum bu arayuzu kendi uygulamasiyla degistirebilir
/// (<c>TryAddSingleton</c>, K4).
/// </para>
/// <para>
/// Kok calistirmanin (<c>RunId == RootRunId</c>) iptali, ayni <c>RootRunId</c>
/// altindaki tum kayitlari da iptal eder. Bir alt calistirmanin tek basina
/// iptali ne kardes dallari ne de koku etkiler.
/// </para>
/// </remarks>
public interface IRunCancellationRegistry
{
    /// <summary>Suren bir calistirmayi deftere yazar.</summary>
    /// <param name="runId">Calistirmanin kimligi.</param>
    /// <param name="rootRunId">Agacin kokundeki calistirmanin kimligi. Kokte <paramref name="runId"/> ile aynidir.</param>
    /// <param name="tenantId">Calistirmanin kiracisi.</param>
    /// <param name="source">Calistirmanin iptalini tetikleyecek kaynak. Sahipligi cagirandadir; bu metot onu bertaraf etmez.</param>
    /// <returns>Kaydi deftereden kaldirmak icin bertaraf edilecek nesne.</returns>
    IDisposable Register(Guid runId, Guid rootRunId, string? tenantId, CancellationTokenSource source);

    /// <summary>Bir calistirmanin iptalini ister. Agac koku ise alt calistirmalar da iptal edilir.</summary>
    /// <param name="runId">Iptali istenen calistirmanin kimligi.</param>
    /// <param name="tenantId">Isteyen kiraci. Kayittaki kiraciyla eslesmezse istek yok sayilir.</param>
    /// <returns>Iptal istegi bir kayda ulastiysa <see langword="true"/>.</returns>
    bool TryCancel(Guid runId, string? tenantId);

    /// <summary>Bu ornekte suren calistirma sayisi. Teshis ve test icindir.</summary>
    int ActiveCount { get; }

    /// <summary>
    /// Bu ornekte SU AN suren calistirmalarin kimlikleri.
    /// </summary>
    /// <remarks>
    /// Faz 54: <c>RunHeartbeatWriter</c> bu listeyi kullanarak yalnizca BU
    /// surecin gercekten yurutuğu calistirmalarin heartbeat'ini yazar. Bir
    /// baska ornegin Running satirini yanlislikla "canli" isaretlemek
    /// uzlastirmanin butun amacini gecersiz kilardi -- bkz.
    /// <c>docs/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md</c>.
    /// </remarks>
    IReadOnlyCollection<Guid> ActiveRunIds { get; }
}
