using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;

namespace Tracon;

/// <summary>
/// Refuses to start a host that loaded Tracon package assemblies from more than
/// one release.
/// </summary>
/// <remarks>
/// <para>
/// It runs in <see cref="IHostedLifecycleService.StartingAsync"/>, which the host
/// calls for every lifecycle service before it calls ANY
/// <see cref="IHostedService.StartAsync"/>. An application's own hosted service
/// may be registered ahead of <c>AddTracon()</c>, and a storage provider's
/// migration service starts in registration order; neither runs on a mixed
/// graph, because the host stops at the first exception a
/// <c>StartingAsync</c> throws.
/// </para>
/// <para>
/// A guard, not an extension point: it is always on and has no opt-out. It
/// reads only assemblies that are already loaded; a package whose registration
/// method was never called is not loaded and does not run either. A process that
/// never starts a host (a bare <c>BuildServiceProvider()</c>) never runs it.
/// </para>
/// </remarks>
internal sealed class PackageFamilyAlignmentService(LoadedPackageFamily family) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // The check is synchronous and in memory; the token is not observed so
        // that a cancelled start never reports a mixed graph it did not see.
        _ = cancellationToken;

        var mismatch = PackageFamilyAlignment.Describe(family.Read());

        return mismatch is null ? Task.CompletedTask : throw new TraconException(mismatch);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The Tracon package assemblies loaded into this process, as the alignment check reads them.</summary>
/// <remarks>
/// Registered with <c>TryAdd</c> so a test can register its own reading ahead of
/// <c>AddTracon()</c> and still exercise the check <c>AddTracon()</c> registered.
/// </remarks>
internal class LoadedPackageFamily
{
    /// <summary>Reads the family assemblies of the current process.</summary>
    /// <returns>One entry per loaded family assembly.</returns>
    internal virtual IReadOnlyList<LoadedFamilyAssembly> Read()
        => PackageFamilyAlignment.ReadLoaded(AppDomain.CurrentDomain.GetAssemblies(), ReadVersion);

    /// <summary>
    /// Reads a version through the helper the sibling abstractions package
    /// exposes to this assembly.
    /// </summary>
    /// <remarks>
    /// Kept out of line on purpose. The helper lives in another package; on a
    /// mixed graph it can be missing or changed, and the runtime then fails while
    /// it COMPILES the method that calls it. Inlined into the caller, that failure
    /// would escape the caller's own <c>catch</c>; as a separate call it surfaces
    /// at the call site, where <see cref="PackageFamilyAlignment.ReadLoaded"/>
    /// turns it into an unknown version.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadVersion(Assembly assembly) => AssemblyVersionText.Read(assembly);
}
