using System.Data.Common;

namespace Tracon;

/// <summary>Counts rate-limit windows in the SQL database, so every instance of a deployment shares one counter.</summary>
/// <remarks>
/// <para>
/// One row per partition (<c>partition_key</c>); the row keeps the current window and its count. An
/// increment is one atomic upsert: it adds one, moves the window forward when the caller's window is
/// later, and never moves it back. The row count is bounded by the number of tenants plus the number
/// of inbound triggers, so nothing deletes rows.
/// </para>
/// <para>
/// There is no tenant column: the tenant is part of the partition key when the limit is partitioned
/// by tenant, and the key is built by Tracon (<c>RateLimitPartitionKey</c>).
/// </para>
/// </remarks>
internal sealed class SqlRateLimitCounterStore : IRateLimitCounterStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new SQL rate-limit counter store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlRateLimitCounterStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    [TenantAgnostic(
        "The counter belongs to a rate-limit partition, not to a tenant row: when the limit is partitioned by " +
        "tenant, the tenant is already part of the partition key Tracon builds, and the table has no tenant column.")]
    public async ValueTask<RateLimitWindowCount> IncrementAsync(
        string partitionKey,
        DateTimeOffset windowStart,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(partitionKey);
        cancellationToken.ThrowIfCancellationRequested();

        var counted = await _context.ExecuteUpsertAsync(
            _sql.IncrementRateLimitCounter,
            command =>
            {
                DbHelpers.Add(command, "partition_key", partitionKey);
                Dialect.AddTimestamp(command, "window_start", windowStart);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadCount, token),
            cancellationToken).ConfigureAwait(false);

        // The upsert always returns the row it wrote; no row means the statement itself was wrong.
        return counted ?? throw new InvalidOperationException(
            "The rate-limit counter upsert returned no row.");
    }

    private static RateLimitWindowCount ReadCount(DbDataReader reader) => new()
    {
        WindowStart = DbHelpers.GetTimestamp(reader, 0),
        Count = reader.GetInt64(1),
    };
}
