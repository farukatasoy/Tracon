using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The check that stops the application at start when
/// <c>Tracon:Mcp:OAuthTokenStore = DistributedCache</c> has nothing to work with.
/// </summary>
/// <remarks>
/// A token never goes to a shared cache as plain text and never to a cache that is not there, so the
/// mode fails closed instead of starting and losing the token on the first refresh.
/// </remarks>
public sealed class McpOAuthTokenStoreValidatorTests
{
    [Fact]
    public void The_memory_mode_needs_nothing()
    {
        var result = Validate(static _ => { }, new TraconMcpOptions { OAuthTokenStore = McpOAuthTokenStore.Memory }, out var logs);

        result.Succeeded.ShouldBeTrue();
        logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void The_distributed_mode_without_a_cache_fails_at_start()
    {
        var result = Validate(static services => Protection(services), Distributed(), out _);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("no IDistributedCache is registered", Case.Sensitive);
        result.FailureMessage.ShouldContain("Tracon:Mcp:OAuthTokenStore", Case.Sensitive);
    }

    [Fact]
    public void The_distributed_mode_without_content_protection_fails_at_start()
    {
        // AddTracon() registers the protector that writes plain text; it is not enough.
        var result = Validate(
            static services =>
            {
                services.AddSingleton<IDistributedCache>(new RecordingDistributedCache());
                services.AddSingleton<IContentProtector>(NullContentProtector.Instance);
            },
            Distributed(),
            out _);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("content protection is off", Case.Sensitive);
        result.FailureMessage.ShouldContain("not written to the cache as plain text", Case.Sensitive);
    }

    [Fact]
    public void The_distributed_mode_without_any_protector_fails_at_start()
    {
        var result = Validate(
            static services => services.AddSingleton<IDistributedCache>(new RecordingDistributedCache()),
            Distributed(),
            out _);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("content protection is off", Case.Sensitive);
    }

    [Fact]
    public void A_missing_cache_and_a_missing_protector_are_both_reported()
    {
        var result = Validate(static _ => { }, Distributed(), out _);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("IDistributedCache", Case.Sensitive);
        result.FailureMessage.ShouldContain("content protection", Case.Sensitive);
    }

    [Fact]
    public void A_sliding_lifetime_of_zero_fails_at_start()
    {
        var result = Validate(
            static services => Everything(services),
            Distributed(lifetime: TimeSpan.Zero),
            out _);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconMcpOptions.OAuthTokenSlidingLifetime), Case.Sensitive);
    }

    [Fact]
    public void A_complete_setup_starts_without_a_warning()
    {
        var result = Validate(static services => Everything(services), Distributed(), out var logs);

        result.Succeeded.ShouldBeTrue();
        logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void A_cache_that_lives_in_one_process_starts_with_a_warning()
    {
        // ASP.NET Core MVC and Razor Pages register one without being asked (measured), so it is
        // not an error: a test and a single process use it for good reason.
        var result = Validate(
            static services =>
            {
                Everything(services);
                services.AddSingleton<IDistributedCache>(new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache());
            },
            Distributed(),
            out var logs);

        result.Succeeded.ShouldBeTrue();
        logs.Count(LogLevel.Warning, "memory of this process").ShouldBe(1);
        logs.Count(LogLevel.Warning, "Other instances do not see them").ShouldBe(1);
    }

    [Fact]
    public void A_lease_store_that_lives_in_one_process_starts_with_a_warning()
    {
        var result = Validate(
            static services =>
            {
                Everything(services);
                services.AddSingleton<ISingletonLeaseStore>(new InMemorySingletonLeaseStore());
            },
            Distributed(),
            out var logs);

        result.Succeeded.ShouldBeTrue();
        logs.Count(LogLevel.Warning, "without waiting for each other").ShouldBe(1);
    }

    private static TraconMcpOptions Distributed(TimeSpan? lifetime = null)
        => new()
        {
            OAuthTokenStore = McpOAuthTokenStore.DistributedCache,
            OAuthTokenSlidingLifetime = lifetime ?? TimeSpan.FromDays(30),
        };

    private static IServiceCollection Protection(IServiceCollection services)
        => services.AddSingleton<IContentProtector>(McpOAuthTestKit.Protector());

    private static IServiceCollection Everything(IServiceCollection services)
    {
        services.AddSingleton<IDistributedCache>(new RecordingDistributedCache());
        services.AddSingleton<ISingletonLeaseStore>(new RecordingLeaseStore(new InMemorySingletonLeaseStore()));

        return Protection(services);
    }

    private static ValidateOptionsResult Validate(
        Action<IServiceCollection> register,
        TraconMcpOptions options,
        out RecordingLoggerFactory logs)
    {
        var services = new ServiceCollection();
        register(services);

        using var provider = services.BuildServiceProvider();
        logs = new RecordingLoggerFactory();

        return new McpOAuthTokenStoreValidator(provider, logs).Validate(name: null, options);
    }
}
