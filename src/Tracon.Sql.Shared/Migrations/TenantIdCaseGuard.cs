using System.Data.Common;
using System.Globalization;

namespace Tracon;

/// <summary>
/// Refuses to run against a database that still holds a non-canonical
/// <c>tenant_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tenant identifier is the product's primary isolation key. It is made of the ASCII letters, the
/// digits, <c>_</c>, <c>.</c> and <c>-</c>, and it is matched case-insensitively by normalizing the
/// <em>value</em> (<see cref="AmbientTenantScope.Normalize"/>). A row written before that
/// rule existed, or by a store that bypasses it, carries a spelling the
/// runtime can no longer reach: on PostgreSQL and SQLite the tenant silently
/// loses that data, and on SQL Server's case-insensitive default collation two
/// tenants reach the same row. A row whose tenant holds any other character is refused for the same
/// reason: the store would throw on every read of it, and an accented letter composed and decomposed
/// is one tenant to SQL Server and two to the other engines.
/// </para>
/// <para>
/// <strong>This guard never changes data.</strong> Folding the rows
/// automatically would merge two tenants that a case-sensitive engine has kept
/// apart, and that cannot be undone. Naming the tables and stopping leaves the
/// decision with the operator, who is the only party that knows whether
/// <c>Acme</c> and <c>acme</c> were ever meant to be one tenant.
/// </para>
/// <para>
/// The audit trail is the one table where the advice to rename does not hold. The tenant and the actor of an
/// entry are part of the text its hash is computed from, so a renamed entry no longer matches its hash and the
/// chain verification reports it as broken. The message says so and leaves the choice, which is between
/// keeping the entries as evidence and getting the tenant to start, to the operator.
/// </para>
/// <para>
/// The check is two round trips: one catalog query for the tables that carry
/// the column, then one <c>UNION ALL</c> of <c>EXISTS</c> probes over them. It
/// runs before pending migrations are applied, so a database in this state is
/// not migrated further.
/// </para>
/// <para>
/// It runs on <em>every</em> start, not once. That is deliberate — it keeps
/// protecting a database a consumer-written store wrote to directly — but it
/// is not free: the predicate cannot use an index, so each probe is a scan the
/// engine abandons at the first match. On a clean database that is one
/// aborted scan per tenant table; on a dirty one it is cheaper still, because
/// the run stops.
/// </para>
/// </remarks>
internal static class TenantIdCaseGuard
{
    /// <summary>What to say about the audit trail, where renaming a row breaks the chain.</summary>
    private const string AuditLogAdvice =
        " The audit trail is the exception: the tenant of an entry is part of its hash, so do not rename " +
        "those rows. A renamed entry reads as broken when the chain is verified. Export the entries of the tenant " +
        "first, then either remove all of them (a chain belongs to one tenant, so no gap is left in any other) " +
        "or rename them and accept that the chain of that tenant fails its verification from the first renamed entry.";

    /// <summary>
    /// Throws when any table in the Tracon schema holds a non-canonical
    /// <c>tenant_id</c>.
    /// </summary>
    /// <param name="context">The store context carrying the dialect and command factory.</param>
    /// <param name="connection">An open connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="TraconException">A non-canonical value exists.</exception>
    public static async ValueTask VerifyAsync(
        SqlStoreContext context,
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var tables = await ReadTenantTablesAsync(context, connection, cancellationToken).ConfigureAwait(false);

        if (tables.Count == 0)
        {
            // A fresh database: the schema has not been created yet. Nothing to
            // check, and nothing to warn about.
            return;
        }

        var offenders = await ReadOffendersAsync(context, connection, tables, cancellationToken).ConfigureAwait(false);

        if (offenders.Count == 0)
        {
            return;
        }

        var auditLog = context.Dialect.QualifyTable("audit_log");

        var holdsAuditEntries = offenders.Any(table =>
            string.Equals(context.Dialect.QualifyCatalogTable(table), auditLog, StringComparison.Ordinal));

        throw new TraconException(
            "Tracon cannot start against this database: " +
            $"{offenders.Count} table(s) hold a tenant_id that is not canonical " +
            $"(lower-case ASCII letters, digits, '_', '.' and '-' only): {string.Join(", ", offenders)}. " +
            "The tenant identifier is matched case-insensitively by normalizing the value, and a store refuses " +
            "any character outside that set, so these rows are unreachable or ambiguous. " +
            "Tracon does not fold or rename them for you: on a case-sensitive engine 'Acme' and 'acme' may be two " +
            "real tenants, and merging them cannot be undone. Decide per table whether the rows belong to " +
            "one tenant (rename them to the canonical spelling) or to two (give each its own), then start again." +
            (holdsAuditEntries ? AuditLogAdvice : string.Empty));
    }

    private static async ValueTask<IReadOnlyList<string>> ReadTenantTablesAsync(
        SqlStoreContext context,
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var command = context.CreateCommand(
            context.Sql.ApplySchema(context.Dialect.TenantIdTableCatalogSql),
            connection,
            transaction: null);

        return await DbHelpers.ReadListAsync(
            command,
            static reader => reader.GetString(0),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<string>> ReadOffendersAsync(
        SqlStoreContext context,
        DbConnection connection,
        IReadOnlyList<string> tables,
        CancellationToken cancellationToken)
    {
        // 🚨 The predicate is dialect-supplied because SQL Server's default
        // collation is case-INsensitive: there `tenant_id <> LOWER(tenant_id)`
        // is ALWAYS false and this guard would report a clean database while
        // the offending rows sit in it. See SqlServerDialect.
        var predicate = context.Dialect.NonCanonicalTenantIdPredicate;

        // The retention policy table alone holds a "*" row: the default policy of every tenant. It is not a
        // tenant, and on every other table the same value is a finding.
        var retentionPolicies = context.Dialect.QualifyTable("retention_policies");

        // The names come from the server's own catalog, but they are spliced
        // into SQL text, so they are held to the same unquoted-identifier rule
        // every other table name in this codebase is held to.
        foreach (var table in tables)
        {
            if (!SqlIdentifier.IsValidUnquoted(table))
            {
                throw new TraconException(
                    $"The Tracon schema holds a table whose name cannot be used unquoted: '{table}'. " +
                    "The tenant_id case guard cannot inspect it.");
            }
        }

        // 🚨 EXISTS, not a plain WHERE. A plain WHERE returns one row per
        // OFFENDING ROW, and this guard exists to stop with a readable
        // message: a table holding ten million non-canonical rows would make
        // it allocate ten million strings and die of memory exhaustion
        // instead. EXISTS also lets the engine stop at the first match, which
        // is what keeps the check cheap on a database that is already clean.
        var parts = tables.Select(table =>
        {
            var qualified = context.Dialect.QualifyCatalogTable(table);
            var tablePredicate = string.Equals(qualified, retentionPolicies, StringComparison.Ordinal)
                ? $"{context.Dialect.NotTheDefaultPolicyTenantPredicate} AND ({predicate})"
                : predicate;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT '{table}' AS offending_table WHERE EXISTS (SELECT 1 FROM {qualified} WHERE {tablePredicate})");
        });

        var command = context.CreateCommand(
            string.Join(" UNION ALL ", parts),
            connection,
            transaction: null);

        var rows = await DbHelpers.ReadListAsync(
            command,
            static reader => reader.GetString(0),
            cancellationToken).ConfigureAwait(false);

        // One row per offending TABLE now, but the sort still matters: the
        // message must read the same way on every engine and every run.
        return [.. rows.Order(StringComparer.Ordinal)];
    }
}
