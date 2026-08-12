using Npgsql;
using Testcontainers.PostgreSql;

namespace AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

/// <summary>
/// Tum entegrasyon testlerinin paylastigi tek kullanimlik PostgreSQL container'i.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Hicbir test uzak veya paylasilan bir sunucuya baglanmaz.</strong> Testcontainers
/// her calistirmada yerel bir container ayaga kaldirir ve sonunda yok eder.
/// </para>
/// <para>
/// Container tum derleme icin bir kez baslar; sema sozlesme test SINIFI basina
/// paylasilir (bkz. <see cref="PostgresSchemaFixture"/>, <see cref="PostgresTestContext"/>),
/// testler arasi izolasyon veri sifirlamayla saglanir (K-390). Migrasyon
/// kilidi de artik veritabani genelinde degil semaya kapsanmistir (K-389); bu
/// ikisi birlikte sinif fixture'lerinin migrasyonlarinin PARALEL kosmasini
/// saglar.
/// </para>
/// <para>
/// 🚨 Imaj <c>postgres:18-alpine</c> DEGIL, <c>pgvector/pgvector:pg18</c>'dir
/// (Faz 51). Migration 0024 <c>CREATE EXTENSION IF NOT EXISTS vector;</c> calistirir
/// ve bu HER testte (yalniz vektor testlerinde degil) uygulanir; duz Postgres imaji
/// uzantiyi tasimadigi icin migration seti butun test paketinde patlardi.
/// <c>pgvector/pgvector</c> imaji `postgres` resmi imajinin ustune yalniz bu
/// uzantiyi ekler, baska bir davranis farki yaratmaz.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("agentprism_tests")
        .WithCleanUp(true)
        .Build();

    /// <summary>Calisan container'in baglanti dizesi.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 <c>vector</c> uzantisi container basladiktan hemen sonra, herhangi bir
    /// sema sinif fixture'i migrate olmadan ONCE burada olusturulur. Migration
    /// 0024'un kendi <c>CREATE EXTENSION IF NOT EXISTS vector;</c> ifadesi de
    /// idempotenttir ve tek basina dogrudur, ama <c>pg_extension</c> katalogu
    /// VERITABANI GENELINDE paylasilir — onlarca sema sinif fixture'i (bkz.
    /// <see cref="PostgresSchemaFixture"/>) ilk migration'ini es zamanli
    /// calistirdiginda hepsi ayni satiri olusturmaya calisir ve benzersizlik
    /// ihlaline (SQLSTATE 23505) duser. <c>MigrationRunner.ApplyOneAsync</c> bu
    /// ihlali yeniden dener (K-389) ama gercekci uretim senaryosu (bir
    /// veritabanina uzantiyi BIR KEZ, kurulum aninda kurmak) burada taklit
    /// edilerek yaris tamamen onlenir — testin yapay "29 sema ayni anda ilk
    /// kez migrate olur" sartlarinin urettigi bir yaris, gercek dagitimda
    /// olmazdi.
    /// </remarks>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
