using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismSqlServerOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// Validation is hand-written; <c>ValidateDataAnnotations()</c> relies on reflection.
/// Rationale: <c>docs/KARARLAR.md</c>, decision K-006.
/// </remarks>
public sealed class AgentPrismSqlServerOptionsValidator : IValidateOptions<AgentPrismSqlServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.ConnectionString)} cannot be empty. " +
                $"Give it in the `UseSqlServer(...)` call, or " +
                $"define the '{AgentPrismSqlServerOptions.SectionName}:{nameof(AgentPrismSqlServerOptions.ConnectionString)}' " +
                "setting in `dotnet user-secrets`.");
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
