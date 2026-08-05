namespace AgentPrism.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// Tum entegrasyon testlerinin paylastigi tek kullanimlik SQLite veritabani dosyasi.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Container GEREKMEZ.</strong> SQLite tek dosyalik oldugu icin gecici
/// bir dosya yolu yeter; bu, PostgreSQL/SQL Server sozlesme kosularina gore
/// CI suresini kisaltir (docs/24-SQLITE.md, bolum 24.5).
/// </para>
/// <para>
/// Dosya tum derleme icin bir kez olusturulur; testler birbirinden <em>ayri
/// tablo oneki</em> kullanarak yalitilir (bkz. <see cref="SqliteTestContext"/>) —
/// SQL Server/PostgreSQL'in sema yalitimiyla ayni desen.
/// </para>
/// </remarks>
public sealed class SqliteFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"agentprism-tests-{Guid.NewGuid():N}.db");

    /// <summary>Calisan veritabaninin baglanti dizesi.</summary>
    public string ConnectionString => $"Data Source={_databasePath}";

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", ".agentprism-migration-lock" })
        {
            File.Delete(_databasePath + suffix);
        }

        return ValueTask.CompletedTask;
    }
}
