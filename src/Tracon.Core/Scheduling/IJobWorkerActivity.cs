using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracon;

/// <summary>
/// Says how many jobs the job worker of this process has leased and not finished with.
/// </summary>
/// <remarks>
/// <c>internal</c> on purpose: it is the one thing the drain on shutdown needs to know about the worker, and nothing a
/// consumer should build on. The drain waits for the runs Tracon itself tracks (<see cref="IRunCancellationRegistry"/>); a
/// job of a consumer's handler is in no registry, so without this reading the drain would stop the host under it.
/// </remarks>
internal interface IJobWorkerActivity
{
    /// <summary>Gets how many leased jobs the worker has not finished with; zero when this process runs no worker.</summary>
    int RunningJobs { get; }
}

/// <summary>
/// Reads <see cref="IJobWorkerActivity.RunningJobs"/> from the job worker the host built, if it built one.
/// </summary>
/// <remarks>
/// The worker sits behind its <see cref="GatedLoop{TLoop}"/> and is built only when it is switched on, so a host that
/// serves requests and leaves the jobs to another process has none, and the drain has nothing to wait for. The gate is
/// looked up when the drain asks, never when this is built: it is a hosted service, and so is the drain that reads this.
/// </remarks>
/// <param name="services">The application services.</param>
internal sealed class JobWorkerActivity(IServiceProvider services) : IJobWorkerActivity
{
    private JobWorkerBackgroundService? _worker;

    /// <inheritdoc />
    public int RunningJobs => (_worker ??= Find())?.RunningJobs ?? 0;

    private JobWorkerBackgroundService? Find()
        => services.GetServices<IHostedService>().OfType<GatedLoop<JobWorkerBackgroundService>>().FirstOrDefault()?.Loop;
}
