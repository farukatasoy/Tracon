namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Only a root agent run is an agent turn that continuation can re-send.
/// The rule itself; <c>RunContinuationTests</c> proves it through the reconciler and the handler.
/// </summary>
public sealed class RunContinuationEligibilityTests
{
    [Fact]
    public void A_root_agent_run_may_be_continued()
        => RunContinuationEligibility.Refusal(Run()).ShouldBeNull();

    [Fact]
    public void A_workflow_run_is_refused_and_named_resumable_from_its_checkpoints()
    {
        var run = Run() with { Kind = RunKind.Workflow, WorkflowName = "podcast" };

        var refusal = RunContinuationEligibility.Refusal(run);

        refusal.ShouldNotBeNull();
        refusal.ShouldContain($"POST /api/workflows/runs/{run.Id}/resume", Case.Sensitive);
        refusal.ShouldContain($"GET /api/workflows/runs/{run.Id}/checkpoints", Case.Sensitive);
    }

    [Fact]
    public void A_sub_run_is_refused_and_names_its_root()
    {
        var root = TraconId.NewId();
        var run = Run() with { ParentRunId = TraconId.NewId(), RootRunId = root, Depth = 2 };

        RunContinuationEligibility.Refusal(run).ShouldNotBeNull().ShouldContain($"sub-run of run '{root}'", Case.Sensitive);
    }

    [Fact]
    public void A_run_below_the_root_is_a_sub_run_even_when_its_parent_id_is_missing()
        => RunContinuationEligibility.Refusal(Run() with { Depth = 1 }).ShouldNotBeNull().ShouldContain("sub-run", Case.Sensitive);

    [Fact]
    public void An_eval_run_is_refused()
        => RunContinuationEligibility.Refusal(Run() with { Kind = RunKind.Eval }).ShouldNotBeNull().ShouldContain("'Eval'", Case.Sensitive);

    [Fact]
    public void A_plain_definition_may_be_continued()
        => RunContinuationEligibility.DefinitionRefusal(Definition(), "support", agentVersion: null).ShouldBeNull();

    [Fact]
    public void A_missing_definition_is_refused_and_names_the_pinned_version()
    {
        RunContinuationEligibility.DefinitionRefusal(null, "support", agentVersion: null)
            .ShouldNotBeNull().ShouldContain("Agent 'support' has no persistent definition;", Case.Sensitive);
        RunContinuationEligibility.DefinitionRefusal(null, "support", agentVersion: 3)
            .ShouldNotBeNull().ShouldContain("persistent definition version 3;", Case.Sensitive);
    }

    [Fact]
    public void An_agent_with_skills_or_callable_sub_agents_is_refused()
    {
        RunContinuationEligibility.DefinitionRefusal(Definition() with { SkillNames = ["invoices"] }, "support", null)
            .ShouldNotBeNull().ShouldContain("skills or callable sub-agents", Case.Sensitive);
        RunContinuationEligibility.DefinitionRefusal(Definition() with { CallableAgents = ["researcher"] }, "support", null)
            .ShouldNotBeNull().ShouldContain("skills or callable sub-agents", Case.Sensitive);
    }

    [Fact]
    public void An_agent_with_an_extension_or_a_session_workspace_is_refused()
    {
        RunContinuationEligibility.DefinitionRefusal(Definition() with { ExtensionNames = ["adds-a-tool"] }, "support", null)
            .ShouldNotBeNull().ShouldContain("agent extensions", Case.Sensitive);
        RunContinuationEligibility.DefinitionRefusal(Definition() with { Workspace = new WorkspaceSettings { Enabled = true } }, "support", null)
            .ShouldNotBeNull().ShouldContain("session workspace", Case.Sensitive);
    }

    private static AgentDefinition Definition()
        => new()
        {
            Name = "support",
            Instructions = "Help.",
            Model = new ModelBinding { Provider = "fake", Model = "fake-model" },
        };

    private static RunRecord Run()
        => new()
        {
            Id = TraconId.NewId(),
            AgentName = "support",
            Status = RunStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow,
            SessionId = "session",
            TenantId = "default",
        };
}
