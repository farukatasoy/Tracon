using System.Text.Json;
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

        // Nothing here reads the catalog: this method runs BEFORE `app.Run()`, so on a host that stores agents in SQL
        // the migrations have not run yet and a read finds no table. The approval guard waits for the schema and
        // checks the exposed agents (A2AApprovalGuardFilter), and each card reads the catalog when it is asked for.
        var approvalGuardFilter = new A2AApprovalGuardFilter(
            services.GetRequiredService<SchemaReadyGate>(),
            catalog,
            services.GetRequiredService<IToolRegistry>(),
            a2aOptions,
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<ILogger<A2AApprovalGuardFilter>>());

        var group = endpoints.MapGroup(pattern).WithTags("Tracon", "A2A");
        group.WithMetadata(ProblemDetailsOnDenialMarker.Instance);
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

        var cardLastModified = A2AAgentCardResponse.LastModifiedNow(services.GetService<TimeProvider>() ?? TimeProvider.System);

        foreach (var agentName in a2aOptions.ExposedAgents)
        {
            var proxy = services.GetRequiredKeyedService<ExternalAgentProxy>(agentName);
            proxy.AttachServices(services);

            var handler = services.GetRequiredKeyedService<A2AServer>(agentName);

            var agentPath = $"/{Uri.EscapeDataString(agentName)}";
            var agentGroup = group.MapGroup(agentPath);
            var interfacePath = pattern + agentPath;

            agentGroup.MapA2A(handler, "/")
                .WithName($"TraconA2A_{agentName}")
                .WithSummary($"Invokes the '{agentName}' agent over the agent-to-agent protocol.")
                .WithDescription(
                    "The body is a JSON-RPC message in the A2A shape; this is not the management " +
                    "API's run endpoint and does not stream. A valid API key carrying the " +
                    "'external:invoke' scope is required — exposing an agent to other agents is a " +
                    "separate permission from running it from the console. The run is recorded " +
                    "like any other, so it appears in the run history with its tokens and cost. " +
                    "A message with a contextId continues a conversation that belongs to the " +
                    "caller; another caller that sends the same contextId starts a new one. " +
                    "A tool call that needs approval cannot be answered over this surface and " +
                    "ends the run instead of waiting.")
                // MapA2A returns the general convention builder, which has no Produces extension.
                .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(JsonElement), ["application/json"]))
                .AddEndpointFilter(KeepStreamUnbufferedAsync);

            // The card is answered here and not by the SDK's MapWellKnownAgentCard: that one serves a card built once, and
            // the card has two parts that change. The address has to be the one this request arrived on
            // (A2AAgentCardResponse), and the description and version are the catalog entry's as of now: an edit of the
            // agent reaches the next card. The request has passed the approval guard, so the schema is ready.
            agentGroup.MapGet(
                    "/.well-known/agent-card.json",
                    async (HttpContext httpContext, CancellationToken cancellationToken) =>
                    {
                        var descriptor = await FindDescriptorAsync(catalog, agentName, cancellationToken).ConfigureAwait(false);

                        return A2AAgentCardResponse.Create(
                            httpContext,
                            interfacePath,
                            address => BuildAgentCard(agentName, descriptor, address),
                            cardLastModified);
                    })
                .WithName($"TraconA2AAgentCard_{agentName}")
                .WithSummary($"Returns the A2A agent card describing the '{agentName}' agent.")
                .WithDescription(
                    "The card is how another agent discovers what this one accepts and returns: " +
                    "its name, description, version, supported input and output modes, and " +
                    "declared capabilities. It is generated from the agent's own catalog entry, " +
                    "so editing the agent's description changes the card. Streaming and push " +
                    "notifications are declared unsupported. The interface address in the card is " +
                    "absolute: the scheme and host of the request (as the host's forwarded headers " +
                    "middleware sets them) and the application's path base.")
                .Produces<JsonElement>(StatusCodes.Status200OK, contentType: "application/json");
        }

        return group;
    }

    /// <summary>
    /// Tells a reverse proxy not to buffer the answer: <c>SendStreamingMessage</c> is written by the A2A library, which
    /// sets no <c>X-Accel-Buffering</c> header, so an nginx with its default buffering held the first events of a
    /// quiet run back (the other streams Tracon serves carry the header).
    /// </summary>
    /// <remarks>
    /// The header is set before the library writes anything. A unary call carries it too, which costs nothing: a proxy
    /// that honours it only stops holding a short body back. The library still writes no byte, headers included, until
    /// its first event, so against a model that stays silent a proxy sees nothing before the agent answers; the
    /// <c>proxy_read_timeout</c> of that location has to outlast the slowest agent.
    /// </remarks>
    private static async ValueTask<object?> KeepStreamUnbufferedAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers["X-Accel-Buffering"] = "no";

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>Finds the catalog entry of an exposed agent as the catalog holds it now.</summary>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="agentName">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token of the request.</param>
    /// <returns>The entry, or <see langword="null"/> when no source holds the agent (the allow list is fixed at registration).</returns>
    /// <remarks>
    /// A source that fails is logged and left out by the catalog itself, so an agent whose source is down gets the card
    /// of an agent the catalog does not hold: its name for the description, version 1.
    /// </remarks>
    private static async ValueTask<AgentDescriptor?> FindDescriptorAsync(IAgentCatalog catalog, string agentName, CancellationToken cancellationToken)
    {
        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, agentName, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }

    private static AgentCard BuildAgentCard(string agentName, AgentDescriptor? descriptor, string interfaceUrl)
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
                    Url = interfaceUrl,
                    ProtocolBinding = ProtocolBindingNames.JsonRpc,
                },
            ],
        };
}
