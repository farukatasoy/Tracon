using System.Text.Json;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// A script the tree's budget refuses is audited like every other refusal of a script (F-690), and the refusal keeps
/// the identity of every other budget refusal (F-530).
/// </summary>
/// <remarks>
/// The run scope is set in the body of each test: an ambient value written in a helper does not flow back to its
/// caller.
/// </remarks>
public sealed partial class SandboxedSkillScriptRunnerTests
{
    [Fact]
    public async Task A_spent_budget_stops_a_script_and_the_refusal_is_audited_as_script_denied_with_the_budget_sentence()
    {
        var skill = Demo(EchoScript());
        var log = new InMemoryAuditLog();
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));
        var spent = SpentBudget();

        using var runner = CreateExecutorRunner(log, await GrantAllAsync(skill), executor);

        TraconRunContext.SetCurrent(Scope(spent));

        try
        {
            var refused = await Should.ThrowAsync<TraconRunBudgetExceededException>(
                async () => await runner.RunStoredScriptAsync(skill, "echo", arguments: null, CancellationToken.None));

            // The identity is the one every budget refusal has: nothing about the exception changed.
            refused.ErrorType.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
            refused.Message.ShouldBe(spent.DescribeToolStartExhaustion());
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        executor.Requests.ShouldBeEmpty("the executor was asked to run a script of a spent tree");

        var entries = await log.QueryAsync(new AuditQuery { TenantId = "default" });
        var denied = entries.Single(static entry => string.Equals(entry.Action, "script.denied", StringComparison.Ordinal));

        denied.Entity.ShouldBe("demo/echo");

        // Written to a jsonb column like every denial: the reason is a JSON string, and it is the budget sentence.
        JsonSerializer.Deserialize<string>(denied.After!).ShouldBe(spent.DescribeToolStartExhaustion());
        entries.ShouldNotContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_spent_budget_leaves_its_reason_on_the_span_like_any_other_refusal()
    {
        var skill = Demo(EchoScript() with { Name = "echo-budget-span" });
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<System.Diagnostics.Activity>();

        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };

        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var runner = CreateRunner(new InMemoryAuditLog(), await GrantAllAsync(skill));

        TraconRunContext.SetCurrent(Scope(SpentBudget()));

        try
        {
            await Should.ThrowAsync<TraconRunBudgetExceededException>(
                async () => await runner.RunStoredScriptAsync(skill, "echo-budget-span", arguments: null, CancellationToken.None));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var span = SpanFor(stopped, "echo-budget-span");

        span.Status.ShouldBe(System.Diagnostics.ActivityStatusCode.Error);
        span.GetTagItem(TraconDiagnostics.Tags.ScriptDenialReason).ShouldBeOfType<string>().ShouldContain("budget is exhausted");
    }

    [Fact]
    public async Task A_spent_budget_still_stops_the_script_when_the_denial_cannot_be_audited()
    {
        // The audit entry of a refusal is best-effort: the script does not start either way, and the run is not
        // broken by a store that is down. The caller still gets the budget exception, not an audit one.
        var skill = Demo(EchoScript());
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));

        using var runner = CreateExecutorRunner(new ThrowingAuditLog(), await GrantAllAsync(skill), executor);

        TraconRunContext.SetCurrent(Scope(SpentBudget()));

        try
        {
            var refused = await Should.ThrowAsync<TraconRunBudgetExceededException>(
                async () => await runner.RunStoredScriptAsync(skill, "echo", arguments: null, CancellationToken.None));

            refused.ErrorType.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        executor.Requests.ShouldBeEmpty("the executor was asked to run a script after its refusal could not be audited");
    }

    [Fact]
    public async Task A_budget_that_is_not_spent_writes_no_denial()
    {
        // The control: the same run with room left in the budget runs, and the trail holds script.run, not script.denied.
        var skill = Demo(EchoScript());
        var log = new InMemoryAuditLog();
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0, "ran")));

        using var runner = CreateExecutorRunner(log, await GrantAllAsync(skill), executor);

        TraconRunContext.SetCurrent(Scope(new AgentRunBudget { MaxTotalCost = 1m }));

        try
        {
            var output = await runner.RunStoredScriptAsync(skill, "echo", arguments: null, CancellationToken.None);

            output.ShouldBeOfType<string>().ShouldContain("ran");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var entries = await log.QueryAsync(new AuditQuery { TenantId = "default" });

        entries.ShouldContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));
        entries.ShouldNotContain(static entry => string.Equals(entry.Action, "script.denied", StringComparison.Ordinal));
    }

    private static AgentRunBudget SpentBudget()
    {
        var spent = new AgentRunBudget { MaxTotalCost = 1m };
        spent.RecordUsage(0, 5m);

        return spent;
    }

    private static AgentRunScope Scope(AgentRunBudget budget)
        => new() { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), Budget = budget };
}
