using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Tests.Workflows;

/// <summary>
/// A workflow that waits on two human requests at once: the start step sends its message to two
/// request ports, so one run publishes two requests.
/// </summary>
/// <remarks>
/// Answering one request resumes the run from its checkpoint, and the checkpoint republishes the
/// request that is still open with the same request id. That is the shape that makes one
/// request appear on two runs - the run that was answered, which stays
/// <see cref="RunStatus.AwaitingInput"/>, and the run that continued it.
/// </remarks>
internal static class TwoQuestionWorkflow
{
    /// <summary>The first port.</summary>
    public const string FirstPort = TwoQuestionGraph.FirstPort;

    /// <summary>The second port.</summary>
    public const string SecondPort = TwoQuestionGraph.SecondPort;

    /// <summary>The workflow name.</summary>
    public const string Name = TwoQuestionGraph.Name;

    /// <summary>Builds the graph.</summary>
    public static Workflow Build() => TwoQuestionGraph.Build();

    /// <summary>The code registration to hand to the runner.</summary>
    /// <param name="requestTimeout">How long a request may wait; <see langword="null"/> for no limit.</param>
    /// <returns>The registration.</returns>
    public static CodeWorkflowRegistration Registration(TimeSpan? requestTimeout = null)
        => new(Name, "Waits on two requests.", _ => Build(), RequestTimeout: requestTimeout);
}
