using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracon;

/// <summary>
/// Refuses to start the host when the audit chain key configuration is wrong.
/// </summary>
/// <remarks>
/// <para>
/// The keys are read lazily, on the first audit write, and a lazy failure would show up as a lost audit
/// entry in the middle of a request. Reading them here moves the failure to the one moment an operator is
/// watching: the start. The failure names the configuration key and never carries a value.
/// </para>
/// <para>
/// A lifecycle service registered ahead of the host's own, like the package family check. It resolves
/// nothing but the ring.
/// </para>
/// </remarks>
internal sealed class AuditChainKeyStartupValidator(IServiceProvider services) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _ = services.GetRequiredService<AuditChainKeyRing>();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
