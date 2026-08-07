using System.Data.Common;

namespace AgentPrism;

/// <summary>Tek yurutucu secimi kiralarini SQL'de saklayan depo (Faz 42).</summary>
/// <remarks>
/// <para>
/// Oturum kilidi (<c>pg_try_advisory_lock</c>/<c>sp_getapplock</c>) yerine bir
/// kira tablosu kullanilir: uc saglayicida da (SQLite'in oturum kilidi
/// karsiligi olmadigi icin) ayni davranisi verir ve bagli baglanti havuzuna
/// bagimli degildir. Gerekce: <c>docs/42-TEK-YURUTUCU-SECIMI.md</c> bolum 42.3.
/// </para>
/// <para>Kiraci sutunu yoktur: tek yurutucu secimi kurulum genelinde bir kavramdir.</para>
/// </remarks>
internal sealed class SqlSingletonLeaseStore : ISingletonLeaseStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir SQL tek yurutucu kira deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlSingletonLeaseStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    [TenantAgnostic(
        "Tek yurutucu secimi kurulum genelinde bir kavramdir; kira kiraciya degil kume genelindeki bir ise aittir.")]
    public async ValueTask<bool> TryAcquireAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.AcquireSingletonLease);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "owner_id", ownerId);
        Dialect.AddTimestamp(command, "expires_at", now + duration);
        Dialect.AddTimestamp(command, "now", now);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result is not null;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "TryAcquireAsync ile ayni gerekce: tek yurutucu secimi kurulum genelinde bir kavramdir.")]
    public async ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.RenewSingletonLease);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "owner_id", ownerId);
        Dialect.AddTimestamp(command, "expires_at", now + duration);
        Dialect.AddTimestamp(command, "now", now);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "TryAcquireAsync ile ayni gerekce: tek yurutucu secimi kurulum genelinde bir kavramdir.")]
    public async ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var command = CreateCommand(_sql.ReleaseSingletonLease);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "owner_id", ownerId);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);
}
