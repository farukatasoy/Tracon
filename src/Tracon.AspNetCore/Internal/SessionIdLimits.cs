using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>The size limits of a session identifier a client chooses.</summary>
internal static class SessionIdLimits
{
    /// <summary>
    /// The longest session identifier accepted. The <c>sessions</c> table of SQL Server holds
    /// <c>nvarchar(200)</c> and refuses a longer value with a truncation error, while the other stores
    /// keep it, so the limit is the narrowest column; the AG-UI thread identity uses the same number.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Says whether an identifier starts or ends with whitespace. SQL Server ignores trailing spaces in every
    /// collation, so <c>abc</c> and <c>abc </c> are one row there and two on PostgreSQL, SQLite and in memory; a
    /// session a client opens under a spelling that differs only by an edge space would be another session on one
    /// store and the same one on another. The edge is refused so that the stores agree.
    /// </summary>
    /// <param name="identifier">The identifier the client chose.</param>
    /// <returns><see langword="true"/> when its first or last character is whitespace.</returns>
    public static bool HasEdgeWhitespace(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return identifier.Length > 0 && (char.IsWhiteSpace(identifier[0]) || char.IsWhiteSpace(identifier[^1]));
    }

    /// <summary>
    /// Checks an identifier the client chose against both rules, for an entry that only carries it (an attachment, a speech
    /// clip, a generated image) and so never reaches the checks of the endpoints that open a session.
    /// </summary>
    /// <param name="identifier">The identifier the client sent, if any.</param>
    /// <param name="field">The name of the field or parameter it came in, for the message.</param>
    /// <returns>The <c>400</c> to answer with, or <see langword="null"/> when the identifier is acceptable or absent.</returns>
    public static ProblemHttpResult? Check(string? identifier, string field)
    {
        if (identifier is null)
        {
            return null;
        }

        if (identifier.Length > MaxLength)
        {
            return TypedResults.Problem(
                title: "Session id too long",
                detail: $"'{field}' cannot exceed {MaxLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return HasEdgeWhitespace(identifier)
            ? TypedResults.Problem(
                title: "Session id has surrounding whitespace",
                detail: $"'{field}' cannot start or end with whitespace.",
                statusCode: StatusCodes.Status400BadRequest)
            : null;
    }
}
