using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Helper that refuses a new run while the process is draining
/// (<see cref="ITraconDrainState.IsDraining"/>).
/// </summary>
/// <remarks>
/// <para>
/// Same calling convention as <see cref="QuotaGate"/>: checked explicitly at
/// the endpoint that starts a run, not through a filter.
/// </para>
/// <para>
/// Every surface that starts a run IN THIS PROCESS asks it first: the agent and
/// workflow run endpoints, replay, the two OpenAI-compatible endpoints, the MCP
/// server's <c>tools/call</c>, A2A and the voice conversation socket. A
/// surface that speaks its own protocol answers with that protocol's refusal
/// and reuses <see cref="Title"/> and <see cref="Detail"/>. A surface that only
/// writes a job (trigger, eval, schedule, approval) is not asked: the job
/// worker stops leasing while the process drains, so the job waits.
/// </para>
/// </remarks>
internal static class DrainGate
{
    /// <summary>The refusal's title.</summary>
    public const string Title = "Service is shutting down";

    /// <summary>The refusal's detail; protocol-specific surfaces send this text.</summary>
    public const string Detail =
        "The process is draining in-flight runs before it stops; new runs are not accepted. Retry shortly.";

    /// <summary>Checks whether the process is draining; produces the response to return if so.</summary>
    /// <param name="drainState">The drain state.</param>
    /// <returns>
    /// The <c>503</c> response to return while draining; otherwise <see langword="null"/>.
    /// </returns>
    public static IResult? Check(ITraconDrainState drainState)
    {
        if (!drainState.IsDraining)
        {
            return null;
        }

        return Results.Problem(
            title: Title,
            detail: Detail,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
