using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Helper that refuses a new run while the process is draining
/// (<see cref="IAgentPrismDrainState.IsDraining"/>).
/// </summary>
/// <remarks>
/// Same calling convention as <see cref="QuotaGate"/>: checked explicitly at
/// the endpoint that starts a run, not through a filter.
/// </remarks>
internal static class DrainGate
{
    /// <summary>Checks whether the process is draining; produces the response to return if so.</summary>
    /// <param name="drainState">The drain state.</param>
    /// <returns>
    /// The <c>503</c> response to return while draining; otherwise <see langword="null"/>.
    /// </returns>
    public static IResult? Check(IAgentPrismDrainState drainState)
    {
        if (!drainState.IsDraining)
        {
            return null;
        }

        return Results.Problem(
            title: "Service is shutting down",
            detail: "The process is draining in-flight runs before it stops; new runs are not accepted. Retry shortly.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
