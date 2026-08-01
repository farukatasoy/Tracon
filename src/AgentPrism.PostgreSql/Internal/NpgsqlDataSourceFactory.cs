using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgentPrism;

/// <summary>
/// AgentPrism'in kullandigi <see cref="NpgsqlDataSource"/> ornegini kurar.
/// </summary>
/// <remarks>
/// <para>
/// Tek bir veri kaynagi kullanilir ve DI icinde singleton olarak yasar. Npgsql
/// baglanti havuzunu kendi yonetir; ayrica bir havuz katmani eklenmez.
/// </para>
/// <para>
/// <c>EnableDynamicJson()</c> <strong>kullanilmaz</strong>: yansimaya dayanir ve
/// AOT vaadini bozar. <c>jsonb</c> alanlari metin olarak tasinir; serilestirme
/// uygulama tarafinda kaynak ureteci ile yapilir (bkz. <c>AgentPrismJsonContext</c>).
/// </para>
/// </remarks>
internal static class NpgsqlDataSourceFactory
{
    /// <summary>Ayarlardan bir veri kaynagi olusturur.</summary>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="loggerFactory">Npgsql'in kullanacagi gunlukleyici fabrikasi.</param>
    /// <returns>Kullanima hazir veri kaynagi.</returns>
    /// <exception cref="AgentPrismException">Baglanti dizesi tanimli degilse.</exception>
    public static NpgsqlDataSource Create(AgentPrismPostgreSqlOptions options, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new AgentPrismException(
                "PostgreSQL baglanti dizesi tanimli degil. `UsePostgreSql(connectionString)` cagrisinda verin " +
                $"veya '{AgentPrismPostgreSqlOptions.SectionName}:{nameof(AgentPrismPostgreSqlOptions.ConnectionString)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        return builder.Build();
    }
}
