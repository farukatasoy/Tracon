using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Lets a tool report non-token usage from its own body.
/// </summary>
/// <remarks>
/// <para>
/// Speech synthesis is billed by character and speech recognition by second. Neither
/// is a token and cannot be written to the cost columns of the <c>runs</c> table.
/// The measurement attaches to the current call's <see cref="ToolInvocationRecord"/>.
/// </para>
/// <para>
/// It reads the call identifier from <see cref="FunctionInvokingChatClient.CurrentContext"/>.
/// This was verified by measurement on 2026-08-05. The <c>AIFunctionArguments.Context</c>
/// dictionary is <see langword="null"/> and does not carry the call identifier, while the
/// static context is populated in the tool body. Dependencies resolve through <c>AIFunctionArguments.Services</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// TraconToolUsage.Report(new ToolCallUsage
/// {
///     Unit = ToolUsageUnits.Characters,
///     Quantity = audio.CharactersBilled ?? request.Text.Length,
///     Cost = price,
///     Currency = "USD",
///     IsEstimated = audio.CharactersBilled is null,
/// });
/// </code>
/// </example>
public static class TraconToolUsage
{
    /// <summary>Reports usage for the current tool call.</summary>
    /// <param name="usage">The usage measurement.</param>
    /// <returns>
    /// <see langword="true"/> when the measurement is attached to a call. Returns
    /// <see langword="false"/> when run recording is disabled or the call is not in a tool context.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method <strong>never throws</strong>, except for an empty argument. A
    /// <see langword="false"/> result does not interrupt the tool. Observability does
    /// not break functionality. This is the same rule that prevents a store error from stopping a run.
    /// </remarks>
    public static bool Report(ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (TraconRunContext.Current?.ToolUsage is not { } accumulator)
        {
            return false;
        }

        if (FunctionInvokingChatClient.CurrentContext?.CallContent.CallId is not { Length: > 0 } callId)
        {
            return false;
        }

        accumulator.Report(callId, usage);

        return true;
    }
}
