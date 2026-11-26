using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The handler of <c>POST {pattern}/{agentName}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The order is the management API run endpoint's, with the AG-UI mapping where the body binding is: the drain gate, the
/// body, the gates every run passes (attribution, authorization, session ownership, quota, pre-flight), and only then the parts
/// of the request that name the agent's own tools and sessions, so a caller who may not run the agent learns nothing about it.
/// </para>
/// <para>
/// Everything that can be refused with a status code is refused before the response begins.
/// </para>
/// </remarks>
internal static class AGUIRunEndpoint
{
    /// <summary>The surface name an approval decision is audited under.</summary>
    internal const string Surface = "ag-ui";

    /// <summary>Handles one AG-UI run request.</summary>
    /// <param name="agentName">The agent the path names.</param>
    /// <param name="httpContext">The request.</param>
    /// <param name="acceptForwardedParameters">
    /// Whether <c>forwardedProps.parameters</c> supplies the run parameters, as the host decided when it mapped the endpoint.
    /// </param>
    /// <param name="resolveParameters">
    /// The host's own code that decides the run parameters (<see cref="TraconAGUIOptions.ResolveParameters"/>); it replaces
    /// <paramref name="acceptForwardedParameters"/> when set.
    /// </param>
    /// <param name="resolveGreeting">
    /// The host's own code that decides the sentence an assistant says first (<see cref="TraconAGUIOptions.ResolveGreeting"/>);
    /// when set, a request with no new input is an opening request.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The refusal, or the event stream.</returns>
    public static async Task<IResult> HandleAsync(
        string agentName,
        HttpContext httpContext,
        bool acceptForwardedParameters,
        Func<AGUIParameterRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, string>?>>? resolveParameters,
        Func<AGUIParameterRequest, CancellationToken, ValueTask<string?>>? resolveGreeting,
        CancellationToken cancellationToken)
    {
        if (AgentTurnService.CheckDrain(httpContext) is { } drainProblem)
        {
            return drainProblem;
        }

        // The management API's run endpoint answers 415 to a body that is not JSON; this one reads the body itself, so it
        // asks. A browser form post cannot send application/json without a preflight, which is what keeps a cookie-carrying
        // cross-site page from starting a run with an ambient identity.
        if (!httpContext.Request.HasJsonContentType())
        {
            return Results.Problem(
                title: "Unsupported media type",
                detail: "The request body must be application/json: an AG-UI RunAgentInput.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        RunAgentInput? input;

        try
        {
            input = await JsonSerializer
                .DeserializeAsync(httpContext.Request.Body, AGUIJsonSerializerContext.Default.RunAgentInput, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return Results.Problem(
                title: RequestBodyBinding.ProblemTitle,
                detail: RequestBodyBinding.Describe(exception),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (input is null)
        {
            return Results.Problem(
                title: RequestBodyBinding.ProblemTitle,
                detail: "The request body is empty; an AG-UI RunAgentInput is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (AGUIInputMapper.Validate(input) is { } invalid)
        {
            return invalid;
        }

        // The events echo this identity; a front end that sends none still gets a well-formed stream.
        if (string.IsNullOrWhiteSpace(input.RunId))
        {
            input.RunId = Guid.NewGuid().ToString("N");
        }

        // Off, forwardedProps is the front end's own and is not read at all: the request is what it always was. On, only the
        // `parameters` property is read, and its shape is refused here like any other body the endpoint cannot map; whether
        // the names and the values fit the agent is the turn preparation's check, the one the run endpoint makes.
        //
        // With a resolver the front end's values are the resolver's input and nothing more: they are read for their shape
        // here, and enter the run only if the resolver returns them.
        //
        // A greeting resolver reads them for its own input as well, and nothing more: they do not enter a run it does not start.
        var useResolver = resolveParameters is not null;
        var readForGreeting = resolveGreeting is not null;
        var parameters = acceptForwardedParameters || useResolver || readForGreeting
            ? AGUIInputMapper.ReadParameters(input)
            : ParameterMapping.None;

        if (parameters.Problem is { } parametersProblem)
        {
            return parametersProblem;
        }

        LogIgnoredInput(httpContext, input, acceptForwardedParameters || useResolver || readForGreeting);

        var request = AGUIInputMapper.MapQueue(input) with
        {
            Parameters = useResolver || !acceptForwardedParameters ? null : parameters.Values,
        };

        if (await AgentTurnService
                .CheckRunGatesAsync(agentName, request, RunChannel.AGUI, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } gateProblem)
        {
            return gateProblem;
        }

        // After the gates: a request that is turned away never reaches the host's code. The gates read no parameter, so
        // the values can be decided after them without the gates being any the wiser.
        var parameterSource = ParameterValueSource.Client;

        if (resolveParameters is not null)
        {
            // The agent is known before the host's code is asked about it: the name is the caller's, and a resolver that is
            // handed a name nothing carries would answer a failure of its own for what is a plain 404.
            if (await httpContext.RequestServices.GetRequiredService<IAgentCatalog>()
                    .DescribeAsync(agentName, cancellationToken)
                    .ConfigureAwait(false) is null)
            {
                return Results.Problem(
                    title: "Agent not found",
                    detail: $"There is no agent named '{agentName}'.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var resolved = await ResolveParametersAsync(
                resolveParameters,
                agentName,
                input,
                parameters.Values,
                httpContext,
                cancellationToken).ConfigureAwait(false);

            if (resolved.Problem is { } resolverProblem)
            {
                return resolverProblem;
            }

            request = request with { Parameters = resolved.Values };
            parameterSource = ParameterValueSource.Host;
        }

        // An opening request carries nothing the run endpoint would answer: no message, no tool result and no resume. With a
        // greeting resolver that is a request for the first sentence, and without one it stays the 400 it always was.
        var opening = resolveGreeting is not null
            && !AGUIInputMapper.HasNewMessages(input)
            && string.IsNullOrWhiteSpace(request.Message)
            && request.ToolResults.Count == 0
            && request.AttachmentIds.Count == 0
            && input.Resume is not { Count: > 0 };

        var resume = await AGUIInputMapper.MapResumeAsync(input, httpContext, cancellationToken).ConfigureAwait(false);

        if (resume.Problem is { } resumeProblem)
        {
            return resumeProblem;
        }

        request = request with { Approvals = resume.Approvals, InputResponses = resume.Inputs };

        // The tools of the run are the tools of the version the experiment assigns, which is known inside the
        // preparation; a check before it read the latest definition.
        var tools = ToolBoundary.None;

        var preparation = await AgentTurnService
            .PrepareAsync(
                agentName,
                request,
                prefix: string.Empty,
                httpContext,
                cancellationToken,
                Surface,
                async version =>
                {
                    tools = await AGUIInputMapper
                        .CheckToolsAsync(agentName, input, version, httpContext, cancellationToken)
                        .ConfigureAwait(false);

                    return tools.Problem;
                },
                parameterSource,
                allowEmptyRequest: opening)
            .ConfigureAwait(false);

        if (preparation.Turn is not { } turn)
        {
            return preparation.Problem!;
        }

        if (opening)
        {
            return await AGUIGreeting
                .OpenAsync(turn, input, parameters.Values, resolveGreeting!, httpContext, cancellationToken)
                .ConfigureAwait(false);
        }

        OpenedTurn opened;

        try
        {
            opened = await turn.OpenAsync(httpContext, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected before the run began.
            return Results.Empty;
        }
        catch (Exception exception)
        {
            return AgentTurnService.DescribeFailure(exception, turn.RunId, httpContext);
        }

        return new AGUIRunStream(opened, input, turn.RunId, tools.Ignored);
    }

    /// <summary>Runs the host's parameter resolver and checks what it returned for a value that cannot be one.</summary>
    /// <remarks>
    /// The answer to a resolver that failed never carries the front end's text or the exception's: the log line has the agent,
    /// the thread and the exception, and the front end is told which kind of failure it was.
    /// </remarks>
    private static async ValueTask<(IResult? Problem, IReadOnlyDictionary<string, string>? Values)> ResolveParametersAsync(
        Func<AGUIParameterRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, string>?>> resolveParameters,
        string agentName,
        RunAgentInput input,
        IReadOnlyDictionary<string, string>? forwarded,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var supplied = await resolveParameters(
                    new AGUIParameterRequest(
                        agentName,
                        input.ThreadId,
                        httpContext.User,
                        forwarded ?? new Dictionary<string, string>(StringComparer.Ordinal)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (supplied is null or { Count: 0 })
            {
                return (null, null);
            }

            // A copy, made inside the same guard as the call: the resolver may keep its dictionary and change it while it is
            // read, and that is its failure as much as a throw is. A value that is not text cannot be checked against the
            // schema, and a run must not start on a guess.
            var copy = new Dictionary<string, string>(supplied.Count, StringComparer.Ordinal);

            foreach (var (name, value) in supplied)
            {
                if (name is null || value is null)
                {
                    throw new InvalidOperationException("The parameter resolver returned a name or a value that is null.");
                }

                copy[name] = value;
            }

            return (null, copy);
        }
        catch (Exception exception) when (AGUIEventStream.IsFailure(exception, cancellationToken))
        {
            return (ResolverFailed(httpContext, agentName, input.ThreadId, exception), null);
        }
    }

    private static IResult ResolverFailed(HttpContext httpContext, string agentName, string threadId, Exception exception)
    {
        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Tracon.AGUI")
            .LogError(exception, "The AG-UI parameter resolver failed for agent '{AgentName}', thread {ThreadId}; the run was not started.", agentName, threadId);

        return Results.Problem(
            title: "Run parameters could not be resolved",
            detail: "The host's parameter resolver failed, so the run was not started.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errorType"] = TraconErrorTypes.ParameterResolutionFailed,
            });
    }

    // The log says that something was ignored and never what it held: forwardedProps can carry a run parameter's value.
    private static void LogIgnoredInput(HttpContext httpContext, RunAgentInput input, bool acceptForwardedParameters)
    {
        var state = IsPresent(input.State);

        // Taken, forwardedProps is read for `parameters` alone; the rest of it is the front end's and is not worth a line.
        var forwardedProps = !acceptForwardedParameters && IsPresent(input.ForwardedProperties);

        if (!state && !forwardedProps)
        {
            return;
        }

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.AGUI");

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "An AG-UI request carried {Ignored}; Tracon does not use it. Thread {ThreadId}.",
                state && forwardedProps ? "'state' and 'forwardedProps'" : state ? "'state'" : "'forwardedProps'",
                input.ThreadId);
        }
    }

    private static bool IsPresent(JsonElement? element)
        => element is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) };
}
