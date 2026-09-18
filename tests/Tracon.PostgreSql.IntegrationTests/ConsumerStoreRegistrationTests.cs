using Microsoft.Extensions.DependencyInjection;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// <c>UsePostgreSql()</c> overrides Tracon's own defaults and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// HATA-S1-019, the 2026-09-16 acceptance round: <c>UsePostgreSql()</c> used
/// <c>Replace</c> for 39 contracts, and <c>Replace</c> cannot tell Tracon's
/// in-memory default from a registration the application made on purpose. A
/// consumer's <c>ITenantStore</c> disappeared with no log, no warning and no
/// startup failure — <c>samples/Tracon.Embedded</c> depends on exactly this
/// order and its documented flow was broken.
/// </para>
/// <para>
/// The rule is one of this repository's four package-quality thresholds —
/// register with <c>TryAdd*</c>, and the consumer's registration always wins —
/// and it is published in <c>guides/write-your-own-store</c>, which tells
/// consumers their registration wins whether it comes before or after
/// <c>AddTracon()</c>.
/// </para>
/// <para>
/// These build a container only; no database is touched.
/// </para>
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
    public void A_store_registered_before_AddTracon_survives_UsePostgreSql()
    {
        using var provider = Build(static services => services.AddSingleton<ITenantStore, ConsumerTenantStore>());

        provider.GetRequiredService<ITenantStore>().ShouldBeOfType<ConsumerTenantStore>();
    }

    [Fact]
    public void Every_other_contract_still_comes_from_the_provider()
    {
        using var provider = Build(static services => services.AddSingleton<ITenantStore, ConsumerTenantStore>());

        // Keeping one registration must not turn the provider off for the rest.
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
        services.AddTracon().UsePostgreSql(static options =>
        {
            options.ConnectionString = "Host=localhost;Port=1;Database=unused;Username=unused";
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }
}
