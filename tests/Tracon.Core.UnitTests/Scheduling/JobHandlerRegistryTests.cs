using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// Phase 137 — the registry decides which handler runs a job, and refuses the
/// three registrations that cannot be resolved at run time.
/// </summary>
/// <remarks>
/// These assertions run against the REGISTRY, not against the container's
/// ability to hand back a type. An earlier version of the sample's test asked
/// only <c>GetRequiredService&lt;THandler&gt;()</c>, which stays green while
/// duplicate detection is broken — the registry is never built, so the throw
/// never happens.
/// </remarks>
public sealed class JobHandlerRegistryTests
{
    [Fact]
    public void Registering_the_same_type_under_the_same_key_twice_is_a_no_op()
    {
        var services = new ServiceCollection();
        services.AddJobHandler<AHandler>("contoso.a");
        services.AddJobHandler<AHandler>("contoso.a");

        using var provider = services.BuildServiceProvider();
        var registry = JobHandlerRegistry.Create(provider.GetServices<JobHandlerRegistration>());

        registry.Keys.ShouldBe(["contoso.a"]);
        registry.TryGetHandlerType("contoso.a", out var type).ShouldBeTrue();
        type.ShouldBe(typeof(AHandler));
    }

    [Fact]
    public void AddTracon_called_twice_still_produces_one_handler_per_built_in_key()
    {
        // 🚨 AddTracon promises TryAdd semantics: calling it twice must not
        // change the container. A handler registration is written with a plain
        // Add (a setup-time extension must not inspect the collection, K-251),
        // so the idempotence has to hold in the registry instead.
        var services = new ServiceCollection();
        services.AddTracon();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();
        var registry = JobHandlerRegistry.Create(provider.GetServices<JobHandlerRegistration>());

        registry.Keys.ShouldBe([.. JobHandlerKeys.BuiltIn.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void Two_different_types_under_one_key_throw_and_name_both()
    {
        var services = new ServiceCollection();
        services.AddJobHandler<AHandler>("contoso.a");
        services.AddJobHandler<BHandler>("contoso.a");

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(
            () => JobHandlerRegistry.Create(provider.GetServices<JobHandlerRegistration>()));

        exception.Message.ShouldContain("contoso.a");
        exception.Message.ShouldContain(nameof(AHandler));
        exception.Message.ShouldContain(nameof(BHandler));
    }

    [Fact]
    public void A_reserved_key_is_refused_by_the_public_registration_itself()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentException>(
            () => services.AddJobHandler<AHandler>(JobHandlerKeys.Retention));

        exception.Message.ShouldContain(JobHandlerKeys.ReservedPrefix);
    }

    [Theory]
    [InlineData("Contoso.A")]
    [InlineData("-leading-dash")]
    [InlineData(".leading-dot")]
    [InlineData("has space")]
    public void A_malformed_key_is_refused(string key)
        => Should.Throw<ArgumentException>(() => new ServiceCollection().AddJobHandler<AHandler>(key));

    [Fact]
    public void A_128_character_key_is_accepted_and_129_is_not()
    {
        JobHandlerKeys.IsValidKey(new string('a', 128)).ShouldBeTrue();
        JobHandlerKeys.IsValidKey(new string('a', 129)).ShouldBeFalse();
    }

    [Fact]
    public async Task Registration_order_does_not_change_which_handler_a_key_resolves_to()
    {
        var forward = await ResolveAsync(services =>
        {
            services.AddJobHandler<AHandler>("contoso.a");
            services.AddJobHandler<BHandler>("contoso.b");
        });

        var reversed = await ResolveAsync(services =>
        {
            services.AddJobHandler<BHandler>("contoso.b");
            services.AddJobHandler<AHandler>("contoso.a");
        });

        forward.ShouldBe(reversed);
        forward["contoso.a"].ShouldBe(typeof(AHandler));
        forward["contoso.b"].ShouldBe(typeof(BHandler));
    }

    private static ValueTask<Dictionary<string, Type>> ResolveAsync(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);

        using var provider = services.BuildServiceProvider();
        var registry = JobHandlerRegistry.Create(provider.GetServices<JobHandlerRegistration>());

        var resolved = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var key in registry.Keys)
        {
            registry.TryGetHandlerType(key, out var type).ShouldBeTrue();
            resolved[key] = type!;
        }

        return ValueTask.FromResult(resolved);
    }

    private sealed class AHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class BHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }
}
