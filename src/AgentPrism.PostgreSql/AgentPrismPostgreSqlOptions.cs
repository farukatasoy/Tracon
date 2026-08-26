using System.Data.Common;

namespace AgentPrism;

/// <summary>Settings for AgentPrism's PostgreSQL persistence layer.</summary>
/// <remarks>
/// Validation is done by hand in <see cref="AgentPrismPostgreSqlOptionsValidator"/>;
/// <c>DataAnnotations</c> is not used.
/// </remarks>
public sealed class AgentPrismPostgreSqlOptions
{
    /// <summary>The full path of the configuration section settings are read from.</summary>
    public const string SectionName = "AgentPrism:PostgreSql";

    /// <summary>
    /// The PostgreSQL connection string.
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
    /// Must be an <c>NpgsqlDataSource</c> (built with <c>NpgsqlDataSourceBuilder</c>)
    /// — for example, the same instance an EF Core <c>DbContext</c> was configured
    /// with via <c>UseNpgsql(dataSource)</c>. When set, <see cref="ConnectionString"/>
    /// is not required, and giving both is a startup error.
    /// </para>
    /// <para>
    /// AgentPrism does <strong>not</strong> take ownership: the instance is never
    /// disposed. The caller keeps ownership and disposes it when the host shuts
    /// down.
    /// </para>
    /// <example>
    /// <code>
    /// var dataSource = new Npgsql.NpgsqlDataSourceBuilder(connectionString).Build();
    ///
    /// // Also give the same instance to your own EF Core DbContext
    /// // (o =&gt; o.UseNpgsql(dataSource)) to share one connection pool.
    /// builder.AddAgentPrism()
    ///        .UsePostgreSql(o =&gt; o.DataSource = dataSource);
    /// </code>
    /// </example>
    /// </remarks>
    public DbDataSource? DataSource { get; set; }

    /// <summary>
    /// The schema AgentPrism's tables are created in. The consumer's <c>public</c>
    /// schema is never touched, under any circumstances.
    /// </summary>
    /// <remarks>
    /// Must follow the rules for an unquoted PostgreSQL identifier: starts with a
    /// lowercase letter or underscore, contains lowercase letters, digits, and
    /// underscores, at most 63 characters.
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

    /// <summary>
    /// Whether the "knowledge" migration set is applied. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The knowledge set needs the <c>pgvector</c> extension; a
    /// consumer on a managed PostgreSQL without permission to install
    /// extensions never sees it unless this is turned on. While it is
    /// <see langword="false"/>, no <see cref="IVectorSearchStore"/> is
    /// registered — an agent definition that requests vector search fails
    /// compilation with a clear error instead of a database error at run
    /// time.
    /// </remarks>
    public bool EnableKnowledge { get; set; }
}
