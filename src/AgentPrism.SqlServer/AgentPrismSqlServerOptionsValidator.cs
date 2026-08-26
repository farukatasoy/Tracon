using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismSqlServerOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// Validation is hand-written; <c>ValidateDataAnnotations()</c> relies on reflection.
/// </remarks>
internal sealed class AgentPrismSqlServerOptionsValidator : IValidateOptions<AgentPrismSqlServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.DataSource is not null && !string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.DataSource)} and " +
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.ConnectionString)} cannot " +
                "both be set. Give exactly one: DataSource for a pool AgentPrism does not own, or ConnectionString " +
                "for AgentPrism to build and own its own.");
        }
        else if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.ConnectionString)} cannot be empty. " +
                $"Give it in the `UseSqlServer(...)` call, define the " +
                $"'{AgentPrismSqlServerOptions.SectionName}:{nameof(AgentPrismSqlServerOptions.ConnectionString)}' " +
                $"setting in `dotnet user-secrets`, or set {nameof(AgentPrismSqlServerOptions.DataSource)} instead.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.SchemaName))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.SchemaName)} is not a valid " +
                "AgentPrism schema name. It must start with a lowercase letter or underscore; contain lowercase " +
                $"letters, digits, and underscores; and be at most 63 characters. Actual value: '{options.SchemaName}'.");
        }
        else if (string.Equals(options.SchemaName, "dbo", StringComparison.Ordinal))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.SchemaName)} cannot be 'dbo'. " +
                "AgentPrism never touches the consumer's default schema. Rationale: docs/KARARLAR.md, decision K-013.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.CommandTimeoutSeconds)} " +
                $"must be between 0 and 3600. Actual value: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
