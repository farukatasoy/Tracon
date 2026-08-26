using System.Data.Common;

namespace AgentPrism;

/// <summary>Settings for AgentPrism's SQL Server persistence layer.</summary>
/// <remarks>
/// Validation is done by hand in <see cref="AgentPrismSqlServerOptionsValidator"/>;
/// <c>DataAnnotations</c> is not used.
/// </remarks>
public sealed class AgentPrismSqlServerOptions
{
    /// <summary>The full path of the configuration section settings are read from.</summary>
    public const string SectionName = "AgentPrism:SqlServer";

    /// <summary>
    /// The SQL Server connection string.
    /// </summary>
    /// <remarks>
    /// <strong>This value is a secret and is never written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable, or a secret manager.
    /// Not required when <see cref="DataSource"/> is set; giving both is an error.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The data source AgentPrism uses, instead of building its own from
    /// <see cref="ConnectionString"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Microsoft.Data.SqlClient</c> does not itself offer a <see cref="DbDataSource"/>
    /// implementation (measured against version 7.0.2); give an adapter of your
    /// own if you need to share a connection pool with another consumer of the
    /// same database. When set, <see cref="ConnectionString"/> is not required,
    /// and giving both is a startup error.
    /// </para>
    /// <para>
    /// AgentPrism does <strong>not</strong> take ownership: the instance is never
    /// disposed. The caller keeps ownership and disposes it when the host shuts
    /// down.
    /// </para>
    /// </remarks>
    public DbDataSource? DataSource { get; set; }

    /// <summary>
    /// The schema AgentPrism's tables are created in. The consumer's <c>dbo</c>
    /// schema is never touched, under any circumstances.
    /// </summary>
    /// <remarks>
    /// Even though SQL Server allows a wider set of identifiers, AgentPrism
    /// enforces the <em>same strict rule</em>: starts with a lowercase letter
    /// or underscore, contains lowercase letters, digits, and underscores, at
    /// most 63 characters. This way the same schema name carries over between
    /// PostgreSQL and SQL Server without changes.
    /// </remarks>
    public string SchemaName { get; set; } = "agentprism";

    /// <summary>
    /// Whether pending migrations are applied automatically at application startup.
    /// </summary>
    /// <remarks>
    /// Can be set to <see langword="false"/> in production and <see cref="MigrationRunner"/>
    /// run as a separate deployment step, so a long-running migration does not
    /// block application startup.
    /// </remarks>
    public bool AutoApplyMigrations { get; set; } = true;

    /// <summary>The upper time limit for a single SQL command (seconds). 0 means unlimited.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
