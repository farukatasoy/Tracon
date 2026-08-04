using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismSqlServerOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
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
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.ConnectionString)} bos olamaz. " +
                $"Baglanti dizesini `UseSqlServer(...)` cagrisinda verin veya " +
                $"'{AgentPrismSqlServerOptions.SectionName}:{nameof(AgentPrismSqlServerOptions.ConnectionString)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.SchemaName))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.SchemaName)} gecerli bir " +
                "AgentPrism sema adi degil. Kucuk harf veya alt cizgi ile baslamali; kucuk harf, " +
                $"rakam ve alt cizgi icermeli; en cok 63 karakter olmalidir. Gelen deger: '{options.SchemaName}'.");
        }
        else if (string.Equals(options.SchemaName, "dbo", StringComparison.Ordinal))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.SchemaName)} 'dbo' olamaz. " +
                "AgentPrism tuketicinin varsayilan semasina dokunmaz. Gerekce: docs/KARARLAR.md, karar K-013.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqlServerOptions)}.{nameof(AgentPrismSqlServerOptions.CommandTimeoutSeconds)} " +
                $"0 ile 3600 arasinda olmalidir. Gelen deger: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
