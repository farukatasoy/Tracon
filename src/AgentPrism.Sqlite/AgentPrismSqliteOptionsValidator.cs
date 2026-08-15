using Microsoft.Data.Sqlite;
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
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.ConnectionString)} cannot be empty. " +
                $"Baglanti dizesini `UseSqlite(...)` cagrisinda verin veya " +
                $"'{AgentPrismSqliteOptions.SectionName}:{nameof(AgentPrismSqliteOptions.ConnectionString)}' " +
                "ayarini yapilandirmada tanimlayin.");
        }
        else if (IsBareInMemoryConnectionString(options.ConnectionString))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.ConnectionString)} ciplak " +
                "'Data Source=:memory:' kullanamaz: bu kutuphane her islem icin yeni bir baglanti acar ve " +
                "ciplak ':memory:' her baglantiya kendi izole veritabanini verir (Cache=Shared eklense bile). " +
                "Migration'lar bir baglantida uygulanir, sonraki sorgu bos bir veritabanina duser. Paylasimli " +
                "bellek ici veritabani icin URI bicimini kullanin: " +
                "'Data Source=file:<ad>?mode=memory&cache=shared' veya 'Data Source=file::memory:?cache=shared'.");
        }

        if (!SqlIdentifier.IsValidUnquoted(options.TablePrefix))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.TablePrefix)} gecerli bir " +
                "AgentPrism tablo oneki degil. Kucuk harf veya alt cizgi ile baslamali; kucuk harf, " +
                $"rakam ve alt cizgi icermeli; en cok 63 karakter olmalidir. Actual value: '{options.TablePrefix}'.");
        }

        if (options.CommandTimeoutSeconds is < 0 or > 3600)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSqliteOptions)}.{nameof(AgentPrismSqliteOptions.CommandTimeoutSeconds)} " +
                $"0 ile 3600 arasinda olmalidir. Actual value: {options.CommandTimeoutSeconds}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Baglanti dizesinin ciplak (URI olmayan) <c>:memory:</c> veri kaynagi
    /// kullanip kullanmadigini bildirir. Bkz. <see cref="AgentPrismSqliteOptions.ConnectionString"/>.
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
            // Baska bir catman (baglanti acilirken) gecersiz sozdizimini bildirir.
            return false;
        }

        return string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
    }
}
