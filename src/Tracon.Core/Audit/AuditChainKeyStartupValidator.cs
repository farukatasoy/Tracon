using Microsoft.Extensions.DependencyInjection;

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
/// A start-up gate, the first one <c>AddTracon()</c> registers. It resolves nothing but the ring.
/// </para>
/// </remarks>
internal sealed class AuditChainKeyStartupValidator(IServiceProvider services) : ITraconStartupCheck
{
    /// <inheritdoc />
    public StartupCheckKind Kind => StartupCheckKind.Gate;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _ = services.GetRequiredService<AuditChainKeyRing>();

        return Task.CompletedTask;
    }
}
