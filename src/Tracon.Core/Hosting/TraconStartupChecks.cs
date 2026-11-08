using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>What a failing start-up check does to the host.</summary>
internal enum StartupCheckKind
{
    /// <summary>The check refuses to start the host on purpose: its exception stops the start.</summary>
    Gate,

    /// <summary>The check writes a log line. It never stops the host: its exception is logged and the start goes on.</summary>
    Advisory,
}

/// <summary>When a start-up check runs.</summary>
internal enum StartupCheckStage
{
    /// <summary>
    /// In <see cref="IHostedLifecycleService.StartingAsync"/>, which the host calls for every lifecycle service before
    /// it calls any <see cref="IHostedService.StartAsync"/>: for a check that must stop the host before a service
    /// registered ahead of Tracon, or a storage migration, has started.
    /// </summary>
    Starting,

    /// <summary>In <see cref="IHostedService.StartAsync"/>, before any of Tracon's loops has started.</summary>
    Start,
}

/// <summary>A check that runs once while the host starts.</summary>
/// <remarks>
/// A check is not a hosted service of its own: <see cref="TraconStartupChecks"/> runs them all, in the order they were
/// registered. A check that does its work in its constructor (it resolves something whose construction validates)
/// still does: the host resolves the start-up check service, and through it every check, before anything starts.
/// </remarks>
internal interface ITraconStartupCheck
{
    /// <summary>Gets what a failure of this check does to the host.</summary>
    StartupCheckKind Kind { get; }

    /// <summary>Gets when this check runs.</summary>
    StartupCheckStage Stage => StartupCheckStage.Start;

    /// <summary>Runs the check.</summary>
    /// <param name="cancellationToken">The host's start token.</param>
    /// <returns>A task that completes when the check has run.</returns>
    Task StartAsync(CancellationToken cancellationToken);
}

/// <summary>Runs every <see cref="ITraconStartupCheck"/> while the host starts.</summary>
/// <remarks>
/// <para>
/// One hosted service instead of one per check: <c>AddTracon()</c> registers it ahead of its loops, so every check has
/// run, and every gate has passed, before the first loop starts. A gate that fails therefore leaves no loop running.
/// </para>
/// <para>
/// A gate's exception reaches the host unchanged and stops the start, as the gate intends. An advisory's exception is
/// logged and the remaining checks still run: an advisory only writes a log line, and a log line must not cost the
/// application its start.
/// </para>
/// </remarks>
internal sealed class TraconStartupChecks(
    IEnumerable<ITraconStartupCheck> checks,
    ILogger<TraconStartupChecks> logger) : IHostedLifecycleService
{
    private readonly ITraconStartupCheck[] _checks = [.. checks];

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => RunAsync(StartupCheckStage.Starting, cancellationToken);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => RunAsync(StartupCheckStage.Start, cancellationToken);

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync(StartupCheckStage stage, CancellationToken cancellationToken)
    {
        foreach (var check in _checks)
        {
            if (check.Stage != stage)
            {
                continue;
            }

            if (check.Kind == StartupCheckKind.Gate)
            {
                await check.StartAsync(cancellationToken).ConfigureAwait(false);

                continue;
            }

            try
            {
                await check.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
            {
                logger.LogWarning(
                    exception,
                    "The start-up advisory {Check} failed; the host starts anyway.",
                    check.GetType().Name);
            }
        }
    }
}
