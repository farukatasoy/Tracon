using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Workflows.UnitTests.Fakes;

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
    public const string FirstPort = "first-port";

    /// <summary>The second port.</summary>
    public const string SecondPort = "second-port";

    /// <summary>The workflow name.</summary>
    public const string Name = "two-questions";

    /// <summary>Builds the graph.</summary>
    public static Workflow Build()
    {
        var first = RequestPort.Create<string, bool>(FirstPort).BindAsExecutor(allowWrappedRequests: false);
        var second = RequestPort.Create<string, bool>(SecondPort).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var endFirst = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "first approved" : "first rejected",
            id: "end-first");

        var endSecond = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "second approved" : "second rejected",
            id: "end-second");

        return new WorkflowBuilder(start)
            .AddEdge(start, first)
            .AddEdge(start, second)
            .AddEdge(first, endFirst)
            .AddEdge(second, endSecond)
            .WithOutputFrom(endFirst, endSecond)
            .WithName(Name)
            .Build();
    }

    /// <summary>The code registration to hand to the runner.</summary>
    /// <param name="requestTimeout">How long a request may wait; <see langword="null"/> for no limit.</param>
    /// <returns>The registration.</returns>
    public static CodeWorkflowRegistration Registration(TimeSpan? requestTimeout = null)
        => new(Name, "Waits on two requests.", _ => Build(), RequestTimeout: requestTimeout);
}
