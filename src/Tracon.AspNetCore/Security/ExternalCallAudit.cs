using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The shared audit trail write path for MCP and A2A external calls.
/// </summary>
/// <remarks>
/// Ordinary runs are not written to the audit trail (see the XML documentation of
/// <see cref="AuditEntry"/>) — this is a DELIBERATE exception: a call from an external
/// surface crosses a separate trust boundary, and the <c>external.call</c> action is the
/// first thing an auditor asks about. The same reasoning as the
/// three-layer write exception of.
/// </remarks>
internal static class ExternalCallAudit
{
    /// <summary>Writes an external call to the audit trail.</summary>
    /// <param name="services">The service provider.</param>
    /// <param name="protocol">The protocol the call arrived on: <c>mcp</c> or <c>a2a</c>.</param>
    /// <param name="agentName">The called agent.</param>
    /// <param name="runId">The generated run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
            services.GetRequiredService<ILoggerFactory>().CreateLogger($"Tracon.{protocol}.ExternalCall"),
            services.GetRequiredService<ITenantContext>().TenantId,
            action: "external.call",
            entity: $"agent:{agentName}",
            before: null,
            after: AuditPayload.Write(writer =>
            {
                writer.WriteString("protocol", protocol);
                writer.WriteString("runId", runId);
            }),
            cancellationToken).ConfigureAwait(false);
    }
}
