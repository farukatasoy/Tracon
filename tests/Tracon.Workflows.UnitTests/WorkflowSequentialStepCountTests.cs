using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A sequential chain takes one super-step for each step (one more when it is a plain list of agent names), and the save
/// refuses the chain the engine would stop.
/// </summary>
/// <remarks>
/// The validator's rule is a claim about the engine, so the claim is measured here: each case runs a chain at a limit
/// one short of what the rule says it needs and at the limit it says is enough, and the rule has to agree with how
/// both ended. A chain of 2 000 steps used to be saved, and every run of it ended <c>Failed</c> at the limit.
/// </remarks>
public sealed class WorkflowSequentialStepCountTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task A_chain_of_nodes_needs_one_super_step_a_step(int steps)
    {
        var definition = FunctionChain(steps, out var host);

        (await EndedAsync(host, definition, maxSuperSteps: steps)).ShouldBe(RunEventType.RunCompleted);
        (await EndedAsync(host, definition, maxSuperSteps: steps - 1)).ShouldBe(RunEventType.RunFailed);

        WorkflowDefinitionValidator.StepCountProblem(definition, steps).ShouldBeNull();
        WorkflowDefinitionValidator.StepCountProblem(definition, steps - 1).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_chain_of_agents_listed_as_nodes_needs_one_super_step_a_step_as_well()
    {
        var names = Enumerable.Range(0, 3).Select(static index => $"agent{index}").ToArray();
        var host = new WorkflowTestHost(names);
        var definition = new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [.. names.Select(static name => new WorkflowNodeReference { Name = name, Kind = WorkflowNodeKind.Agent })],
        };

        (await EndedAsync(host, definition, maxSuperSteps: 3)).ShouldBe(RunEventType.RunCompleted);
        (await EndedAsync(host, definition, maxSuperSteps: 2)).ShouldBe(RunEventType.RunFailed);

        WorkflowDefinitionValidator.StepCountProblem(definition, 3).ShouldBeNull();
        WorkflowDefinitionValidator.StepCountProblem(definition, 2).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task A_chain_of_agent_names_needs_one_super_step_more_than_it_has_steps(int steps)
    {
        var names = Enumerable.Range(0, steps).Select(static index => $"agent{index}").ToArray();
        var host = new WorkflowTestHost(names);
        var definition = new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Sequential, AgentNames = names };

        (await EndedAsync(host, definition, maxSuperSteps: steps + 1)).ShouldBe(RunEventType.RunCompleted);
        (await EndedAsync(host, definition, maxSuperSteps: steps)).ShouldBe(RunEventType.RunFailed);

        WorkflowDefinitionValidator.StepCountProblem(definition, steps + 1).ShouldBeNull();
        WorkflowDefinitionValidator.StepCountProblem(definition, steps).ShouldNotBeNull();
    }

    [Fact]
    public void The_refusal_names_the_workflow_the_steps_the_limit_and_the_longest_chain_that_fits()
    {
        var definition = new WorkflowDefinition
        {
            Name = "long-chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = [.. Enumerable.Range(0, 100).Select(static index => $"agent{index}")],
        };

        var problem = WorkflowDefinitionValidator.StepCountProblem(definition, maxSuperSteps: 100).ShouldNotBeNull();

        problem.ShouldContain("'long-chain'");
        problem.ShouldContain("100 steps");
        problem.ShouldContain("101 super-steps");
        problem.ShouldContain("Tracon:Workflows:MaxSuperSteps");
        problem.ShouldContain("at most 99 steps");
    }

    [Fact]
    public void The_longest_chain_of_nodes_that_fits_is_the_limit_itself()
    {
        static WorkflowDefinition Chain(int steps) => new()
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [.. Enumerable.Range(0, steps).Select(static index => new WorkflowNodeReference { Name = $"step{index}", Kind = WorkflowNodeKind.Agent })],
        };

        WorkflowDefinitionValidator.StepCountProblem(Chain(100), maxSuperSteps: 100).ShouldBeNull();
        WorkflowDefinitionValidator.StepCountProblem(Chain(101), maxSuperSteps: 100).ShouldNotBeNull().ShouldContain("at most 100 steps");
    }

    [Theory]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.GroupChat)]
    [InlineData(WorkflowKind.Handoff)]
    public void Only_the_sequential_pattern_is_judged_by_the_length_of_its_list(WorkflowKind kind)
    {
        // A list of 500 agents in any other pattern does not take a super-step each.
        var definition = new WorkflowDefinition
        {
            Name = "wide",
            Kind = kind,
            AgentNames = [.. Enumerable.Range(0, 500).Select(static index => $"agent{index}")],
        };

        WorkflowDefinitionValidator.StepCountProblem(definition, maxSuperSteps: 100).ShouldBeNull();
    }

    private static WorkflowDefinition FunctionChain(int steps, out WorkflowTestHost host)
    {
        host = new WorkflowTestHost();

        var nodes = new List<WorkflowNodeReference>();

        for (var index = 0; index < steps; index++)
        {
            var name = $"step{index}";

            host.AddFunction<List<ChatMessage>, List<ChatMessage>>(name, static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));
            nodes.Add(new WorkflowNodeReference { Name = name, Kind = WorkflowNodeKind.Function });
        }

        return new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Sequential, Nodes = nodes };
    }

    private static async Task<RunEventType> EndedAsync(WorkflowTestHost host, WorkflowDefinition definition, int maxSuperSteps)
    {
        await host.SaveAsync(definition);

        var runner = host.CreateRunner(options => options.MaxSuperSteps = maxSuperSteps);
        RunEventType? last = null;

        await foreach (var runEvent in runner.RunStreamingAsync(
            new WorkflowRunRequest { WorkflowName = definition.Name, Message = "hello" },
            TestContext.Current.CancellationToken))
        {
            last = runEvent.Type;
        }

        return last.ShouldNotBeNull();
    }
}
