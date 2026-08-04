namespace AgentPrism;

/// <summary>
/// AgentPrism'in kullandigi <see cref="SqlServerDataSource"/> ornegini kurar.
/// </summary>
/// <remarks>
/// Tek bir veri kaynagi kullanilir ve DI icinde singleton olarak yasar.
/// <c>Microsoft.Data.SqlClient</c> baglanti havuzunu kendi yonetir; ayrica bir
/// havuz katmani eklenmez.
/// </remarks>
internal static class SqlServerDataSourceFactory
{
    /// <summary>Ayarlardan bir veri kaynagi olusturur.</summary>
    /// <param name="options">SQL Server ayarlari.</param>
    /// <returns>Kullanima hazir veri kaynagi.</returns>
    /// <exception cref="AgentPrismException">Baglanti dizesi tanimli degilse.</exception>
    public static SqlServerDataSource Create(AgentPrismSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new AgentPrismException(
                "SQL Server baglanti dizesi tanimli degil. `UseSqlServer(connectionString)` cagrisinda verin " +
                $"veya '{AgentPrismSqlServerOptions.SectionName}:{nameof(AgentPrismSqlServerOptions.ConnectionString)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        return new SqlServerDataSource(options.ConnectionString);
    }
}
