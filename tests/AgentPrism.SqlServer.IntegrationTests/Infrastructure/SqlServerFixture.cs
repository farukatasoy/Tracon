using Testcontainers.MsSql;

namespace AgentPrism.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// Tum entegrasyon testlerinin paylastigi tek kullanimlik SQL Server container'i.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Hicbir test uzak veya paylasilan bir sunucuya baglanmaz.</strong>
/// Testcontainers her calistirmada yerel bir container ayaga kaldirir ve sonunda
/// yok eder.
/// </para>
/// <para>
/// Container tum derleme icin bir kez baslar; sema sozlesme test SINIFI basina
/// paylasilir (bkz. <see cref="SqlServerSchemaFixture"/>, <see cref="SqlServerTestContext"/>),
/// testler arasi izolasyon veri sifirlamayla saglanir (K-390). Migrasyon
/// kilidi de artik veritabani genelinde degil semaya kapsanmistir (K-389); bu
/// ikisi birlikte 29 sinif fixture'inin migrasyonlarinin PARALEL kosmasini
/// saglar.
/// </para>
/// <para>
/// 🚨 SQL Server container'i ~2 GB bellek ister; PostgreSQL imajindan belirgin
/// olarak agirdir. CI is tanimlarinda kaynak siniri kontrol edilmelidir.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithCleanUp(true)
            .Build();

    /// <summary>Calisan container'in baglanti dizesi.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
