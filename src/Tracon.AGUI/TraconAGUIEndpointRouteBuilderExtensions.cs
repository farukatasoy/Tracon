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
    /// Maps the AG-UI endpoint under the default prefix <c>/tracon/agui</c>: <c>POST /tracon/agui/{agentName}</c>.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><c>MapTracon()</c> has not been called first.</exception>
    /// <remarks>
    /// See <see cref="MapTraconAGUI(IEndpointRouteBuilder, string)"/> for what the endpoint does.
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapTracon();
    /// app.MapTraconAGUI();
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapTraconAGUI(this IEndpointRouteBuilder endpoints)
        => MapTraconAGUI(endpoints, DefaultPattern, static _ => { });

    /// <summary>
    /// Maps the AG-UI endpoint: <c>POST {pattern}/{agentName}</c>.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="pattern">Path prefix, such as <see cref="DefaultPattern"/>.</param>
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
    /// Applies the SAME three-layer protection as <c>MapTracon</c> (loopback, bearer token, authorization policy) and the
    /// <c>Operator</c> role; the settings are inherited from there. An API key needs the <c>ChatRun</c> or the
    /// <c>RunsWrite</c> scope: a key that holds only <c>ChatRun</c> opens this endpoint and no other. The returned builder
    /// is the endpoint group: chain <c>RequireAuthorization()</c> on it to add your own policy.
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
    /// <c>state</c> is not used, and <c>forwardedProps</c> is not used unless the overload that takes
    /// <see cref="TraconAGUIOptions"/> turns on <see cref="TraconAGUIOptions.AcceptForwardedParameters"/>.</description></item>
    /// <item><description>The event stream carries no <c>rawEvent</c>: the provider's raw update does not reach the browser.</description></item>
    /// </list>
    /// <example>
    /// <code>
    /// app.MapTracon();
    /// app.MapTraconAGUI("/chat/agui");
    /// </code>
    /// </example>
    /// </remarks>
    public static IEndpointConventionBuilder MapTraconAGUI(this IEndpointRouteBuilder endpoints, string pattern)
        => MapTraconAGUI(endpoints, pattern, static _ => { });

    /// <summary>
    /// Maps the AG-UI endpoint with settings: <c>POST {pattern}/{agentName}</c>.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="pattern">Path prefix. The usual one is <c>/tracon/agui</c> (<see cref="DefaultPattern"/>).</param>
    /// <param name="configure">Sets the endpoint's settings.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is empty.</exception>
    /// <exception cref="InvalidOperationException"><c>MapTracon()</c> has not been called first.</exception>
    /// <remarks>
    /// <para>
    /// Everything the overload without settings says holds here. The settings are read when the endpoint is mapped: a
    /// change made to the options afterwards does not reach the running endpoint.
    /// </para>
    /// <example>
    /// <code>
    /// app.MapTracon();
    /// app.MapTraconAGUI("/tracon/agui", options => options.AcceptForwardedParameters = true);
    /// </code>
    /// </example>
    /// </remarks>
    public static IEndpointConventionBuilder MapTraconAGUI(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Action<TraconAGUIOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TraconAGUIOptions();

        configure(options);

        // Read once, here: a flag changed after the endpoint is mapped must not loosen a running endpoint.
        var acceptForwardedParameters = options.AcceptForwardedParameters;

        var services = endpoints.ServiceProvider;
        var endpointOptions = TraconEndpointRouteBuilderExtensions.RequireSharedEndpointOptions(endpoints, "AG-UI");
        var normalizedPattern = '/' + pattern.Trim('/');
        var roles = TraconRolePolicies.Resolve(services, endpointOptions);

        // A browser front end runs on another origin more often than not (CopilotKit's dev server next to the API), so the
        // path needs the same CORS answer the management API gives. Under the default prefix MapTracon's own middleware
        // already answers; applying the same fixed policy a second time is idempotent.
        TraconCorsMiddleware.Map(endpoints, endpointOptions, normalizedPattern);

        var group = endpoints.MapGroup(normalizedPattern).WithTags("Tracon", "AG-UI");

        // The stream keeps itself alive at the cadence MapTracon's group sets.
        group.WithMetadata(new SseKeepAliveMetadata(endpointOptions.StreamKeepAliveInterval));
        group.WithMetadata(ProblemDetailsOnDenialMarker.Instance);
        group.AddEndpointFilter(new TraconEndpointFilter(endpointOptions));

        // Off by default, like the management API's. The group counts its own window: an AG-UI
        // request does not spend the management API's, with Scope Instance or Deployment alike.
        if (TraconRateLimitFilter.ForAgUi(services) is { } rateLimitFilter)
        {
            group.AddEndpointFilter(rateLimitFilter);
        }

        if (endpointOptions.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        group.MapPost(
                "/{agentName}",
                (string agentName, HttpContext httpContext, CancellationToken cancellationToken) =>
                    AGUIRunEndpoint.HandleAsync(agentName, httpContext, acceptForwardedParameters, cancellationToken))
            .RequireRole(roles.Operator)
            .RequireAnyApiKeyScope(ApiKeyScope.ChatRun, ApiKeyScope.RunsWrite)
            .WithName("TraconAGUIRun")
            .WithSummary("Runs an agent for an AG-UI front end and streams the AG-UI events.")
            .WithDescription(
                "The body is an AG-UI RunAgentInput; the answer is text/event-stream with one 'data:' frame per AG-UI event. " +
                "The run is a Tracon run: it is recorded, counted against the quota and priced like one started from the " +
                "management API, and the same gates apply (role, session ownership, content guard, quota). " +
                "The thread is the session. Only the messages after the last assistant message are read as new input, a " +
                "tool message answers a pending client-side tool call, and 'context' becomes run documents; 'state' is " +
                "not used, and 'forwardedProps' is not used unless the host turned on AcceptForwardedParameters, which " +
                "reads 'forwardedProps.parameters' as the run's parameters. 'tools' must name every client-side tool the agent carries, otherwise the " +
                "request is refused with 400 naming the missing ones; the schema of a tool always comes from the code. A " +
                "tool the agent does not carry is ignored and the run records the names. A tool that waits for approval, " +
                "and an MCP server that asks the user for input, end the run with an interrupt in RUN_FINISHED; the answer " +
                "is a 'resume' entry of the next request, with { approved, reason, remember } for an approval and the " +
                "requested fields for an input. The first event after RUN_STARTED is a CUSTOM event named 'tracon.run' " +
                "that carries the Tracon run id. No event carries 'rawEvent'. An API key needs the ChatRun or the RunsWrite scope. " +
                "A failure after the stream began is a RUN_ERROR event.")
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
