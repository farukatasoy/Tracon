using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Tests.Workflows;

/// <summary>
/// The graph of a workflow that waits on two human requests at once: the start step sends its message to two request ports,
/// so one run publishes two requests.
/// </summary>
/// <remarks>
/// Only types of Microsoft Agent Framework appear here, so a test project that cannot see Tracon's internal code registration
/// links this file alone and builds the same graph as the workflow unit tests, instead of keeping a copy of it.
/// </remarks>
internal static class TwoQuestionGraph
{
    /// <summary>The first port.</summary>
    public const string FirstPort = "first-port";

    /// <summary>The second port.</summary>
    public const string SecondPort = "second-port";

    /// <summary>The workflow name.</summary>
    public const string Name = "two-questions";

    /// <summary>Builds the graph.</summary>
    /// <returns>The workflow.</returns>
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
}
