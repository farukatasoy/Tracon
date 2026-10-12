using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// Reads a workflow run's parameter values from inside a function node.
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
    ///        .AddWorkflowFunction&lt;string, string&gt;(
    ///            "narrate",
    ///            provider => (input, context, cancellationToken) =>
    ///            {
    ///                var voice = context.GetRunParameters()["voice"];
    ///                return ValueTask.FromResult($"{voice}: {input}");
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
}
