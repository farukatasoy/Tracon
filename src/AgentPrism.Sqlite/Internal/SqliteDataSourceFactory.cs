namespace AgentPrism;

/// <summary>
/// AgentPrism'in kullandigi <see cref="SqliteDataSource"/> ornegini kurar.
/// </summary>
/// <remarks>
/// Tek bir veri kaynagi kullanilir ve DI icinde singleton olarak yasar.
/// </remarks>
internal static class SqliteDataSourceFactory
{
    /// <summary>Ayarlardan bir veri kaynagi olusturur.</summary>
    /// <param name="options">SQLite ayarlari.</param>
    /// <returns>Kullanima hazir veri kaynagi.</returns>
    /// <exception cref="AgentPrismException">Baglanti dizesi tanimli degilse.</exception>
    public static SqliteDataSource Create(AgentPrismSqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new AgentPrismException(
                "SQLite baglanti dizesi tanimli degil. `UseSqlite(connectionString)` cagrisinda verin " +
                $"veya '{AgentPrismSqliteOptions.SectionName}:{nameof(AgentPrismSqliteOptions.ConnectionString)}' " +
                "ayarini yapilandirmada tanimlayin.");
        }

        return new SqliteDataSource(options.ConnectionString);
    }
}
