using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Pins what a configuration reload does to the options of Tracon.Core: nothing. The options are read when the host
/// starts, and a change made while it runs needs a restart.
/// </summary>
/// <remarks>
/// <para>
/// The packages hold the same rule. This project does not reference them; <c>PackageConfigurationReloadTests</c> lists every
/// options type with a change token source across the packages, and <c>WorkflowOptionsReloadTests</c> pins what a reload
/// does to the workflow readers.
/// </para>
/// <para>
/// Tracon binds its sections by hand and registers no <see cref="IOptionsChangeTokenSource{TOptions}"/>, so a
/// configuration provider that reloads (a file with <c>reloadOnChange</c>, a vault, a configuration server) never reaches
/// an <see cref="IOptionsMonitor{TOptions}"/>. Many readers look the value up on every call, which looks like live
/// reconfiguration, and it is not: the monitor holds the value it built at start.
/// </para>
/// <para>
/// A host that adds a change token source of its own gets a half of it. The monitor sees the new value, and a reader that
/// built something from the old one (the model concurrency limiter holds a semaphore the size of the first limit) keeps
/// it. A new value that fails validation makes every read of the monitor throw until the next good reload. These tests
/// are the reason the documentation says "restart": supporting reload means deciding each of those cases, option by
/// option, and the day a source is registered this class fails and says so.
/// </para>
/// </remarks>
public sealed class ConfigurationReloadTests
{
    private const string LimitKey = "Tracon:ModelConcurrency:MaxConcurrentCallsPerProvider";

    [Fact]
    public void Tracon_registers_no_change_token_source_for_any_of_its_options()
    {
        var (services, _) = Compose(limit: 2, hostSource: false);

        var sources = services
            .Where(static descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IOptionsChangeTokenSource<>))
            .Select(static descriptor => descriptor.ServiceType.GetGenericArguments()[0].Name)
            .ToArray();

        sources.ShouldBeEmpty(
            "Tracon reads its options once, when the host starts, and says so in its documentation. A change token source makes " +
            "the options monitor recompute on a reload, which reaches only the readers that look the value up on every call. " +
            "Decide each reader (the model concurrency limiter keeps a semaphore of the first size), then change the documentation.");
    }

    [Fact]
    public void A_reload_of_the_configuration_does_not_reach_the_options_monitor()
    {
        var (services, configuration) = Compose(limit: 2, hostSource: false);

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<TraconOptions>>();

        // A host that validates on start has built the options by now; IOptions<T> builds them on its first read.
        var options = provider.GetRequiredService<IOptions<TraconOptions>>();

        monitor.CurrentValue.ModelConcurrency.MaxConcurrentCallsPerProvider.ShouldBe(2);
        options.Value.ModelConcurrency.MaxConcurrentCallsPerProvider.ShouldBe(2);

        configuration[LimitKey] = "5";
        configuration.Reload();

        configuration[LimitKey].ShouldBe("5", "the configuration itself did change");
        monitor.CurrentValue.ModelConcurrency.MaxConcurrentCallsPerProvider.ShouldBe(2);
        options.Value.ModelConcurrency.MaxConcurrentCallsPerProvider.ShouldBe(2);
    }

    [Fact]
    public async Task A_limit_the_host_reloads_does_not_resize_a_semaphore_that_already_exists()
    {
        var (services, configuration) = Compose(limit: 2, hostSource: true);

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<TraconOptions>>();
        var limiter = new ProviderConcurrencyLimiter(monitor);

        var first = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);
        var second = await limiter.AcquireAsync("openai", TestContext.Current.CancellationToken);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();

        configuration[LimitKey] = "5";
        configuration.Reload();

        // The host's own source reached the monitor ...
        monitor.CurrentValue.ModelConcurrency.MaxConcurrentCallsPerProvider.ShouldBe(5);

        // ... and the limiter still holds two slots: a third caller waits.
        var third = limiter.AcquireAsync("openai", TestContext.Current.CancellationToken).AsTask();
        var finished = await Task.WhenAny(third, Task.Delay(150, TestContext.Current.CancellationToken)); // delay: negative

        finished.ShouldNotBe(third, "a SemaphoreSlim cannot be resized, so the limit is the one the first call saw");

        first!.Dispose();
        (await third)!.Dispose();
        second!.Dispose();
    }

    [Fact]
    public void A_reloaded_value_that_fails_validation_makes_every_read_of_the_monitor_throw_once_the_host_added_a_source()
    {
        var (services, configuration) = Compose(limit: 2, hostSource: true);

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<TraconOptions>>();

        monitor.CurrentValue.ModelConcurrency.MaxConcurrentCallsPerProvider.ShouldBe(2);

        configuration["Tracon:CircuitBreaker:FailureThreshold"] = "0";

        // The reload itself throws: the monitor recomputes on the token and the new value fails validation.
        Should.Throw<AggregateException>(configuration.Reload);

        // And so does every later read, until a good value replaces it. The limiter reads on every model call.
        Should.Throw<OptionsValidationException>(() => _ = monitor.CurrentValue);
        Should.Throw<OptionsValidationException>(() => _ = monitor.CurrentValue);
    }

    private static (ServiceCollection Services, IConfigurationRoot Configuration) Compose(int limit, bool hostSource)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [LimitKey] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var section = configuration.GetSection("Tracon");
        var builder = services.AddTracon(section);
        builder.UseLiveVoice(section.GetSection("Voice:Live"));
        builder.UseVoiceConversation(section.GetSection("Voice:Conversation"));

        if (hostSource)
        {
            services.AddSingleton<IOptionsChangeTokenSource<TraconOptions>>(new ReloadTokenSource(configuration));
        }

        return (services, configuration);
    }

    /// <summary>What a host writes to make an options monitor follow its configuration.</summary>
    private sealed class ReloadTokenSource(IConfiguration configuration) : IOptionsChangeTokenSource<TraconOptions>
    {
        public string? Name => Options.DefaultName;

        public IChangeToken GetChangeToken() => configuration.GetReloadToken();
    }
}
