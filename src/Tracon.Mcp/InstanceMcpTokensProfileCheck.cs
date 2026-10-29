using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Answers whether the OAuth tokens of MCP servers are kept where every declared instance reads them.
/// </summary>
/// <remarks>
/// <para>
/// Tracon cannot tell at start whether any MCP server uses OAuth: the servers are rows in the store, written
/// after the host is up. The check therefore asks whenever the MCP client is registered. A deployment whose
/// servers use no OAuth accepts the risk by name.
/// </para>
/// <para>
/// <see cref="McpOAuthTokenStore.DistributedCache"/> satisfies it unless the registered
/// <see cref="IDistributedCache"/> keeps its entries in one process. The mode's own startup validator already
/// refuses a missing cache or content protection that is off, and warns about the in-memory cache; the profile
/// turns that warning into a refusal for a deployment that said it runs more than one instance.
/// </para>
/// </remarks>
internal sealed class InstanceMcpTokensProfileCheck : InstanceProfileCheck
{
    private const string MemoryDistributedCacheTypeName = "Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache";

    /// <inheritdoc />
    public override TraconProductionRisk Risk => TraconProductionRisk.InstanceLocalMcpTokens;

    /// <inheritdoc />
    protected override ProductionProfileResult EvaluateShared(IServiceProvider services, int instances)
    {
        var setting = $"{TraconMcpOptions.SectionName}:{nameof(TraconMcpOptions.OAuthTokenStore)}";
        var options = services.GetRequiredService<IOptions<TraconMcpOptions>>().Value;

        if (options.OAuthTokenStore != McpOAuthTokenStore.DistributedCache)
        {
            return ProductionProfileResult.Permissive(
                setting,
                $"{nameof(McpOAuthTokenStore.Memory)} with {instances} instances declared, so an authorization one " +
                "instance started cannot finish on the other, and a token one instance obtained is not used by the other",
                $"set {setting} to {nameof(McpOAuthTokenStore.DistributedCache)}, register an IDistributedCache that every " +
                "instance shares (for example AddStackExchangeRedisCache) and turn content protection on. " +
                "If no MCP server uses OAuth, accept the risk by name");
        }

        return string.Equals(
            services.GetService<IDistributedCache>()?.GetType().FullName,
            MemoryDistributedCacheTypeName,
            StringComparison.Ordinal)
            ? ProductionProfileResult.Permissive(
                setting,
                $"{nameof(McpOAuthTokenStore.DistributedCache)} on an IDistributedCache that keeps its entries in one process, " +
                $"so with {instances} instances declared the other instance does not see them",
                "register an IDistributedCache that every instance shares, for example AddStackExchangeRedisCache")
            : ProductionProfileResult.Satisfied(setting);
    }
}
