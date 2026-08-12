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
/// Dosya tum derleme icin bir kez olusturulur; tablo oneki sozlesme test
/// SINIFI basina paylasilir (bkz. <see cref="SqliteSchemaFixture"/>,
/// <see cref="SqliteTestContext"/>), testler arasi izolasyon veri
/// sifirlamayla saglanir (K-390) — SQL Server/PostgreSQL ile ayni desen.
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
    /// <remarks>
    /// Migration kilit dosyalari artik tablo onegine kapsanmistir (K-389), yani
    /// dosya adi sinif basina degisir; sabit bir sonek listesi yerine dizin
    /// glob'u ile taranir.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        var directory = Path.GetDirectoryName(_databasePath)!;
        var fileName = Path.GetFileName(_databasePath);

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }
}
