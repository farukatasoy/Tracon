using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Compilation;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The two bounds of the compiled agent cache reach it from configuration, are checked when the host starts,
/// and leave the consumer's own registration in charge.
/// </summary>
public sealed class AgentCacheOptionsTests
{
    private const string MaxEntriesKey = "Tracon:AgentCache:MaxEntries";
    private const string IdleLifetimeKey = "Tracon:AgentCache:IdleLifetime";

    private static Dictionary<string, string?> Config(string key, string value)
        => new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value };

    [Fact]
    public void The_defaults_are_the_ones_the_measurement_chose()
    {
        var options = new TraconOptions().AgentCache;

        options.MaxEntries.ShouldBe(4096);
        options.IdleLifetime.ShouldBe(TimeSpan.FromHours(1));
        TraconAgentCacheOptions.DefaultMaxEntries.ShouldBe(4096);
    }

    [Fact]
    public void Both_bounds_bind_from_configuration()
    {
        var bound = SectionBinding.Bind<TraconOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MaxEntriesKey] = "250",
            [IdleLifetimeKey] = "00:05:00",
        });

        bound.AgentCache.MaxEntries.ShouldBe(250);
        bound.AgentCache.IdleLifetime.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(MaxEntriesKey, "lots")]
    [InlineData(IdleLifetimeKey, "an hour")]
    public void Text_that_is_not_a_value_fails_at_startup_and_names_the_key(string key, string text)
    {
        var failure = SectionBinding.FailsAtStartup(Config(key, text));

        failure.Message.ShouldContain(key);
        failure.Message.ShouldContain($"'{text}'");
    }

    [Theory]
    [InlineData(MaxEntriesKey, "0")]
    [InlineData(MaxEntriesKey, "-1")]
    [InlineData(MaxEntriesKey, "1000001")]
    [InlineData(IdleLifetimeKey, "00:00:00")]
    [InlineData(IdleLifetimeKey, "-00:01:00")]
    [InlineData(IdleLifetimeKey, "30.00:00:01")]
    public void A_zero_negative_or_absurd_value_fails_at_startup(string key, string value)
    {
        var failure = SectionBinding.FailsAtStartup(Config(key, value));

        failure.Message.ShouldContain(nameof(TraconAgentCacheOptions));
        failure.Message.ShouldContain(key.Split(':')[^1]);
    }

    [Theory]
    [InlineData(MaxEntriesKey, "1")]
    [InlineData(MaxEntriesKey, "1000000")]
    [InlineData(IdleLifetimeKey, "00:00:00.001")]
    [InlineData(IdleLifetimeKey, "30.00:00:00")]
    public void The_edges_of_the_range_are_accepted(string key, string value)
        => SectionBinding.StartsCleanly(Config(key, value));

    [Fact]
    public void A_cache_resolved_from_the_container_holds_what_the_configuration_says()
    {
        using var provider = SectionBinding.Build(Config(MaxEntriesKey, "3"), compose: null);

        var cache = provider.GetRequiredService<CompiledAgentCache>();

        for (var tenant = 0; tenant < 50; tenant++)
        {
            cache.GetOrAdd($"tenant-{tenant}", "support", "fp", () => new TaggedAgent("x"));

            cache.Count.ShouldBeLessThanOrEqualTo(3);
        }

        cache.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_cache_follows_the_clock_the_container_registers()
    {
        var clock = new ManualClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTracon(new ConfigurationBuilder()
            .AddInMemoryCollection(Config(IdleLifetimeKey, "00:10:00"))
            .Build()
            .GetSection("Tracon"));

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<CompiledAgentCache>();

        cache.GetOrAdd("tenant-a", "support", "fp", () => new TaggedAgent("a"));
        cache.GetOrAdd("tenant-b", "support", "fp", () => new TaggedAgent("b"));

        clock.Advance(TimeSpan.FromMinutes(11));
        cache.GetOrAdd("tenant-c", "support", "fp", () => new TaggedAgent("c"));

        cache.Count.ShouldBe(1, "a cache on the system clock would still hold all three");
    }

    [Fact]
    public void A_cache_the_consumer_registered_first_is_the_one_the_container_gives_back()
    {
        var own = new CompiledAgentCache();
        var services = new ServiceCollection();
        services.AddSingleton(own);
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<CompiledAgentCache>().ShouldBeSameAs(own);
    }

    [Fact]
    public void A_null_section_is_refused_by_name()
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.Configure<TraconOptions>(static options => options.AgentCache = null!);

        using var provider = services.BuildServiceProvider();

        var failure = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain(nameof(TraconOptions.AgentCache));
    }
}
