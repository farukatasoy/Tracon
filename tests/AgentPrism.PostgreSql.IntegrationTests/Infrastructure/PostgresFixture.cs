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
/// Container tum derleme icin bir kez baslar; testler birbirinden <em>ayri sema</em>
/// kullanarak yalitilir (bkz. <see cref="PostgresTestContext"/>). Boylece hem baslatma
/// maliyeti bir kez odenir hem de sema adinin yapilandirilabilir olmasi her testte
/// dogrulanmis olur.
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
    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
