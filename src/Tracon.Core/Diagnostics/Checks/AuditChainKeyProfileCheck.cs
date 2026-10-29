using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Answers whether the audit trail's hash chain is signed with a key.
/// </summary>
/// <remarks>
/// <para>
/// Without a key the chain is tamper-evident in name only: anyone who can write to the audit table can
/// change an entry and recompute every hash after it, and the verify endpoint answers <c>Valid</c>. The
/// key closes that for an actor who can write to the table but cannot read the host's configuration.
/// </para>
/// <para>
/// Not applicable while the bound audit log is Tracon's in-memory one: its chain lives in the process and
/// goes with it, so no one writes to a table behind the host's back. Any other audit log is asked, because
/// the key is what Tracon's own database-backed log signs with and a custom log decides for itself whether
/// it reads the same setting; a deployment whose own log carries its own protection accepts the risk by name.
/// </para>
/// </remarks>
internal sealed class AuditChainKeyProfileCheck : IProductionProfileCheck
{
    /// <inheritdoc />
    public TraconProductionRisk Risk => TraconProductionRisk.UnkeyedAuditChain;

    /// <inheritdoc />
    public ProductionProfileResult Evaluate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var setting = AuditChainKeyRing.ChainKeyIdSetting;

        if (services.GetRequiredService<IAuditLog>() is InMemoryAuditLog)
        {
            return ProductionProfileResult.NotApplicable(
                setting,
                "the audit trail is kept in process memory, so no one writes to a table behind the host's back");
        }

        return services.GetRequiredService<AuditChainKeyRing>().Active is not null
            ? ProductionProfileResult.Satisfied(setting)
            : ProductionProfileResult.Permissive(
                setting,
                "not set, so anyone who can write to the audit table can change an entry and recompute every hash after it",
                $"put a random key of at least {AuditChainKeyRing.MinimumKeyBytes} bytes, base64, under " +
                $"{TraconOptions.SectionName}:{AuditChainKeyRing.KeysSectionName}:<id> in a secret store, and set {setting} to <id>");
    }
}
