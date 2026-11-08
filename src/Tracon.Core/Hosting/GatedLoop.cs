using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Tracon;

/// <summary>A background loop whose work an option switches on or off.</summary>
/// <remarks>
/// The switch is read once, when the host starts: Tracon does not reload its options while the host runs, and a loop
/// that is off at start stays off until the next start. The loop's own <c>ExecuteAsync</c> reads the same switch through
/// the same rule, so a loop built by hand (a test) behaves as the host would run it.
/// </remarks>
internal interface ISwitchedLoop
{
    /// <summary>Reads whether the loop has work to do in this host.</summary>
    /// <param name="services">The application services.</param>
    /// <returns><see langword="true"/> when the loop runs.</returns>
    static abstract bool IsEnabled(IServiceProvider services);
}

/// <summary>The hosted service in front of a switched loop: it builds the loop only when its switch is on.</summary>
/// <typeparam name="TLoop">The loop.</typeparam>
/// <remarks>
/// <para>
/// A hosted service is built, with everything it depends on, when the host starts, even when the first thing it does is
/// return because its feature is off. Six of Tracon's loops are off by default, and the job worker is off in a host
/// that serves requests and leaves the jobs to a worker process. This service is what the host builds instead; the loop,
/// and its dependencies, are built only when the switch is on.
/// </para>
/// <para>
/// The switch is read and the loop is built in <see cref="StartAsync"/>, on the host's start, so a loop whose
/// dependencies cannot be built still fails the start instead of dying unseen in the background. The host builds every
/// hosted service before it starts any, so a loop that was built by the host failed before the first service started;
/// a loop built here fails after the services registered ahead of Tracon have started. Only the loop's run moves to
/// the background.
/// </para>
/// <para>
/// It is a <see cref="BackgroundService"/> itself and waits on the loop's own task, so the host still sees a loop that
/// fails: the exception ends this service's task, and the host's background-service exception behaviour applies to it as
/// it applied to the loop. A stop is passed on to the loop before this service stops.
/// </para>
/// </remarks>
internal sealed class GatedLoop<TLoop>(IServiceProvider services) : BackgroundService
    where TLoop : BackgroundService, ISwitchedLoop
{
    private TLoop? _loop;

    /// <summary>Gets the loop this gate built, or <see langword="null"/> while it has built none.</summary>
    internal TLoop? Loop => Volatile.Read(ref _loop);

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (TLoop.IsEnabled(services))
        {
            Volatile.Write(ref _loop, services.GetRequiredService<TLoop>());
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _loop) is { } loop)
        {
            await loop.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A host that stops before this runs never starts the loop: its stop has already been passed on.
        if (Volatile.Read(ref _loop) is not { } loop || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        await loop.StartAsync(stoppingToken).ConfigureAwait(false);
        await (loop.ExecuteTask ?? Task.CompletedTask).ConfigureAwait(false);
    }
}

/// <summary>Registers a switched loop behind its <see cref="GatedLoop{TLoop}"/>.</summary>
internal static class GatedLoopRegistration
{
    /// <summary>Registers <typeparamref name="TLoop"/> as a singleton the container builds, and its gate as a hosted service.</summary>
    /// <typeparam name="TLoop">The loop.</typeparam>
    /// <param name="services">The service collection.</param>
    public static void TryAddGatedLoop<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TLoop>(
        this IServiceCollection services)
        where TLoop : BackgroundService, ISwitchedLoop
    {
        services.TryAddSingleton<TLoop>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GatedLoop<TLoop>>());
    }

    /// <summary>Registers <typeparamref name="TLoop"/> through <paramref name="factory"/>, and its gate as a hosted service.</summary>
    /// <typeparam name="TLoop">The loop.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Builds the loop.</param>
    public static void TryAddGatedLoop<TLoop>(this IServiceCollection services, Func<IServiceProvider, TLoop> factory)
        where TLoop : BackgroundService, ISwitchedLoop
    {
        services.TryAddSingleton(factory);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GatedLoop<TLoop>>());
    }
}
