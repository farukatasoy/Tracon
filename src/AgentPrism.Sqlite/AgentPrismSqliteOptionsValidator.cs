using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismSqliteOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
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
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.ConnectionString)} bos olamaz. " +
                $"Baglanti dizesini `UseSqlite(...)` cagrisinda verin veya " +
                $"'{AgentPrismSqliteOptions.SectionName}:{nameof(AgentPrismSqliteOptions.ConnectionString)}' " +
                "ayarini yapilandirmada tanimlayin.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.TablePrefix))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.TablePrefix)} gecerli bir " +
                "AgentPrism tablo oneki degil. Kucuk harf veya alt cizgi ile baslamali; kucuk harf, " +
                $"rakam ve alt cizgi icermeli; en cok 63 karakter olmalidir. Gelen deger: '{options.TablePrefix}'.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.CommandTimeoutSeconds)} " +
                $"0 ile 3600 arasinda olmalidir. Gelen deger: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
