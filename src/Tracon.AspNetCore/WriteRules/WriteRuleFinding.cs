using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// A refusal one of the definition write rules makes, before it becomes an HTTP answer (a save endpoint) or a
/// line of an import report.
/// </summary>
/// <remarks>
/// The write rules of agents, workflows, skills and MCP servers used to live inside their save endpoints and
/// answer with a <see cref="ProblemHttpResult"/> directly. An import is a second writer of the same four
/// definitions, so the rules answer with this neutral shape and each caller turns it into its own answer: a
/// rule that only one of them knew would let an import write what the save refuses (K-455).
/// </remarks>
internal sealed record WriteRuleFinding
{
    /// <summary>Gets the HTTP status the save endpoint answers with.</summary>
    public required int Status { get; init; }

    /// <summary>Gets the title of the <c>ProblemDetails</c> the save endpoint answers with.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the detail of the <c>ProblemDetails</c>, and the message of an import report line.</summary>
    public required string Detail { get; init; }

    /// <summary>Gets the stable code of the refusal (<see cref="WriteRuleCodes"/>).</summary>
    public required string Code { get; init; }

    /// <summary>Gets the path of the offending field inside the definition, or <see langword="null"/>.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets every error the rule found, when it found more than one: the entity validation of an agent reports
    /// each unknown tool and skill on its own line, and the save answers with all of them joined.
    /// </summary>
    public IReadOnlyList<ValidationMessage> Messages { get; init; } = [];

    /// <summary>Creates a finding.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="title">The problem title.</param>
    /// <param name="detail">The problem detail.</param>
    /// <param name="code">The stable code.</param>
    /// <param name="path">The offending field, if one is known.</param>
    /// <returns>The finding.</returns>
    public static WriteRuleFinding Of(int status, string title, string detail, string code, string? path = null)
        => new() { Status = status, Title = title, Detail = detail, Code = code, Path = path };

    /// <summary>Creates a <c>400 Bad Request</c> finding.</summary>
    /// <param name="title">The problem title.</param>
    /// <param name="detail">The problem detail.</param>
    /// <param name="code">The stable code.</param>
    /// <param name="path">The offending field, if one is known.</param>
    /// <returns>The finding.</returns>
    public static WriteRuleFinding BadRequest(string title, string detail, string code, string? path = null)
        => Of(StatusCodes.Status400BadRequest, title, detail, code, path);

    /// <summary>Creates a <c>409 Conflict</c> finding.</summary>
    /// <param name="title">The problem title.</param>
    /// <param name="detail">The problem detail.</param>
    /// <param name="code">The stable code.</param>
    /// <returns>The finding.</returns>
    public static WriteRuleFinding Conflict(string title, string detail, string code)
        => Of(StatusCodes.Status409Conflict, title, detail, code);

    /// <summary>Turns the finding into the answer a save endpoint returns.</summary>
    /// <returns>The <c>ProblemDetails</c> answer.</returns>
    public ProblemHttpResult ToProblem()
        => TypedResults.Problem(title: Title, detail: Detail, statusCode: Status);

    /// <summary>Turns the finding into the lines an import report carries.</summary>
    /// <returns>One line for each error the rule found; one line when it found a single refusal.</returns>
    public IReadOnlyList<ValidationMessage> ToMessages()
        => Messages.Count > 0
            ? Messages
            :
            [
                new ValidationMessage
                {
                    Severity = ValidationSeverity.Error,
                    Code = Code,
                    Message = Detail,
                    Path = Path,
                },
            ];
}
