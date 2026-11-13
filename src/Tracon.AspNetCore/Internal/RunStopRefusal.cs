namespace Tracon;

/// <summary>
/// Tells whether a run ended at a stop the request cannot change by being sent again, and what the caller is told.
/// </summary>
/// <remarks>
/// <para>
/// The provider answered and the request was understood. A <c>502</c> says "the provider is broken" and sends the caller to
/// the wrong system, with no error type to tell one stop from another. These four are the run's own: the loop reached its
/// ceiling with a criterion unsatisfied, the answer is not the structured response the agent asked for, the answer was cut
/// at the output limit the agent's model binding sets, or the provider's filter left nothing to keep. The message of each is
/// written for the caller and names the setting to change; the run record holds the rest.
/// </para>
/// <para>
/// One place decides, because four non-streaming endpoints (the agent run, Chat Completions, Responses and replay) map the
/// same exceptions and each had its own copy of the list. The streamed answers tell the same stop inside the stream.
/// </para>
/// </remarks>
internal static class RunStopRefusal
{
    /// <summary>Gets whether <paramref name="exception"/> is a stop of the run's own.</summary>
    /// <param name="exception">What the run raised.</param>
    /// <returns><see langword="true"/> when the caller is answered <c>422</c> with the exception's error type.</returns>
    public static bool Matches(Exception exception)
        => exception is TraconLoopUnsatisfiedException
            or TraconStructuredResponseException
            or TraconOutputTruncatedException
            or TraconContentFilteredException;

    /// <summary>Gets the title of the problem document for a stop of the run's own.</summary>
    /// <param name="exception">An exception for which <see cref="Matches"/> is <see langword="true"/>.</param>
    /// <returns>The title.</returns>
    public static string Title(Exception exception)
        => exception switch
        {
            TraconLoopUnsatisfiedException => "Loop not satisfied",
            TraconStructuredResponseException => "Structured response invalid",
            TraconOutputTruncatedException => "Output truncated",
            _ => "Content filtered",
        };
}
