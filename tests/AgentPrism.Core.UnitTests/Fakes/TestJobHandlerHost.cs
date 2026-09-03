using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// Builds the two things <see cref="JobWorkerBackgroundService"/> now needs to
/// dispatch: a <see cref="JobHandlerRegistry"/> mapping keys to handler types,
/// and the <see cref="IServiceScopeFactory"/> it resolves them from.
/// </summary>
/// <remarks>
/// <para>
/// A real container, not a stub. The worker's contract is "one scope per
/// execution, handler resolved from that scope"; a fake scope factory would
/// let a test pass while the real registration path was broken — the exact
/// failure mode Phase 137 exists to close.
/// </para>
/// <para>
/// Registrations are marked built-in so a fake handler may stand in for a
/// <see cref="JobHandlerKeys"/> key. The reserved-namespace rule is a rule
/// about the PUBLIC <c>AddJobHandler</c> surface, and it is asserted where it
/// lives — not here, where every test would have to work around it.
/// </para>
/// </remarks>
internal sealed class TestJobHandlerHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private TestJobHandlerHost(ServiceProvider provider, JobHandlerRegistry registry)
    {
        _provider = provider;
        Registry = registry;
    }

    /// <summary>The key-to-type map the worker dispatches through.</summary>
    public JobHandlerRegistry Registry { get; }

    /// <summary>The scope factory the worker builds each execution's scope from.</summary>
    public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    /// <summary>
    /// Registers each handler INSTANCE under a key, so a test can keep hold of
    /// the instance it asserts on.
    /// </summary>
    /// <param name="handlers">The key/handler pairs.</param>
    /// <returns>The host.</returns>
    public static TestJobHandlerHost For(params (string Key, IJobHandler Handler)[] handlers)
    {
        var services = new ServiceCollection();
        var registrations = new List<JobHandlerRegistration>();

        foreach (var (key, handler) in handlers)
        {
            services.AddScoped(handler.GetType(), _ => handler);
            registrations.Add(new JobHandlerRegistration(key, handler.GetType(), BuiltIn: true));
        }

        return Build(services, registrations);
    }

    /// <summary>
    /// Registers a handler TYPE under a key, so every execution gets a fresh
    /// instance from its own scope — what a real registration does.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="key">The handler key.</param>
    /// <returns>The host.</returns>
    public static TestJobHandlerHost ForType<THandler>(string key)
        where THandler : class, IJobHandler
    {
        var services = new ServiceCollection();
        services.AddScoped<THandler>();

        return Build(services, [new JobHandlerRegistration(key, typeof(THandler), BuiltIn: true)]);
    }

    /// <summary>Adds a service the handlers under test resolve.</summary>
    /// <param name="configure">Registers extra services before the container is built.</param>
    /// <param name="handlers">The key/handler-type pairs.</param>
    /// <returns>The host.</returns>
    public static TestJobHandlerHost With(
        Action<IServiceCollection> configure,
        params (string Key, Type HandlerType)[] handlers)
    {
        var services = new ServiceCollection();
        configure(services);

        var registrations = new List<JobHandlerRegistration>();

        foreach (var (key, type) in handlers)
        {
            services.AddScoped(type);
            registrations.Add(new JobHandlerRegistration(key, type, BuiltIn: true));
        }

        return Build(services, registrations);
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();

    private static TestJobHandlerHost Build(
        ServiceCollection services,
        IReadOnlyList<JobHandlerRegistration> registrations)
        => new(services.BuildServiceProvider(), JobHandlerRegistry.Create(registrations));
}
