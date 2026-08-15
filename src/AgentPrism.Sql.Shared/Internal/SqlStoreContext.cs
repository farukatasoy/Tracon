using System.Data.Common;

namespace AgentPrism;

/// <summary>
/// Carries everything the shared store implementations need.
/// </summary>
/// <remarks>
/// <para>
/// The stores do not depend on an <c>Options</c> type: every provider has its own
/// settings class (<c>AgentPrismPostgreSqlOptions</c>, <c>AgentPrismSqlServerOptions</c>)
/// and those are public. Shared code cannot depend on a single one of them, so the
/// values derived from the settings are collected here and the <c>Use*</c> extension
/// of the provider registers this context in DI.
/// </para>
/// <para>
/// Injecting a single object also simplifies the
/// <c>ActivatorUtilities.CreateInstance</c> calls.
/// </para>
/// </remarks>
internal sealed class SqlStoreContext
{
    /// <summary>Gets the data source. The provider driver manages the connection pool.</summary>
    public required DbDataSource DataSource { get; init; }

    /// <summary>Gets the gateway to the provider-specific behaviour.</summary>
    public required SqlDialect Dialect { get; init; }

    /// <summary>Gets the upper time limit of a single SQL command, in seconds. 0 means unlimited.</summary>
    public required int CommandTimeoutSeconds { get; init; }

    /// <summary>Gets a value indicating whether pending migrations are applied automatically at application start.</summary>
    public bool AutoApplyMigrations { get; init; } = true;

    /// <summary>Gets the name of the provider that built this context. It appears in log messages.</summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the additional key/value pairs that replace the <c>{non-schema}</c>
    /// placeholders in the migration text (for example <c>{dimension}</c> — the
    /// vector dimension of phase 51).
    /// </summary>
    /// <remarks>
    /// The schema placeholder (<see cref="SqlQueriesBase.SchemaPlaceholder"/>) is
    /// always replaced separately and unconditionally; this dictionary is for the
    /// additional provider-specific values known AT SETUP TIME. When it is empty,
    /// no additional replacement happens.
    /// </remarks>
    public IReadOnlyDictionary<string, string> MigrationTemplateValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the SQL texts of this provider.</summary>
    public SqlQueriesBase Sql => Dialect.Queries;

    /// <summary>Creates a command with the configured timeout.</summary>
    /// <param name="sql">The command text.</param>
    /// <returns>A command ready to run.</returns>
    /// <remarks>
    /// <see cref="DbDataSource.CreateCommand(string)"/> manages the connection
    /// lifetime itself: it takes a connection from the pool when the command runs
    /// and returns it when the command is disposed.
    /// </remarks>
    public DbCommand CreateCommand(string sql)
    {
        var command = DataSource.CreateCommand(sql);
        command.CommandTimeout = CommandTimeoutSeconds;

        return command;
    }

    /// <summary>Creates a command over an existing connection and transaction.</summary>
    /// <param name="sql">The command text.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="transaction">The transaction to use; <see langword="null"/> when there is none.</param>
    /// <returns>A command ready to run.</returns>
    public DbCommand CreateCommand(string sql, DbConnection connection, DbTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Transaction = transaction;

        return command;
    }
}
