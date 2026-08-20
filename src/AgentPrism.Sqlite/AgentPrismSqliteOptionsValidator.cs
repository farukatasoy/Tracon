using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismSqliteOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// Validation is written manually; <c>ValidateDataAnnotations()</c> relies on reflection.
/// </remarks>
public sealed class AgentPrismSqliteOptionsValidator : IValidateOptions<AgentPrismSqliteOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.ConnectionString)} cannot be empty. " +
                $"Provide the connection string in the `UseSqlite(...)` call, or " +
                $"define the '{AgentPrismSqliteOptions.SectionName}:{nameof(AgentPrismSqliteOptions.ConnectionString)}' " +
                "setting in configuration.");
        }
        else if (IsBareInMemoryConnectionString(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.ConnectionString)} cannot use a bare " +
                "'Data Source=:memory:': this library opens a new connection for every operation, and a bare " +
                "':memory:' gives each connection its own isolated database (even with Cache=Shared added). " +
                "Migrations get applied on one connection, and the next query lands on an empty database. Use the " +
                "URI form for a shared in-memory database: " +
                "'Data Source=file:<name>?mode=memory&cache=shared' or 'Data Source=file::memory:?cache=shared'.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.TablePrefix))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.TablePrefix)} is not a valid " +
                "AgentPrism table prefix. It must start with a lowercase letter or underscore; contain lowercase " +
                $"letters, digits, and underscores; and be at most 63 characters. Actual value: '{options.TablePrefix}'.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.CommandTimeoutSeconds)} " +
                $"must be between 0 and 3600. Actual value: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Reports whether the connection string uses a bare (non-URI) <c>:memory:</c> data
    /// source. See <see cref="AgentPrismSqliteOptions.ConnectionString"/>.
    /// </summary>
    private static bool IsBareInMemoryConnectionString(string connectionString)
    {
        SqliteConnectionStringBuilder builder;

        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // Another layer (when the connection is opened) reports the invalid syntax.
            return false;
        }

        return string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
    }
}
