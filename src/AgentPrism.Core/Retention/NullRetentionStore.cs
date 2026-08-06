namespace AgentPrism;

/// <summary>
/// Veri duzleminin islevsiz varsayilan uygulamasi: bellek ici depolarla
/// calisirken kayitlidir ve her zaman "hicbir sey eslesmedi" doner.
/// </summary>
/// <remarks>
/// Saklama, satirlari kalici bir SQL saglayicisinda saymaya/silmeye dayanir.
/// Bellek ici depolar surec kapaninca zaten kaybolur; bu yuzden gercek bir
/// saklama uygulamasi yalniz <c>UsePostgreSql()</c>/<c>UseSqlServer()</c>/
/// <c>UseSqlite()</c> ile gelir. Bu sinif olmadan <c>RetentionExecutor</c>,
/// <c>IRetentionStore</c> kayitli degilse hata firlatirdi; onun yerine sessizce
/// "silinecek bir sey yok" davranisi tercih edildi.
/// </remarks>
public sealed class NullRetentionStore : IRetentionStore
{
    /// <inheritdoc />
    public ValueTask<long> CountOlderThanAsync(
        string target,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
        => new(0L);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
        => new((IReadOnlyList<ArchiveRow>)[]);

    /// <inheritdoc />
    public ValueTask<int> DeleteBatchAsync(
        string target,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
        => new(0);

    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
        string target,
        long maxRows,
        CancellationToken cancellationToken = default)
        => new((DateTimeOffset?)null);
}
