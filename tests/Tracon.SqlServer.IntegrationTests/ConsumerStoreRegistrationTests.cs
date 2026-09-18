using Microsoft.Extensions.DependencyInjection;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// <c>UseSqlServer()</c> overrides Tracon's own defaults and nothing else.
/// </summary>
/// <remarks>
/// The class scan for HATA-S1-019: the defect was found through
/// <c>UsePostgreSql</c>, but all three providers shared the same 39
/// <c>Replace</c> calls, so all three overwrote a consumer's store. See the
/// PostgreSQL copy of this file for the full rationale.
/// </remarks>
public sealed class ConsumerStoreRegistrationTests
{
    /// <summary>A store the application owns. Only its TYPE matters here.</summary>
    private sealed class ConsumerTenantStore : ITenantStore
    {
        public ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TenantDescriptor> SaveAsync(TenantDescriptor tenant, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public void A_store_registered_before_AddTracon_survives_UseSqlServer()
    {
        using var provider = Build(static services => services.AddSingleton<ITenantStore, ConsumerTenantStore>());

        provider.GetRequiredService<ITenantStore>().ShouldBeOfType<ConsumerTenantStore>();
    }

    [Fact]
    public void Every_other_contract_still_comes_from_the_provider()
    {
        using var provider = Build(static services => services.AddSingleton<ITenantStore, ConsumerTenantStore>());

        provider.GetRequiredService<IRunStore>().ShouldBeOfType<SqlRunStore>();
    }

    [Fact]
    public void A_contract_the_consumer_left_alone_still_comes_from_the_provider()
    {
        using var provider = Build(static _ => { });

        provider.GetRequiredService<ITenantStore>()
            .ShouldBeOfType<AuditingTenantStore>().AuditedInner
            .ShouldBeOfType<SqlTenantStore>();
    }

    private static ServiceProvider Build(Action<IServiceCollection> registerConsumerStores)
    {
        var services = new ServiceCollection();
        registerConsumerStores(services);
        services.AddTracon().UseSqlServer(static options =>
        {
            options.ConnectionString = "Server=localhost,1;Database=unused;User Id=unused;Password=unused;TrustServerCertificate=true";
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }
}
