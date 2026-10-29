using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The intervals and limits of the distributed OAuth state. They are constants of the design, not
/// settings; a test replaces them to run in milliseconds.
/// </summary>
internal sealed record McpOAuthDistributedTiming
{
    /// <summary>Gets the defaults.</summary>
    public static McpOAuthDistributedTiming Default { get; } = new();

    /// <summary>Gets how often the owner of a flow looks for a callback that another instance left.</summary>
    public TimeSpan CallbackPoll { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets how often the instance that received a redirect looks for the owner's result.</summary>
    public TimeSpan OutcomePoll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets how long the instance that received a redirect waits for the owner's result.</summary>
    public TimeSpan OutcomeWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how long a flow marker and a callback stay in the cache; the authorization window.</summary>
    public TimeSpan FlowLifetime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets how long the owner's result stays in the cache.</summary>
    public TimeSpan OutcomeLifetime { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets how long the instance that does not refresh waits for the token the other instance stores.</summary>
    public TimeSpan RefreshWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how often that instance reads the cache.</summary>
    public TimeSpan RefreshPoll { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets how long the lease that serializes a refresh lasts.</summary>
    public TimeSpan RefreshLease { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The services the distributed mode of <see cref="McpOAuthTokenStore"/> works with, or the
/// statement that the mode is off.
/// </summary>
/// <remarks>
/// <para>
/// The mode is read once, when this object is built. <see cref="FromServices"/> builds it from the
/// application's container; the startup check (<see cref="McpOAuthTokenStoreValidator"/>) has already
/// stopped an application whose container lacks a service, so a missing service here is a
/// registration made after the start.
/// </para>
/// </remarks>
internal sealed class McpOAuthDistributedBackend
{
    private McpOAuthDistributedBackend()
    {
        Timing = McpOAuthDistributedTiming.Default;
        Clock = TimeProvider.System;
    }

    /// <summary>Initializes the backend of the distributed mode.</summary>
    /// <param name="cache">The cache every instance shares.</param>
    /// <param name="protector">The content protector.</param>
    /// <param name="leases">The lease store that serializes refreshes.</param>
    /// <param name="slidingLifetime">How long a token stays in the cache without a write.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="timing">The intervals and limits.</param>
    public McpOAuthDistributedBackend(
        IDistributedCache cache,
        IContentProtector protector,
        ISingletonLeaseStore leases,
        TimeSpan slidingLifetime,
        TimeProvider clock,
        McpOAuthDistributedTiming timing)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(timing);

        Cache = cache;
        Protector = protector;
        Leases = leases;
        SlidingLifetime = slidingLifetime;
        Clock = clock;
        Timing = timing;
        Enabled = true;
    }

    /// <summary>Gets the backend of an application that keeps tokens in memory.</summary>
    public static McpOAuthDistributedBackend Disabled { get; } = new();

    /// <summary>Gets a value indicating whether tokens are kept in the distributed cache.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the cache every instance shares.</summary>
    public IDistributedCache Cache { get => field ?? throw NotEnabled(); }

    /// <summary>Gets the content protector.</summary>
    public IContentProtector Protector { get => field ?? throw NotEnabled(); }

    /// <summary>Gets the lease store that serializes refreshes.</summary>
    public ISingletonLeaseStore Leases { get => field ?? throw NotEnabled(); }

    /// <summary>Gets how long a token stays in the cache without a write.</summary>
    public TimeSpan SlidingLifetime { get; }

    /// <summary>Gets the clock.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Gets the intervals and limits.</summary>
    public McpOAuthDistributedTiming Timing { get; }

    /// <summary>Reads the mode and builds the backend from the container.</summary>
    /// <param name="services">The application's service provider.</param>
    /// <returns>The backend; <see cref="Disabled"/> when the mode is <see cref="McpOAuthTokenStore.Memory"/>.</returns>
    public static McpOAuthDistributedBackend FromServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<IOptions<TraconMcpOptions>>().Value;

        if (options.OAuthTokenStore != McpOAuthTokenStore.DistributedCache)
        {
            return Disabled;
        }

        return new McpOAuthDistributedBackend(
            services.GetRequiredService<IDistributedCache>(),
            services.GetRequiredService<IContentProtector>(),
            services.GetRequiredService<ISingletonLeaseStore>(),
            options.OAuthTokenSlidingLifetime,
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetService<McpOAuthDistributedTiming>() ?? McpOAuthDistributedTiming.Default);
    }

    private static InvalidOperationException NotEnabled()
        => new($"The distributed OAuth state is off; set {TraconMcpOptions.SectionName}:{nameof(TraconMcpOptions.OAuthTokenStore)} to {nameof(McpOAuthTokenStore.DistributedCache)}.");
}
