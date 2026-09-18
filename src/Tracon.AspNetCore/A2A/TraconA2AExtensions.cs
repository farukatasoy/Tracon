using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Extensions that connect the HTTP endpoint that publishes Tracon agents
/// over A2A.
/// </summary>
public static class TraconA2AExtensions
{
    /// <summary>Default path prefix.</summary>
    public const string DefaultPattern = "/tracon/a2a";

    /// <summary>
    /// Publishes the agents registered via <c>UseA2A()</c> over A2A.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="pattern">Path prefix. Default is <c>/tracon/a2a</c>.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>UseA2A()</c> or <c>MapTracon()</c> has not been called first, or
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
    /// Applies the SAME three-layer protection as <c>MapTracon</c> —
    /// settings are inherited from there.
    /// </para>
    /// <para>
    /// If an exposed agent carries a tool that requires approval the
    /// application STILL fails — but not synchronously from this method,
    /// rather through <see cref="A2AApprovalGuardFilter"/>: the check waits in
    /// the background until the SQL schema is ready (so this method itself
    /// does not crash with "no such table" against an empty database, the same
    /// pattern), completes by the first request, and no request can
    /// get ahead of it.
    /// </para>
    /// <example>
    /// <code>
    /// app.MapTracon();
    /// app.MapTraconA2A();
    /// </code>
    /// </example>
    /// </remarks>
    public static IEndpointConventionBuilder MapTraconA2A(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var services = endpoints.ServiceProvider;
        var a2aOptions = services.GetService<TraconA2AOptions>()
            ?? throw new InvalidOperationException(
                "A2A services are not registered. Call builder.UseA2A(...) before " +
                "calling MapTraconA2A().");

        var endpointOptions = TraconEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "A2A");

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
        //
        // 🚨 The catch is not enough on its own, and the gate below is why.
        // Swallowing the exception here happens one layer too late:
        // `CompositeAgentCatalog.ListAsync` has ALREADY logged it at Error
        // level with a full stack trace by the time it returns. So a correct,
        // expected, handled path printed "Agent source 'database' failed
        // during list" with a provider stack trace on the 7th line of EVERY
        // first startup against an empty schema — before the migration runner
        // has even created the schema — and that is a consumer's first
        // impression of this library. Asking the gate first means the question
        // is never put to the store while the answer can only be an error.
        // The gate opens on its own when no SQL provider is registered, so an
        // in-memory installation still gets its decorated card.
        Dictionary<string, AgentDescriptor> descriptorsByName;

        if (!services.GetRequiredService<SchemaReadyGate>().IsReady)
        {
            descriptorsByName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);
        }
        else
        {
            try
            {
                var descriptors = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
                descriptorsByName = descriptors.ToDictionary(static d => d.Name, StringComparer.Ordinal);
            }
            catch (Exception)
            {
                descriptorsByName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);
            }
        }

        var approvalGuardFilter = new A2AApprovalGuardFilter(
            services.GetRequiredService<SchemaReadyGate>(),
            catalog,
            services.GetRequiredService<IToolRegistry>(),
            a2aOptions,
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<ILogger<A2AApprovalGuardFilter>>());

        var group = endpoints.MapGroup(pattern).WithTags("Tracon", "A2A");
        // 🚨 Order matters and this one is deliberate: authentication runs FIRST.
        // The approval guard awaits a startup check that has no timeout and no
        // retry limit (only ApplicationStopping cancels it), so with
        // AutoApplyMigrations off and the migration step not yet run, an
        // UNAUTHENTICATED request used to park on that task instead of getting
        // 401 - holding a connection and a thread-pool continuation each. A
        // caller must clear the door before it is allowed to wait in the hall.
        group.AddEndpointFilter(new TraconEndpointFilter(endpointOptions));
        group.AddEndpointFilter(approvalGuardFilter);
        group.RequireApiKeyScope(ApiKeyScope.ExternalInvoke, mandatory: true);

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

            agentGroup.MapA2A(handler, "/")
                .WithName($"TraconA2A_{agentName}")
                .WithSummary($"Invokes the '{agentName}' agent over the agent-to-agent protocol.")
                .WithDescription(
                    "The body is a JSON-RPC message in the A2A shape; this is not the management " +
                    "API's run endpoint and does not stream. A valid API key carrying the " +
                    "'external:invoke' scope is required — exposing an agent to other agents is a " +
                    "separate permission from running it from the console. The run is recorded " +
                    "like any other, so it appears in the run history with its tokens and cost. " +
                    "A tool call that needs approval cannot be answered over this surface and " +
                    "ends the run instead of waiting.");

            agentGroup.MapWellKnownAgentCard(card, "")
                .WithName($"TraconA2AAgentCard_{agentName}")
                .WithSummary($"Returns the A2A agent card describing the '{agentName}' agent.")
                .WithDescription(
                    "The card is how another agent discovers what this one accepts and returns: " +
                    "its name, description, version, supported input and output modes, and " +
                    "declared capabilities. It is generated from the agent's own catalog entry, " +
                    "so editing the agent's description changes the card. Streaming and push " +
                    "notifications are declared unsupported.");
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
