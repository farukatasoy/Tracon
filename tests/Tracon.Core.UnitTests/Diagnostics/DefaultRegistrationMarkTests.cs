using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The rule <c>AGENTS.md</c> states and <c>guides/write-your-own-store</c>
/// publishes: a registration the consuming application made always wins.
/// </summary>
/// <remarks>
/// <para>
/// A storage provider cannot use <c>TryAdd</c> — <c>AddTracon()</c> already
/// registered the in-memory defaults and the provider exists to override them.
/// Plain <c>Replace</c> is what it used instead, and that overwrote the
/// consumer's registration as readily as Tracon's own (HATA-S1-019, the
/// 2026-09-16 acceptance round). The mark is what separates the two.
/// </para>
/// <para>
/// These tests use a stand-in contract rather than a real store so they state
/// the MECHANISM's rule. Each shipped provider proves the same rule end to end
/// against its own <c>Use*</c> call.
/// </para>
/// </remarks>
public sealed class DefaultRegistrationMarkTests
{
    private interface IThing;

    private sealed class TraconDefault : IThing;

    private sealed class ConsumerOwn : IThing;

    private sealed class ProviderOwn : IThing;

    [Fact]
    public void A_consumer_registration_made_before_the_default_survives_the_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, ConsumerOwn>();

        // AddTracon()'s half: TryAdd is a no-op, so no mark is written.
        services.TryAddTraconDefault(ServiceDescriptor.Singleton<IThing, TraconDefault>());

        // UsePostgreSql()'s half.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IThing>().ShouldBeOfType<ConsumerOwn>();
    }

    /// <summary>
    /// The shipped guide promises BOTH orders work, so the mark has to answer
    /// for a registration that arrives after <c>AddTracon()</c> too. Resolution
    /// takes the last descriptor, and the consumer's is unmarked either way.
    /// </summary>
    [Fact]
    public void A_consumer_registration_made_after_the_default_survives_the_provider()
    {
        var services = new ServiceCollection();
        services.TryAddTraconDefault(ServiceDescriptor.Singleton<IThing, TraconDefault>());
        services.AddSingleton<IThing, ConsumerOwn>();

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IThing>().ShouldBeOfType<ConsumerOwn>();
    }

    [Fact]
    public void Traconts_own_default_is_still_overwritten()
    {
        var services = new ServiceCollection();
        services.TryAddTraconDefault(ServiceDescriptor.Singleton<IThing, TraconDefault>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IThing>().ShouldBeOfType<ProviderOwn>();
    }

    [Fact]
    public void An_unregistered_contract_is_simply_added()
    {
        var services = new ServiceCollection();

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IThing>().ShouldBeOfType<ProviderOwn>();
    }

    /// <summary>
    /// Keeping the registration is correct; keeping it SILENTLY is what made
    /// the old behavior expensive to find, in the opposite direction.
    /// </summary>
    [Fact]
    public void Keeping_a_consumer_registration_is_recorded_for_the_startup_warning()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IThing, ConsumerOwn>();
        services.TryAddTraconDefault(ServiceDescriptor.Singleton<IThing, TraconDefault>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        services.DefaultRegistrations().Preserved.ShouldContain(typeof(IThing));
    }

    [Fact]
    public void Overwriting_Tracons_own_default_records_nothing_to_warn_about()
    {
        var services = new ServiceCollection();
        services.TryAddTraconDefault(ServiceDescriptor.Singleton<IThing, TraconDefault>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        services.DefaultRegistrations().Preserved.ShouldBeEmpty();
    }

    /// <summary>
    /// Two providers in one container is not a supported setup, but the second
    /// one must not read the first one's registration as a consumer's and stop
    /// overriding.
    /// </summary>
    [Fact]
    public void A_second_provider_still_overrides_the_first()
    {
        var services = new ServiceCollection();
        services.TryAddTraconDefault(ServiceDescriptor.Singleton<IThing, TraconDefault>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ProviderOwn>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IThing, ConsumerOwn>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IThing>().ShouldBeOfType<ConsumerOwn>();
        services.DefaultRegistrations().Preserved.ShouldBeEmpty();
    }
}
