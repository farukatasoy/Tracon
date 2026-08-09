using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// AgentPrism agent'larini MCP tool'u olarak yayimlayan HTTP ucunu baglayan
/// uzantilar.
/// </summary>
public static class AgentPrismMcpServerExtensions
{
    /// <summary>Varsayilan yol.</summary>
    public const string DefaultPattern = "/agentprism/mcp";

    /// <summary>
    /// Katalogdaki disa acik agent'lari MCP tool'u olarak yayimlar.
    /// </summary>
    /// <param name="endpoints">Uygulamanin yonlendirme olusturucusu.</param>
    /// <param name="pattern">Yol. Varsayilan <c>/agentprism/mcp</c>.</param>
    /// <returns>Ucun sozlesme olusturucusu.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> bos ise.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>UseMcpServer()</c> veya <c>MapAgentPrism()</c> onceden cagrilmamissa, veya
    /// uzak erisim acikken cagrilmissa.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <c>MapAgentPrism</c>'in AYNI uc katmanli korumasini uygular (loopback +
    /// bearer token + authorization policy) — ayarlar oradan devralinir, ikinci
    /// bir ayar kumesi kurulmaz.
    /// </para>
    /// <para>
    /// 🚨 <c>AllowRemoteAccess</c> aciksa acilista hata verilir. Tek statik token,
    /// disa acilmis bir agent yuzeyi icin yeterli degildir (bolum 50.4).
    /// </para>
    /// <para>
    /// 🚨 Disa acik bir agent onay gerektiren bir tool tasiyorsa uygulama YINE
    /// hata verir — ama bu metottan senkron olarak degil,
    /// <see cref="McpApprovalGuardFilter"/> uzerinden: denetim SQL semasi hazir
    /// olana kadar arka planda bekler (boylece bos bir veritabaninda bu metodun
    /// kendisi "no such table" ile cokmez, K-354'un ayni deseni), ilk isteğe
    /// kadar tamamlanir ve hicbir istek onun onune gecemez.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapAgentPrismMcpServer(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var services = endpoints.ServiceProvider;
        var mcpOptionsMonitor = services.GetService<IOptionsMonitor<AgentPrismMcpServerOptions>>()
            ?? throw new InvalidOperationException(
                "MCP sunucu servisleri kayitli degil. MapAgentPrismMcpServer() cagrisindan " +
                "once builder.UseMcpServer(...) cagirin.");

        var endpointOptions = AgentPrismEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "MCP");

        ExternalSurfaceGuard.EnsureRemoteAccessNotCombined(
            endpointOptions.AllowRemoteAccess, "MCP", services.GetRequiredService<IApiKeyStore>());

        var approvalGuardFilter = new McpApprovalGuardFilter(
            services.GetRequiredService<SchemaReadyGate>(),
            services.GetRequiredService<IAgentCatalog>(),
            services.GetRequiredService<IToolRegistry>(),
            mcpOptionsMonitor,
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<ILogger<McpApprovalGuardFilter>>());

        var group = endpoints.MapGroup(pattern).WithTags("AgentPrism", "MCP");
        group.AddEndpointFilter(approvalGuardFilter);
        group.AddEndpointFilter(new AgentPrismEndpointFilter(endpointOptions));
        group.RequireApiKeyScope(ApiKeyScope.ExternalInvoke);

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        group.MapMcp();

        return group;
    }
}
