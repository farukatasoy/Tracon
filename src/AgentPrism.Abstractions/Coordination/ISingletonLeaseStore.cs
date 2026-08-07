namespace AgentPrism;

/// <summary>
/// Kume genelinde adlandirilmis bir isin yalnizca bir ornekte kosmasini saglayan
/// kira deposu.
/// </summary>
/// <remarks>
/// <para>
/// Tek yurutucu secimi (Faz 42) bunu MCP kesfi ve model saglik yoklamasi gibi,
/// birden fazla replikada tekrarlanmasi istenmeyen arka plan islerini
/// koordine etmek icin kullanir. Kira, oturum kilidi degil bir tablodur: uc
/// SQL saglayicisinda da (PostgreSQL, SQL Server, SQLite) ayni davranisi
/// verir; SQLite'in oturum kilidi karsiligi yoktur.
/// </para>
/// <para>
/// <see cref="SingletonExecutionOptions.Enabled"/> <see langword="false"/>
/// (varsayilan) iken bu depo hic cagirilmaz — tek ornekli bir kurulum ek bir
/// veritabani gidisi odemez.
/// </para>
/// </remarks>
public interface ISingletonLeaseStore
{
    /// <summary>
    /// Kirayi almayi dener. Kira baskasindaysa ve suresi dolmamissa
    /// <see langword="false"/> doner.
    /// </summary>
    /// <param name="name">Kiranin adi (korunan isin kimligi).</param>
    /// <param name="ownerId">Bu ornegin kimligi.</param>
    /// <param name="duration">Kiranin gecerlilik suresi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kira alindiysa <see langword="true"/>.</returns>
    ValueTask<bool> TryAcquireAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elde tutulan kirayi uzatir. Kira baskasina gectiyse
    /// <see langword="false"/> doner — cagiran isi BIRAKMALIDIR.
    /// </summary>
    /// <param name="name">Kiranin adi.</param>
    /// <param name="ownerId">Kirayi tuttugunu iddia eden ornegin kimligi.</param>
    /// <param name="duration">Yeni kira suresi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yenileme basarili oldu ise <see langword="true"/>.</returns>
    ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Kirayi birakir. Sahip degilse hicbir sey yapmaz.</summary>
    /// <param name="name">Kiranin adi.</param>
    /// <param name="ownerId">Kirayi tuttugunu iddia eden ornegin kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask ReleaseAsync(
        string name,
        string ownerId,
        CancellationToken cancellationToken = default);
}
