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
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The refusal, or the event stream.</returns>
    public static async Task<IResult> HandleAsync(string agentName, HttpContext httpContext, CancellationToken cancellationToken)
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

        LogIgnoredInput(httpContext, input);

        var request = AGUIInputMapper.MapQueue(input);

        if (await AgentTurnService
                .CheckRunGatesAsync(agentName, request, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } gateProblem)
        {
            return gateProblem;
        }

        var tools = await AGUIInputMapper.CheckToolsAsync(agentName, input, httpContext, cancellationToken).ConfigureAwait(false);

        if (tools.Problem is { } toolProblem)
        {
            return toolProblem;
        }

        var resume = await AGUIInputMapper.MapResumeAsync(input, httpContext, cancellationToken).ConfigureAwait(false);

        if (resume.Problem is { } resumeProblem)
        {
            return resumeProblem;
        }

        request = request with { Approvals = resume.Approvals, InputResponses = resume.Inputs };

        var preparation = await AgentTurnService
            .PrepareAsync(agentName, request, prefix: string.Empty, httpContext, cancellationToken, Surface)
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

    private static void LogIgnoredInput(HttpContext httpContext, RunAgentInput input)
    {
        if (!IsPresent(input.State) && !IsPresent(input.ForwardedProperties))
        {
            return;
        }

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.AGUI");

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "An AG-UI request carried 'state' or 'forwardedProps'; Tracon does not use them. Thread {ThreadId}.",
                input.ThreadId);
        }
    }

    private static bool IsPresent(JsonElement? element)
        => element is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) };
}
