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
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
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
