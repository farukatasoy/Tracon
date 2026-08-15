using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Extensions that connect the HTTP endpoint that publishes AgentPrism agents
/// over A2A.
/// </summary>
public static class AgentPrismA2AExtensions
{
    /// <summary>Default path prefix.</summary>
    public const string DefaultPattern = "/agentprism/a2a";

    /// <summary>
    /// Publishes the agents registered via <c>UseA2A()</c> over A2A.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="pattern">Path prefix. Default is <c>/agentprism/a2a</c>.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>UseA2A()</c> or <c>MapAgentPrism()</c> has not been called first, or
    /// this is called while remote access is enabled.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Each exposed agent is mapped to its own sub-path: <c>{pattern}/{agent}</c>.
    /// The A2A protocol models a single server as a single agent identity;
    /// multiple agents cannot share the SAME <c>.well-known/agent-card.json</c>
    /// path. Each agent's card is published at its own sub-path
    /// (<c>{pattern}/{agent}/.well-known/agent-card.json</c>).
    /// </para>
    /// <para>
    /// Applies the SAME three-layer protection as <c>MapAgentPrism</c> —
    /// settings are inherited from there.
    /// </para>
    /// <para>
    /// 🚨 If an exposed agent carries a tool that requires approval the
    /// application STILL fails — but not synchronously from this method,
    /// rather through <see cref="A2AApprovalGuardFilter"/>: the check waits in
    /// the background until the SQL schema is ready (so this method itself
    /// does not crash with "no such table" against an empty database, the same
    /// pattern as K-354), completes by the first request, and no request can
    /// get ahead of it.
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
                "A2A services are not registered. Call builder.UseA2A(...) before " +
                "calling MapAgentPrismA2A().");

        var endpointOptions = AgentPrismEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "A2A");

        ExternalSurfaceGuard.EnsureRemoteAccessNotCombined(
            endpointOptions.AllowRemoteAccess, "A2A", services.GetRequiredService<IApiKeyStore>());

        var catalog = services.GetRequiredService<IAgentCatalog>();

        // The approval guard no longer lives here: `A2AApprovalGuardHostedService`
        // (see `UseA2A`) performs the same check after the schema is ready.
        // Endpoint mapping (this method) runs BEFORE `app.Run()`, so migrations
        // may not have finished yet — a catalog query could blow up with
        // "no such table" against an empty database. The only remaining use
        // here is to DECORATE the agent card (Description/Version); since
        // there is no SECURITY check, falling back to agentName on failure is
        // SAFE. A broad catch is DELIBERATE because the type/message of the
        // "no such table" error differs across providers.
        Dictionary<string, AgentDescriptor> descriptorsByName;

        try
        {
            var descriptors = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
            descriptorsByName = descriptors.ToDictionary(static d => d.Name, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            descriptorsByName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);
        }

        var approvalGuardFilter = new A2AApprovalGuardFilter(
            services.GetRequiredService<SchemaReadyGate>(),
            catalog,
            services.GetRequiredService<IToolRegistry>(),
            a2aOptions,
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<ILogger<A2AApprovalGuardFilter>>());

        var group = endpoints.MapGroup(pattern).WithTags("AgentPrism", "A2A");
        group.AddEndpointFilter(approvalGuardFilter);
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

            agentGroup.MapA2A(handler, "/").WithName($"AgentPrismA2A_{agentName}");
            agentGroup.MapWellKnownAgentCard(card, "").WithName($"AgentPrismA2AAgentCard_{agentName}");
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
