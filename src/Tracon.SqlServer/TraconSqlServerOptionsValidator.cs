using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconSqlServerOptions"/> settings at application startup.
/// </summary>
/// <remarks>
/// Validation is hand-written; <c>ValidateDataAnnotations()</c> relies on reflection.
/// </remarks>
internal sealed class TraconSqlServerOptionsValidator : IValidateOptions<TraconSqlServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.DataSource is not null && !string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(TraconSqlServerOptions)}.{nameof(TraconSqlServerOptions.DataSource)} and " +
                $"{nameof(TraconSqlServerOptions)}.{nameof(TraconSqlServerOptions.ConnectionString)} cannot " +
                "both be set. Give exactly one: DataSource for a pool Tracon does not own, or ConnectionString " +
                "for Tracon to build and own its own.");
        }
        else if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(TraconSqlServerOptions)}.{nameof(TraconSqlServerOptions.ConnectionString)} cannot be empty. " +
                $"Give it in the `UseSqlServer(...)` call, define the " +
                $"'{TraconSqlServerOptions.SectionName}:{nameof(TraconSqlServerOptions.ConnectionString)}' " +
                $"setting in `dotnet user-secrets`, or set {nameof(TraconSqlServerOptions.DataSource)} instead.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.SchemaName))
        {
            (failures ??= []).Add(
                $"{nameof(TraconSqlServerOptions)}.{nameof(TraconSqlServerOptions.SchemaName)} is not a valid " +
                "Tracon schema name. It must start with a lowercase letter or underscore; contain lowercase " +
                $"letters, digits, and underscores; and be at most 63 characters. Actual value: '{options.SchemaName}'.");
        }
        else if (string.Equals(options.SchemaName, "dbo", StringComparison.Ordinal))
        {
            (failures ??= []).Add(
                $"{nameof(TraconSqlServerOptions)}.{nameof(TraconSqlServerOptions.SchemaName)} cannot be 'dbo'. " +
                "Tracon never touches the consumer's default schema. Rationale: docs/KARARLAR.md, decision K-013.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSqlServerOptions)}.{nameof(TraconSqlServerOptions.CommandTimeoutSeconds)} " +
                $"must be between 0 and 3600. Actual value: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
