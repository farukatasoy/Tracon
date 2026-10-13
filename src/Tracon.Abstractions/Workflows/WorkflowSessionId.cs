using System.Globalization;

namespace Tracon;

/// <summary>
/// Validates execution session ids.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The session id comes from the client and is untrusted input.</strong>
/// Checkpoints are grouped under this value; using it without validation would
/// let one user read or overwrite another execution's state. The same
/// validation is applied to <c>conversation_id</c>.
/// </para>
/// <para>
/// It lives in this package, not in the workflow engine, because three
/// packages apply the one rule: the engine when a run starts, the HTTP layer
/// before it queues a run (a queued run is refused with <c>400</c>, not failed
/// after the <c>202</c>), and the batch item reader (an item with a bad id fails
/// on its own).
/// </para>
/// </remarks>
internal static class WorkflowSessionId
{
    /// <summary>The maximum number of characters accepted.</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Validates the given id; generates a new one if it is empty.
    /// </summary>
    /// <param name="sessionId">The id supplied by the client.</param>
    /// <returns>A usable id.</returns>
    /// <exception cref="TraconException">The id is not in a valid format.</exception>
    public static string Require(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return NewId();
        }

        return Problem(sessionId) is { } problem ? throw new TraconException(problem) : sessionId;
    }

    /// <summary>Generates a new id in the format <see cref="Require"/> produces.</summary>
    /// <returns>A new id.</returns>
    public static string NewId() => TraconId.NewId().ToString("n", CultureInfo.InvariantCulture);

    /// <summary>Returns why a given id is not valid, without throwing.</summary>
    /// <param name="sessionId">A non-empty id supplied by the client.</param>
    /// <returns>The reason; <see langword="null"/> when the id is valid.</returns>
    public static string? Problem(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (sessionId.Length > MaxLength)
        {
            return $"Execution session id may be at most {MaxLength} characters.";
        }

        // A manual loop instead of a regex: MA0009 flags every regex that
        // cannot be given a timeout, and a regex is unnecessary for a pattern
        // this simple anyway.
        foreach (var character in sessionId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return "Execution session id may only contain letters, digits, '-', and '_'.";
            }
        }

        return null;
    }
}
