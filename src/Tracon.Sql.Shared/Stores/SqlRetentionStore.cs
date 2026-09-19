using System.Data.Common;
using System.Globalization;

namespace Tracon;

/// <summary>
/// The data-plane implementation of <see cref="IRetentionStore"/>: counting, batch
/// deletion, and reading for archival.
/// </summary>
/// <remarks>
/// <para>
/// SQL text per target is NOT hand-copied: <see cref="RetentionTargetRegistry"/>
/// defines the table/predicate, <see cref="SqlDialect"/> applies the provider-specific
/// 3-part template (count/read/delete).
/// </para>
/// <para>
/// Archive rows are converted to JSON by <see cref="SqlJsonRowWriter"/>, without
/// knowing the column schema up front and without reflection — this store compiles into
/// <c>Tracon.PostgreSql</c> and that package must stay AOT-compatible.
/// </para>
/// </remarks>
internal sealed class SqlRetentionStore : IRetentionStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlDialect _dialect;

    /// <summary>Creates a new retention data-plane store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlRetentionStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _dialect = context.Dialect;
    }

    /// <inheritdoc />
    public async ValueTask<long> CountOlderThanAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionCountSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);

        var result = await DbHelpers.ExecuteScalarAsync(command, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            null => 0L,
            long count => count,
            int count => count,
            _ => Convert.ToInt64(result, CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionArchiveSelectSql(
            definition.Table,
            Where(definition, tenantId),
            definition.OrderColumn);

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        return await DbHelpers
            .ReadListAsync(command, static reader => new ArchiveRow { Json = SqlJsonRowWriter.RowToJson(reader) }, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteBatchAsync(
        string target,
        string? tenantId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionDeleteBatchSql(definition.Table, Where(definition, tenantId));

        var command = _context.CreateCommand(sql);
        _dialect.AddTimestamp(command, "cutoff", cutoff);
        AddTenant(command, tenantId);
        DbHelpers.Add(command, "batchSize", Math.Max(1, batchSize));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
        string target,
        string? tenantId,
        long maxRows,
        CancellationToken cancellationToken = default)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(
            definition.Table,
            definition.RowLimitOrderExpression,
            tenantId is null ? null : definition.TenantPredicate);

        var command = _context.CreateCommand(sql);
        _dialect.AddInt64(command, "n", maxRows);
        AddTenant(command, tenantId);

        var rows = await DbHelpers
            .ReadListAsync(command, static reader => DbHelpers.GetNullableTimestamp(reader, 0), cancellationToken)
            .ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>Adds the tenant filter to the target's predicate.</summary>
    /// <param name="definition">The target definition.</param>
    /// <param name="tenantId">The tenant; no filter is added when <see langword="null"/>.</param>
    /// <returns>The <c>WHERE</c> predicate to run.</returns>
    private static string Where(RetentionTargetDefinition definition, string? tenantId)
        => tenantId is null
            ? definition.WherePredicate
            : SqlDialect.Combine(definition.WherePredicate, definition.TenantPredicate);

    /// <summary>Binds the tenant parameter only when needed.</summary>
    /// <param name="command">The command.</param>
    /// <param name="tenantId">The tenant; nothing is bound when <see langword="null"/>.</param>
    private static void AddTenant(DbCommand command, string? tenantId)
    {
        if (tenantId is not null)
        {
            DbHelpers.AddTenant(command, tenantId);
        }
    }
}
