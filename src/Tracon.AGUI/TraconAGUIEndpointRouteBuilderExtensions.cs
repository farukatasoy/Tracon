using AGUI.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Extensions that connect the HTTP endpoint a front end speaking the AG-UI protocol runs a Tracon agent through.
/// </summary>
public static class TraconAGUIEndpointRouteBuilderExtensions
{
    /// <summary>Default path prefix.</summary>
    public const string DefaultPattern = "/tracon/agui";

    /// <summary>
    /// Maps the AG-UI endpoint: <c>POST {pattern}/{agentName}</c>.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="pattern">Path prefix. Default is <c>/tracon/agui</c>.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is empty.</exception>
    /// <exception cref="InvalidOperationException"><c>MapTracon()</c> has not been called first.</exception>
    /// <remarks>
    /// <para>
    /// The body is an AG-UI <c>RunAgentInput</c> and the answer is the AG-UI event stream (<c>text/event-stream</c>, one
    /// <c>data:</c> frame per event). Any catalog agent the caller's role may run is reachable; the agent is named in the
    /// path. This is the management API's run endpoint spoken in the AG-UI dialect: the run is recorded, the tenant is
    /// resolved, the quota and the session ownership are enforced and the cost is written, exactly as for
    /// <c>POST /api/agents/{name}/run</c>.
    /// </para>
    /// <para>
    /// Applies the SAME three-layer protection as <c>MapTracon</c> (loopback, bearer token, authorization policy), the
    /// <c>Operator</c> role and the <c>runs:write</c> API key scope; the settings are inherited from there. The returned
    /// builder is the endpoint group: chain <c>RequireAuthorization()</c> on it to add your own policy.
    /// </para>
    /// <para>
    /// What an AG-UI request means here, and what it does not:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>threadId</c> is the session. The server's history is the truth: only the messages after the last
    /// assistant message are new input, so a front end that sends the whole conversation every turn adds nothing twice.</description></item>
    /// <item><description><c>tools</c> must name every client-side tool the agent carries (<c>AddClientTool</c>); the schema comes from the
    /// code, never from the request. A declared tool the agent does not carry is ignored.</description></item>
    /// <item><description><c>resume</c> answers a tool approval or a request for user input; <c>context</c> becomes run documents.
    /// <c>state</c> and <c>forwardedProps</c> are not used.</description></item>
    /// </list>
    /// <example>
    /// <code>
    /// app.MapTracon();
    /// app.MapTraconAGUI();
    /// </code>
    /// </example>
    /// </remarks>
    public static IEndpointConventionBuilder MapTraconAGUI(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var services = endpoints.ServiceProvider;
        var endpointOptions = TraconEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "AG-UI");
        var normalizedPattern = '/' + pattern.Trim('/');
        var roles = TraconRolePolicies.Resolve(services, endpointOptions);

        // A browser front end runs on another origin more often than not (CopilotKit's dev server next to the API), so the
        // path needs the same CORS answer the management API gives. Under the default prefix MapTracon's own middleware
        // already answers; applying the same fixed policy a second time is idempotent.
        TraconCorsMiddleware.Map(endpoints, endpointOptions, normalizedPattern);

        var group = endpoints.MapGroup(normalizedPattern).WithTags("Tracon", "AG-UI");

        // The stream keeps itself alive at the cadence MapTracon's group sets (F-325).
        group.WithMetadata(new SseKeepAliveMetadata(endpointOptions.StreamKeepAliveInterval));
        group.AddEndpointFilter(new TraconEndpointFilter(endpointOptions));

        // Off by default, like the management API's (K-165).
        if (services.GetService<IOptionsMonitor<TraconRateLimitOptions>>() is { } rateLimitOptions)
        {
            group.AddEndpointFilter(new TraconRateLimitFilter(rateLimitOptions));
        }

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        group.MapPost("/{agentName}", AGUIRunEndpoint.HandleAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconAGUIRun")
            .WithSummary("Runs an agent for an AG-UI front end and streams the AG-UI events.")
            .WithDescription(
                "The body is an AG-UI RunAgentInput; the answer is text/event-stream with one 'data:' frame per AG-UI event. " +
                "The run is a Tracon run: it is recorded, counted against the quota and priced like one started from the " +
                "management API, and the same gates apply (role, session ownership, content guard, quota). " +
                "The thread is the session. Only the messages after the last assistant message are read as new input, a " +
                "tool message answers a pending client-side tool call, and 'context' becomes run documents; 'state' and " +
                "'forwardedProps' are not used. 'tools' must name every client-side tool the agent carries, otherwise the " +
                "request is refused with 400 naming the missing ones; the schema of a tool always comes from the code. A " +
                "tool the agent does not carry is ignored and the run records the names. A tool that waits for approval, " +
                "and an MCP server that asks the user for input, end the run with an interrupt in RUN_FINISHED; the answer " +
                "is a 'resume' entry of the next request, with { approved, reason, remember } for an approval and the " +
                "requested fields for an input. The first event after RUN_STARTED is a CUSTOM event named 'tracon.run' " +
                "that carries the Tracon run id. A failure after the stream began is a RUN_ERROR event.")
            .Accepts<RunAgentInput>("application/json")
            .Produces<string>(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }
}
