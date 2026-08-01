using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismPostgreSqlOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. <c>AgentPrism.PostgreSql</c> AOT uyumlu kalmalidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismPostgreSqlOptionsValidator : IValidateOptions<AgentPrismPostgreSqlOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismPostgreSqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismPostgreSqlOptions)}.{nameof(AgentPrismPostgreSqlOptions.ConnectionString)} bos olamaz. " +
                $"Baglanti dizesini `UsePostgreSql(...)` cagrisinda verin veya " +
                $"'{AgentPrismPostgreSqlOptions.SectionName}:{nameof(AgentPrismPostgreSqlOptions.ConnectionString)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.SchemaName))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismPostgreSqlOptions)}.{nameof(AgentPrismPostgreSqlOptions.SchemaName)} gecerli bir " +
                "tirnaksiz PostgreSQL tanimlayicisi degil. Kucuk harf veya alt cizgi ile baslamali; kucuk harf, " +
                $"rakam ve alt cizgi icermeli; en cok 63 karakter olmalidir. Gelen deger: '{options.SchemaName}'.");
        }
        else if (string.Equals(options.SchemaName, "public", StringComparison.Ordinal))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismPostgreSqlOptions)}.{nameof(AgentPrismPostgreSqlOptions.SchemaName)} 'public' olamaz. " +
                "AgentPrism tuketicinin public semasina dokunmaz. Gerekce: docs/KARARLAR.md, karar K-013.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismPostgreSqlOptions)}.{nameof(AgentPrismPostgreSqlOptions.CommandTimeoutSeconds)} " +
                $"0 ile 3600 arasinda olmalidir. Gelen deger: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
