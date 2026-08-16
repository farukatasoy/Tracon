using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Compiling a graph from a definition. All five patterns must actually build;
/// the Microsoft Agent Framework builder API is unpredictable, and a pattern
/// that fails to build would otherwise only surface at run time.
/// </summary>
public sealed class WorkflowDefinitionCompilerTests
{
    [Fact]
    public async Task Sequential_compiles()
    {
        var host = new WorkflowTestHost("writer", "editor", "reviewer");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor", "reviewer"],
        });

        workflow.Name.ShouldBe("chain");

        // Three agents + an output collector. MAF appends its own collector
        // executor to the end of the prebuilt pattern; that is why the count
        // is greater than the agent count.
        workflow.ReflectExecutors().Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Concurrent_compiles()
    {
        var host = new WorkflowTestHost("writer", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "parallel",
            Kind = WorkflowKind.Concurrent,
            AgentNames = ["writer", "editor"],
        });

        workflow.Name.ShouldBe("parallel");
    }

    [Fact]
    public async Task Handoff_compiles()
    {
        var host = new WorkflowTestHost("support", "expert");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "handoff",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["support", "expert"],
            HandoffInstructions = "Hand off to the expert for technical questions.",
            MaxIterations = 4,
        });

        workflow.Name.ShouldBe("handoff");
    }

    [Fact]
    public async Task GroupChat_compiles()
    {
        var host = new WorkflowTestHost("writer", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "chat",
            Kind = WorkflowKind.GroupChat,
            AgentNames = ["writer", "editor"],
            MaxIterations = 2,
        });

        workflow.Name.ShouldBe("chat");
    }

    [Fact]
    public async Task Magentic_compiles()
    {
        var host = new WorkflowTestHost("manager", "writer", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["writer", "editor"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
        });

        workflow.Name.ShouldBe("magentic");
    }

    [Fact]
    public async Task Compiling_the_same_definition_twice_KEEPS_EXECUTOR_IDS_THE_SAME()
    {
        // 🚨 Resuming from a checkpoint depends on this. Microsoft Agent
        // Framework derives executor ids from the agent INSTANCE; if the ids
        // change on every compile, MAF rejects the checkpoint ("The specified
        // checkpoint is not compatible with the workflow"). Measured
        // (Phase 15): a graph built with fresh agent instances came out incompatible.
        var host = new WorkflowTestHost("writer", "editor");

        var definition = new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        };

        var first = await host.Compiler.CompileAsync(definition);
        var second = await host.Compiler.CompileAsync(definition);

        first.ReflectExecutors().Keys.Order(StringComparer.Ordinal)
            .ShouldBe(second.ReflectExecutors().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Unknown_agent_name_fails_with_a_clear_error()
    {
        var host = new WorkflowTestHost("writer");

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "chain",
                Kind = WorkflowKind.Sequential,
                AgentNames = ["writer", "missing-agent"],
            }));

        exception.Message.ShouldContain("'missing-agent'", Case.Sensitive);
        exception.Message.ShouldContain("no such agent exists in the catalog", Case.Sensitive);
    }

    [Fact]
    public async Task Invalid_definition_is_rejected_before_compiling()
    {
        var host = new WorkflowTestHost("writer");

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "chain",
                Kind = WorkflowKind.Sequential,
                AgentNames = [],
            }));

        exception.Message.ShouldContain("has no agents", Case.Sensitive);
    }
}
