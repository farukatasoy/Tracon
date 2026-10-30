using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Lets a tool report non-token usage from its own body.
/// </summary>
/// <remarks>
/// <para>
/// Speech synthesis is billed by character and speech recognition by second. Neither
/// is a token and cannot be written to the cost columns of the <c>runs</c> table.
/// The measurement attaches to the current call's <see cref="ToolInvocationRecord"/>,
/// and its <see cref="ToolCallUsage.Cost"/> joins the run's non-token total
/// (<see cref="RunRecord.NonTokenCost"/>), which the run's budget, the tenant's quota and
/// the <c>run.completed</c> webhook count beside the token cost.
/// </para>
/// <para>
/// A tool that reports more than once for the same call reports the total at the end:
/// the last report of a call replaces the earlier ones, in the record and in the total.
/// </para>
/// <para>
/// The amount reaches the call tree's budget the moment it is reported. Once the tree has
/// spent its ceiling (<c>AgentGraph.MaxTotalCost</c>), no tool call that has not started
/// begins: its result is a sentence that says the tool call did not start, and a streaming
/// run records the call as failed. The call that crosses the ceiling runs to its end, so a
/// tool should report when it knows the amount, not only at its last line.
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
    /// <see langword="false"/> when run recording is disabled, the call is not in a tool context,
    /// or the measurement is outside what a record holds: an empty unit, a unit longer than 64
    /// characters, a currency longer than 16, a provider longer than 100, a model longer than
    /// 200, a quantity outside 0 to 9,999,999,999 or an amount outside 0 to 1,000,000,000. A
    /// refused measurement is attached to nothing and counts in no total.
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

        // Checked where it is reported, as a function node's item is. A
        // value its column cannot hold failed the insert later, and the failed insert
        // took the call's whole record with it.
        if (NonTokenCostAccumulator.Validate(usage) is not null)
        {
            return false;
        }

        if (TraconRunContext.Current is not { ToolUsage: { } accumulator } scope)
        {
            return false;
        }

        if (FunctionInvokingChatClient.CurrentContext?.CallContent.CallId is not { Length: > 0 } callId)
        {
            return false;
        }

        accumulator.Report(callId, usage);

        // The run's non-token total hears about it NOW, not when the call settles: a
        // non-streaming run reads its tool records after the last model turn, and the
        // tree's budget must be able to refuse the next turn. The last report of a call
        // replaces the earlier one in both places.
        scope.NonTokenCost?.ReportToolMeasurement(callId, usage);

        return true;
    }
}
