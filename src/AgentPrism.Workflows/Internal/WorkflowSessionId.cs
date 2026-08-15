using System.Globalization;

namespace AgentPrism;

/// <summary>
/// Validates execution session ids.
/// </summary>
/// <remarks>
/// 🚨 <strong>The session id comes from the client and is untrusted input.</strong>
/// Checkpoints are grouped under this value; using it without validation would
/// let one user read or overwrite another execution's state. The same
/// validation was applied to <c>conversation_id</c> in phase 4.
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
    /// <exception cref="AgentPrismException">The id is not in a valid format.</exception>
    public static string Require(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return AgentPrismId.NewId().ToString("n", CultureInfo.InvariantCulture);
        }

        if (sessionId.Length > MaxLength)
        {
            throw new AgentPrismException(
                $"Execution session id may be at most {MaxLength} characters.");
        }

        // A manual loop instead of a regex: MA0009 flags every regex that
        // cannot be given a timeout, and a regex is unnecessary for a pattern
        // this simple anyway.
        foreach (var character in sessionId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                throw new AgentPrismException(
                    "Execution session id may only contain letters, digits, '-', and '_'.");
            }
        }

        return sessionId;
    }
}
