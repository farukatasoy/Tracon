namespace AgentPrism;

/// <summary>Settings for AgentPrism's SQLite persistence layer.</summary>
/// <remarks>
/// Validation is done manually in <see cref="AgentPrismSqliteOptionsValidator"/>;
/// <c>DataAnnotations</c> is not used. Rationale: <c>docs/KARARLAR.md</c>, decision K-006.
/// </remarks>
public sealed class AgentPrismSqliteOptions
{
    /// <summary>The full path of the configuration section settings are read from.</summary>
    public const string SectionName = "AgentPrism:Sqlite";

    /// <summary>
    /// The SQLite connection string (example: <c>Data Source=agentprism.db</c>).
    /// </summary>
    /// <remarks>
    /// 🚨 A bare <c>Data Source=:memory:</c> is NOT SUPPORTED: this library opens
    /// a NEW connection for every operation via
    /// <see cref="System.Data.Common.DbDataSource.CreateDbConnection"/>, and in
    /// SQLite a bare <c>:memory:</c> gives each connection its own isolated,
    /// anonymous database — even adding <c>Cache=Shared</c> does not change this
    /// (only a URI-form name can be shared). Result: migrations get applied on
    /// one connection, the first query lands on a different (empty) database and
    /// crashes with "no such table". Use the URI form for a shared in-memory
    /// database instead, e.g. <c>Data Source=file:agentprism?mode=memory&amp;cache=shared</c>
    /// or <c>Data Source=file::memory:?cache=shared</c>. This setting is rejected
    /// during validation (<see cref="AgentPrismSqliteOptionsValidator"/>).
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The prefix added to the names of AgentPrism tables. The consumer's own
    /// tables are never touched, under any condition.
    /// </summary>
    /// <remarks>
    /// SQLite has no schema concept; this is the SQLite equivalent of K-013
    /// ("do not touch the consumer's schema"). The prefix goes through the
    /// SAME strict validation as PostgreSQL/SQL Server's schema name: starts
    /// with a lowercase letter or underscore, contains lowercase letters,
    /// digits, and underscores, at most 63 characters.
    /// </remarks>
    public string TablePrefix { get; set; } = "agentprism_";

    /// <summary>
    /// Whether pending migrations should be applied automatically at application startup.
    /// </summary>
    public bool AutoApplyMigrations { get; set; } = true;

    /// <summary>Upper time limit for a single SQL command (seconds). 0 means unlimited.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
