using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Stops the application at start when <see cref="McpOAuthTokenStore.DistributedCache"/> has nothing
/// to work with, and warns when what it has cannot do what the mode is for.
/// </summary>
/// <remarks>
/// <para>
/// The check reads the container, not the options alone, so it runs where the options are
/// validated at start (<c>ValidateOnStart</c>). A token never goes to the cache as plain text:
/// without content protection the mode does not start (fail-closed), and no setting turns that off.
/// </para>
/// <para>
/// Two cases start with a warning because they are right for a test or a single process:
/// an <c>IDistributedCache</c> that lives in one process (<c>AddDistributedMemoryCache</c>;
/// ASP.NET Core MVC and Razor Pages register it without being asked), and a lease store that lives in one
/// process (no SQL provider), where only a single instance refreshes a token at a time.
/// </para>
/// </remarks>
internal sealed class McpOAuthTokenStoreValidator(IServiceProvider services, ILoggerFactory loggerFactory)
    : IValidateOptions<TraconMcpOptions>
{
    /// <summary>
    /// The full name of the cache <c>AddDistributedMemoryCache</c> registers. Both this validator (a warning) and
    /// <see cref="InstanceMcpTokensProfileCheck"/> (a refusal for a declared multi-instance deployment) recognise it by
    /// this one name, so the two answers cannot drift apart.
    /// </summary>
    internal const string MemoryDistributedCacheTypeName = "Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache";

    private readonly ILogger _logger = loggerFactory.CreateLogger<McpOAuthTokenStoreValidator>();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.OAuthTokenStore != McpOAuthTokenStore.DistributedCache)
        {
            return ValidateOptionsResult.Success;
        }

        const string Setting = $"{TraconMcpOptions.SectionName}:{nameof(TraconMcpOptions.OAuthTokenStore)}";
        List<string>? failures = null;

        if (options.OAuthTokenSlidingLifetime <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.OAuthTokenSlidingLifetime)} must be greater than zero. " +
                $"Actual value: {options.OAuthTokenSlidingLifetime}.");
        }

        var cache = services.GetService<IDistributedCache>();

        if (cache is null)
        {
            (failures ??= []).Add(
                $"{Setting} is {nameof(McpOAuthTokenStore.DistributedCache)}, but no IDistributedCache is registered. " +
                "Register one that every instance shares, for example with AddStackExchangeRedisCache.");
        }

        if (services.GetService<IContentProtector>() is not { IsEnabled: true })
        {
            (failures ??= []).Add(
                $"{Setting} is {nameof(McpOAuthTokenStore.DistributedCache)}, but content protection is off. " +
                "A token is not written to the cache as plain text. Call AddContentProtection(...) or set Tracon:ContentProtection:Enabled.");
        }

        if (failures is not null)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        if (string.Equals(cache!.GetType().FullName, MemoryDistributedCacheTypeName, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "{Setting} is {Mode}, but the registered IDistributedCache keeps its entries in the memory of this process. " +
                "Other instances do not see them. Register a cache that every instance shares, for example AddStackExchangeRedisCache.",
                Setting,
                nameof(McpOAuthTokenStore.DistributedCache));
        }

        if (services.GetService<ISingletonLeaseStore>() is InMemorySingletonLeaseStore)
        {
            _logger.LogWarning(
                "{Setting} is {Mode}, but the lease store keeps its leases in the memory of this process. " +
                "Instances then refresh an OAuth token without waiting for each other. Register a SQL provider so that they do.",
                Setting,
                nameof(McpOAuthTokenStore.DistributedCache));
        }

        return ValidateOptionsResult.Success;
    }
}
