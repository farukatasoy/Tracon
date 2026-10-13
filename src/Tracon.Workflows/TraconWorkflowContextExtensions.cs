using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// What a function node reads from, and reports to, its workflow run: the run's parameter
/// values and the non-token usage it spent.
/// </summary>
public static class TraconWorkflowContextExtensions
{
    private static readonly IReadOnlyDictionary<string, string> NoValues =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    /// <summary>
    /// Returns the running workflow's final parameter values.
    /// </summary>
    /// <param name="context">The workflow context the function node received.</param>
    /// <returns>
    /// The values the run was started with, with the workflow's schema defaults
    /// applied. Read-only. Empty when the workflow declares no parameters.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called outside a Tracon workflow run. An empty dictionary would hide the
    /// wiring mistake: a test that forgets to open a run would read "no values"
    /// and pass, and the same code in production would read the real ones.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The values come from the run, not from MAF's state: the workflow engine
    /// has no way to seed a graph's initial state, so Tracon carries the values
    /// in the run's own scope. A resumed run, or a run that continues after a
    /// human answer, reads the same values as the run it continues.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, List&lt;ChatMessage&gt;&gt;(
    ///            "narrate",
    ///            provider => (messages, context, cancellationToken) =>
    ///            {
    ///                var voice = context.GetRunParameters()["voice"];
    ///                return ValueTask.FromResult&lt;List&lt;ChatMessage&gt;&gt;(
    ///                    [new ChatMessage(ChatRole.User, $"{voice}: {messages[^1].Text}")]);
    ///            });
    /// </code>
    /// </example>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> GetRunParameters(this IWorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var scope = TraconRunContext.Current
            ?? throw new InvalidOperationException(
                "GetRunParameters() was called outside a Tracon workflow run. A function node reads its " +
                "workflow's parameter values while the run executes; open a run first.");

        return scope.ParameterValues ?? NoValues;
    }

    /// <summary>
    /// Reports what the function node spent that is not tokens: a text-to-speech
    /// character, a render second, a GPU minute.
    /// </summary>
    /// <param name="context">The workflow context the function node received.</param>
    /// <param name="usage">
    /// The item: the unit and quantity, and the amount when the node knows it. Every call
    /// adds one item; the node reports once per step, not per chunk (at most 1,000 items per run).
    /// </param>
    /// <returns>
    /// A task that yields <see langword="true"/> when the item was recorded.
    /// <see langword="false"/> outside a Tracon workflow run, when the item is invalid
    /// (an empty <see cref="ToolCallUsage.Unit"/>, a negative quantity or amount, or a
    /// value beyond what a column holds), and when the run already reported the most
    /// items it can; the cause is logged.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="usage"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>It does not throw</strong> except for an empty argument. A <see langword="false"/>
    /// result does not interrupt the node: observability does not break functionality, the
    /// same rule as <see cref="TraconToolUsage.Report"/>.
    /// </para>
    /// <para>
    /// <strong>What the item does.</strong> It is written to the run's detail at once
    /// (<c>GET /api/runs/{runId}/usage</c>) and is not cancelled with the run: the work was
    /// done and billed. Its amount joins the run's non-token total, which the run's budget,
    /// the tenant's quota and the <c>run.completed</c> webhook count beside the token cost.
    /// Only an amount in <c>Tracon:Pricing:Currency</c> joins (an item that names no currency is
    /// in it); an item with no amount, or in another currency, is listed and counted as not
    /// added. Tracon never prices the item for you: the node knows its provider's price, and
    /// <see cref="ToolCallUsage.Cost"/> is the amount the node computed.
    /// </para>
    /// <para>
    /// <strong>Retries keep their items.</strong> A node that fails and is retried keeps the
    /// items of the failed attempt: the provider billed that attempt.
    /// </para>
    /// <para>
    /// <strong>An item reported after the run closed</strong> (a node that outlived its
    /// timeout, then finished) is kept in the run's detail and does not change the run's
    /// total: the quota entry and the webhook were already written. A body that honours its
    /// token does not reach this case.
    /// </para>
    /// <para>
    /// The item names the node it was reported from. In a graph built in code, a custom
    /// <c>Executor</c> that is not a function node registered with Tracon reports with no node name.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, string&gt;(
    ///            "narrate",
    ///            provider => async (messages, context, cancellationToken) =>
    ///            {
    ///                var text = messages[messages.Count - 1].Text;
    ///
    ///                // ... synthesize the speech with your provider, then report what it billed.
    ///                await context.ReportUsageAsync(new ToolCallUsage
    ///                {
    ///                    Unit = ToolUsageUnits.Characters,
    ///                    Quantity = text.Length,
    ///                    Cost = text.Length * 0.00003m,
    ///                    Currency = "USD",
    ///                    Provider = "elevenlabs",
    ///                    Model = "eleven_multilingual_v2",
    ///                });
    ///
    ///                return text;
    ///            });
    /// </code>
    /// </example>
    /// </remarks>
    public static ValueTask<bool> ReportUsageAsync(this IWorkflowContext context, ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(usage);

        return NonTokenUsageReporter.ReportAsync(WorkflowFunctionNodeScope.CurrentName, usage);
    }
}
