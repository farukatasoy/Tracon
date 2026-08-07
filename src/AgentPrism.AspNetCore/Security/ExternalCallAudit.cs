using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// MCP ve A2A dis cagrilarinin ortak denetim izi yazma yolu (Faz 50).
/// </summary>
/// <remarks>
/// Normal calistirmalar denetim izine yazilmaz (bkz. <see cref="AuditEntry"/>
/// XML belgesi) — bu BILEREK bir istisnadir: dis yuzeyden gelen cagri ayri bir
/// guven sinirindan gelir ve <c>external.call</c> eylemi denetcinin ilk soracagi
/// seydir (Acik Soru 5). K-079'un uc katmani yazma istisnasiyla ayni gerekce.
/// </remarks>
internal static class ExternalCallAudit
{
    /// <summary>Bir dis cagriyi denetim izine yazar.</summary>
    /// <param name="services">Servis saglayici.</param>
    /// <param name="protocol">Cagrinin geldigi protokol: <c>mcp</c> veya <c>a2a</c>.</param>
    /// <param name="agentName">Cagrilan agent.</param>
    /// <param name="runId">Uretilen calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    public static async ValueTask WriteAsync(
        IServiceProvider services,
        string protocol,
        string agentName,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await AuditRecorder.WriteAsync(
            services.GetRequiredService<IAuditLog>(),
            services.GetRequiredService<IAuditActorResolver>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger($"AgentPrism.{protocol}.ExternalCall"),
            services.GetRequiredService<ITenantContext>().TenantId,
            action: "external.call",
            entity: $"agent:{agentName}",
            before: null,
            after: $$"""{"protocol":"{{protocol}}","runId":"{{runId}}"}""",
            cancellationToken).ConfigureAwait(false);
    }
}
