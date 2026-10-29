using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>
    /// Registers the keys that sign the audit trail hash chain, the start check that stops a host with a
    /// bad key, and the production profile check that asks whether the chain has a key.
    /// </summary>
    /// <remarks>
    /// The key VALUES are read from <paramref name="configurationSection"/> directly and are not bound to an
    /// options object: nothing that prints an options object can print a key.
    /// </remarks>
    private static void RegisterAuditChain(IServiceCollection services, IConfiguration? configurationSection)
    {
        services.TryAddSingleton(provider => AuditChainKeyRing.FromConfiguration(
            provider.GetRequiredService<IOptions<TraconOptions>>().Value.Audit.ChainKeyId,
            configurationSection?.GetSection(AuditChainKeyRing.KeysSectionName)));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditChainKeyStartupValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProductionProfileCheck, AuditChainKeyProfileCheck>());
    }
}
