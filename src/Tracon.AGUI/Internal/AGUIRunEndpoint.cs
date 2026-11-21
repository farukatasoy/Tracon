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
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The refusal, or the event stream.</returns>
    public static async Task<IResult> HandleAsync(
        string agentName,
        HttpContext httpContext,
        bool acceptForwardedParameters,
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
        var parameters = acceptForwardedParameters ? AGUIInputMapper.ReadParameters(input) : ParameterMapping.None;

        if (parameters.Problem is { } parametersProblem)
        {
            return parametersProblem;
        }

        LogIgnoredInput(httpContext, input, acceptForwardedParameters);

        var request = AGUIInputMapper.MapQueue(input) with { Parameters = parameters.Values };

        if (await AgentTurnService
                .CheckRunGatesAsync(agentName, request, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } gateProblem)
        {
            return gateProblem;
        }

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
                })
            .ConfigureAwait(false);

        if (preparation.Turn is not { } turn)
        {
            return preparation.Problem!;
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
