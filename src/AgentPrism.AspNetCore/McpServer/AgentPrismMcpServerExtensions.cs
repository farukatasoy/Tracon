using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    /// <c>UseMcpServer()</c> veya <c>MapAgentPrism()</c> onceden cagrilmamissa, uzak
    /// erisim acikken cagrilmissa, veya disa acik bir agent onay gerektiren bir
    /// tool tasiyorsa.
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

        ExternalSurfaceGuard.EnsureRemoteAccessNotCombined(endpointOptions.AllowRemoteAccess, "MCP");

        var mcpOptions = mcpOptionsMonitor.CurrentValue;
        var catalog = services.GetRequiredService<IAgentCatalog>();
        var toolRegistry = services.GetRequiredService<IToolRegistry>();

        // Senkron cagri BILEREK yapilir: ASP.NET Core'un minimal barindirma
        // modelinde bir ambient SynchronizationContext yoktur, dolayisiyla
        // acilista bir kere calisan bu denetim icin kilitlenme riski taşımaz.
        var descriptors = catalog.ListAsync().AsTask().GetAwaiter().GetResult();

        ExternalSurfaceGuard.EnsureNoApprovalRequiredTools(
            descriptors,
            mcpOptions.ExposedAgents,
            mcpOptions.ExposeAllAgents,
            toolRegistry,
            "MCP");

        var group = endpoints.MapGroup(pattern).WithTags("AgentPrism", "MCP");
        group.AddEndpointFilter(new AgentPrismEndpointFilter(endpointOptions));

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        group.MapMcp();

        return group;
    }
}
