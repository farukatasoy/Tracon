using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// AgentPrism agent'larini A2A uzerinden yayimlayan HTTP ucunu baglayan
/// uzantilar.
/// </summary>
public static class AgentPrismA2AExtensions
{
    /// <summary>Varsayilan yol oneki.</summary>
    public const string DefaultPattern = "/agentprism/a2a";

    /// <summary>
    /// <c>UseA2A()</c> ile kayitli agent'lari A2A uzerinden yayimlar.
    /// </summary>
    /// <param name="endpoints">Uygulamanin yonlendirme olusturucusu.</param>
    /// <param name="pattern">Yol oneki. Varsayilan <c>/agentprism/a2a</c>.</param>
    /// <returns>Ucun sozlesme olusturucusu.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> bos ise.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>UseA2A()</c> veya <c>MapAgentPrism()</c> onceden cagrilmamissa, uzak
    /// erisim acikken cagrilmissa, veya disa acik bir agent onay gerektiren bir
    /// tool tasiyorsa.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Her disa acik agent kendi alt yoluna baglanir: <c>{pattern}/{agent}</c>.
    /// A2A protokolu tek bir sunucuyu tek bir agent kimligi olarak modeller;
    /// birden fazla agent'in AYNI <c>.well-known/agent-card.json</c> yolunu
    /// paylasmasi mumkun degildir. Her agent'in karti kendi alt yolunda
    /// (<c>{pattern}/{agent}/.well-known/agent-card.json</c>) yayimlanir.
    /// </para>
    /// <para>
    /// <c>MapAgentPrism</c>'in AYNI uc katmanli korumasini uygular — ayarlar
    /// oradan devralinir.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapAgentPrismA2A(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var services = endpoints.ServiceProvider;
        var a2aOptions = services.GetService<AgentPrismA2AOptions>()
            ?? throw new InvalidOperationException(
                "A2A servisleri kayitli degil. MapAgentPrismA2A() cagrisindan once " +
                "builder.UseA2A(...) cagirin.");

        var endpointOptions = AgentPrismEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "A2A");

        ExternalSurfaceGuard.EnsureRemoteAccessNotCombined(
            endpointOptions.AllowRemoteAccess, "A2A", services.GetRequiredService<IApiKeyStore>());

        var catalog = services.GetRequiredService<IAgentCatalog>();
        var toolRegistry = services.GetRequiredService<IToolRegistry>();

        // Senkron cagri gerekcesi: AgentPrismMcpServerExtensions.MapAgentPrismMcpServer'daki
        // ayni not gecerlidir.
        var descriptors = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
        var descriptorsByName = descriptors.ToDictionary(static d => d.Name, StringComparer.Ordinal);

        ExternalSurfaceGuard.EnsureNoApprovalRequiredTools(
            descriptors,
            a2aOptions.ExposedAgents,
            exposeAll: false,
            toolRegistry,
            "A2A");

        var group = endpoints.MapGroup(pattern).WithTags("AgentPrism", "A2A");
        group.AddEndpointFilter(new AgentPrismEndpointFilter(endpointOptions));
        group.RequireApiKeyScope(ApiKeyScope.ExternalInvoke);

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        foreach (var agentName in a2aOptions.ExposedAgents)
        {
            var proxy = services.GetRequiredKeyedService<ExternalAgentProxy>(agentName);
            proxy.AttachServices(services);

            var handler = services.GetRequiredKeyedService<A2AServer>(agentName);
            descriptorsByName.TryGetValue(agentName, out var descriptor);

            var agentPath = $"/{Uri.EscapeDataString(agentName)}";
            var agentGroup = group.MapGroup(agentPath);
            var card = BuildAgentCard(agentName, descriptor, pattern + agentPath);

            agentGroup.MapA2A(handler, "/");
            agentGroup.MapWellKnownAgentCard(card, "");
        }

        return group;
    }

    private static AgentCard BuildAgentCard(string agentName, AgentDescriptor? descriptor, string relativeUrl)
        => new()
        {
            Name = agentName,
            Description = descriptor?.Description ?? descriptor?.DisplayName ?? agentName,
            Version = (descriptor?.Version ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = relativeUrl,
                    ProtocolBinding = ProtocolBindingNames.JsonRpc,
                },
            ],
        };
}
