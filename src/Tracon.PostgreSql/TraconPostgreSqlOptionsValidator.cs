using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconPostgreSqlOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// Validation is hand-written; <c>ValidateDataAnnotations()</c> relies on
/// reflection and produces <c>IL2026</c>. <c>Tracon.PostgreSql</c> must stay
/// AOT-compatible.
/// </remarks>
internal sealed class TraconPostgreSqlOptionsValidator : IValidateOptions<TraconPostgreSqlOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconPostgreSqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.DataSource is not null && !string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.DataSource)} and " +
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.ConnectionString)} cannot " +
                "both be set. Give exactly one: DataSource for a pool Tracon does not own, or ConnectionString " +
                "for Tracon to build and own its own.");
        }
        else if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.ConnectionString)} cannot be empty. " +
                $"Give it in the `UsePostgreSql(...)` call, define the " +
                $"'{TraconPostgreSqlOptions.SectionName}:{nameof(TraconPostgreSqlOptions.ConnectionString)}' " +
                $"setting in `dotnet user-secrets`, or set {nameof(TraconPostgreSqlOptions.DataSource)} instead.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.SchemaName))
        {
            (failures ??= []).Add(
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.SchemaName)} is not a valid " +
                "unquoted PostgreSQL identifier. It must start with a lowercase letter or underscore; contain lowercase " +
                $"letters, digits, and underscores; and be at most 63 characters. Actual value: '{options.SchemaName}'.");
        }
        else if (string.Equals(options.SchemaName, "public", StringComparison.Ordinal))
        {
            (failures ??= []).Add(
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.SchemaName)} cannot be 'public'. " +
                "Tracon never touches the consumer's public schema. Rationale: docs/KARARLAR.md, decision K-013.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(TraconPostgreSqlOptions)}.{nameof(TraconPostgreSqlOptions.CommandTimeoutSeconds)} " +
                $"must be between 0 and 3600. Actual value: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
