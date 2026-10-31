using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// A tool that waits for approval is an AG-UI interrupt, and the answer is the next request's <c>resume</c>.
/// </summary>
/// <remarks>
/// The decision is the one the management API takes: audited BEFORE it is applied, remembered the same way
/// And carried by the next turn of the session. What differs is the surface it is audited under.
/// </remarks>
public sealed class AGUIApprovalTests
{
    private static readonly AGUITool[] NoTools = [];

    private static async Task<(AGUITestHost Host, StrongBox<int> Executions, AGUIResponse First)> StartPendingAsync(
        string thread,
        Action<IServiceCollection>? configureServices = null,
        bool callsTwice = false,
        StrongBox<FakeModelProvider?>? model = null)
    {
        var executions = new StrongBox<int>();
        var host = await AGUITestHost.StartAsync(
            builder =>
            {
                var scripted = TestAgents.AddApprovalTool(builder, executions, callsTwice: callsTwice);

                model?.Value = scripted;
            },
            configureServices);

        var first = await host.RunAsync(TestAgents.Approval, AGUIInputs.User(thread, "cancel the order", "r1"));

        return (host, executions, first);
    }

    private static Task<AGUIResponse> ResumeAsync(AGUITestHost host, string thread, string run, params AGUIResume[] resume)
        => host.RunAsync(TestAgents.Approval, AGUIInputs.With(thread, run, [], NoTools, resume));

    [Fact]
    public async Task A_tool_that_waits_for_approval_ends_the_run_with_an_interrupt()
    {
        var (host, executions, first) = await StartPendingAsync("t-pending");
        await using var _ = host;

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);

        var interrupt = first.Interrupts.ShouldHaveSingleItem();
        interrupt.Reason.ShouldBe(InterruptReasons.ToolCall);
        interrupt.Id.ShouldNotBeNullOrEmpty();
        interrupt.Message!.ShouldContain(TestAgents.CancelOrder, Case.Sensitive);
        interrupt.ResponseSchema!.Value.GetProperty("required")[0].GetString().ShouldBe("approved");

        // The call the interrupt is about is in the stream, so the front end can show it.
        var start = first.Events.OfType<ToolCallStartEvent>().Single();
        start.ToolCallName.ShouldBe(TestAgents.CancelOrder);
        interrupt.ToolCallId.ShouldBe(start.ToolCallId);

        executions.Value.ShouldBe(0, "the tool has not run: it waits for approval");

        // Tracon's own record says the same as the protocol: the run waits.
        var record = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(first.TraconRunId, TestContext.Current.CancellationToken);
        record!.Status.ShouldBe(RunStatus.AwaitingApproval);
    }

    [Fact]
    public async Task An_approval_runs_the_tool_and_the_audit_row_names_the_surface()
    {
        var (host, executions, first) = await StartPendingAsync("t-approve");
        await using var _ = host;

        var second = await ResumeAsync(host, "t-approve", "r2", AGUIInputs.Resolved(first.Interrupts[0].Id!, new { approved = true }));

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();
        second.Text.ShouldContain("ORD-7 canceled.", Case.Sensitive);
        executions.Value.ShouldBe(1);

        var audit = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Action = "approval.decision" }, TestContext.Current.CancellationToken);

        var row = audit.ShouldHaveSingleItem();
        row.Entity.ShouldBe("tool:" + TestAgents.CancelOrder);

        var after = JsonDocument.Parse(row.After!).RootElement;
        after.GetProperty("approved").GetBoolean().ShouldBeTrue();
        after.GetProperty("surface").GetString().ShouldBe("ag-ui");
    }

    [Fact]
    public async Task A_refusal_and_a_dismissal_keep_the_tool_from_running_and_the_reason_reaches_the_model()
    {
        var model = new StrongBox<FakeModelProvider?>();
        var (host, executions, first) = await StartPendingAsync("t-refuse", model: model);
        await using var _ = host;

        var refused = await ResumeAsync(
            host,
            "t-refuse",
            "r2",
            AGUIInputs.Resolved(first.Interrupts[0].Id!, new { approved = false, reason = "not today" }));

        refused.Status.ShouldBe(HttpStatusCode.OK, refused.Body);
        executions.Value.ShouldBe(0);

        // The reason the user gave is what the model reads in place of the tool's result.
        var seenByModel = string.Join(
            '\n',
            model.Value!.Requests[^1].Messages
                .SelectMany(static message => message.Contents)
                .Select(static content => content switch
                {
                    FunctionResultContent result => result.Result?.ToString(),
                    TextContent text => text.Text,
                    _ => null,
                }));
        seenByModel.ShouldContain("not today", Case.Sensitive);

        var (host2, executions2, first2) = await StartPendingAsync("t-dismiss");
        await using var __ = host2;

        var dismissed = await ResumeAsync(host2, "t-dismiss", "r2", AGUIInputs.Cancelled(first2.Interrupts[0].Id!));

        dismissed.Status.ShouldBe(HttpStatusCode.OK, dismissed.Body);
        executions2.Value.ShouldBe(0, "a dismissed interrupt is a refusal, never an approval");

        var audit = await host2.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Action = "approval.decision" }, TestContext.Current.CancellationToken);
        JsonDocument.Parse(audit.ShouldHaveSingleItem().After!).RootElement.GetProperty("approved").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Remember_writes_the_same_standing_rule_the_management_API_writes()
    {
        var (host, executions, first) = await StartPendingAsync("t-remember", callsTwice: true);
        await using var _ = host;

        await ResumeAsync(
            host,
            "t-remember",
            "r2",
            AGUIInputs.Resolved(first.Interrupts[0].Id!, new { approved = true, remember = true }));

        executions.Value.ShouldBe(1);

        var rules = await host.Services.GetRequiredService<IToolApprovalRuleStore>()
            .ListAsync("default", TestContext.Current.CancellationToken);

        var rule = rules.ShouldHaveSingleItem();
        rule.AgentName.ShouldBe(TestAgents.Approval);
        rule.ToolName.ShouldBe(TestAgents.CancelOrder);

        // The next call to the tool is approved without asking.
        var third = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-remember-2", "cancel the order again", "r3"));

        third.Interrupts.ShouldBeEmpty();
        executions.Value.ShouldBe(2);
    }

    [Fact]
    public async Task Remember_bound_to_the_arguments_does_not_approve_a_call_with_other_arguments()
    {
        // 🚨 The rule the caller asked for covers THIS call, not the tool. Reading "rememberArgumentsOnly" loosely once
        // would have saved a rule for every call of the tool; the test pins the narrow rule from the outside: a second
        // call with other arguments still asks.
        var (host, executions, first) = await StartPendingAsync("t-narrow", callsTwice: true);
        await using var _ = host;

        await ResumeAsync(
            host,
            "t-narrow",
            "r2",
            AGUIInputs.Resolved(first.Interrupts[0].Id!, new { approved = true, remember = true, rememberArgumentsOnly = true }));

        executions.Value.ShouldBe(1);

        var rule = (await host.Services.GetRequiredService<IToolApprovalRuleStore>()
            .ListAsync("default", TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        rule.ArgumentsHash.ShouldNotBeNullOrEmpty();

        var other = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-narrow-2", "cancel another order", "r3"));

        other.Interrupts.ShouldHaveSingleItem("the second call has other arguments, so the rule does not cover it");
        executions.Value.ShouldBe(1);
    }

    [Fact]
    public async Task An_interrupt_that_does_not_exist_or_was_answered_already_is_a_400()
    {
        var (host, executions, first) = await StartPendingAsync("t-unknown");
        await using var _ = host;

        var unknown = await ResumeAsync(host, "t-unknown", "r2", AGUIInputs.Resolved("not-an-interrupt", new { approved = true }));

        unknown.Status.ShouldBe(HttpStatusCode.BadRequest, unknown.Body);
        unknown.Problem.GetProperty("title").GetString().ShouldBe("Invalid resume");

        var id = first.Interrupts[0].Id!;

        (await ResumeAsync(host, "t-unknown", "r3", AGUIInputs.Resolved(id, new { approved = true }))).Status.ShouldBe(HttpStatusCode.OK);

        var again = await ResumeAsync(host, "t-unknown", "r4", AGUIInputs.Resolved(id, new { approved = true }));
        again.Status.ShouldBe(HttpStatusCode.BadRequest, "an answered interrupt is no longer pending");
        executions.Value.ShouldBe(1, "the tool ran once");
    }

    [Theory]
    [InlineData("{\"reason\":\"no verdict\"}")]
    [InlineData("{\"approved\":\"yes\"}")]
    [InlineData("{\"approved\":true,\"rememberArgumentsOnly\":\"true\"}")]
    [InlineData("{\"approved\":true,\"remember\":1}")]
    [InlineData("{\"approved\":true,\"reason\":5}")]
    [InlineData("\"approved\"")]
    public async Task An_answer_without_a_boolean_verdict_is_a_400(string payload)
    {
        var (host, executions, first) = await StartPendingAsync("t-verdict-" + Math.Abs(payload.GetHashCode(StringComparison.Ordinal)));
        await using var _ = host;

        var thread = "t-verdict-" + Math.Abs(payload.GetHashCode(StringComparison.Ordinal));

        var resume = new AGUIResume
        {
            InterruptId = first.Interrupts[0].Id,
            Status = ResumeStatus.Resolved,
            Payload = JsonDocument.Parse(payload).RootElement.Clone(),
        };

        var response = await ResumeAsync(host, thread, "r2", resume);

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        executions.Value.ShouldBe(0, "no verdict is not an approval");
    }

    [Fact]
    public async Task A_decision_that_cannot_be_audited_is_not_applied()
    {
        // The audit row comes BEFORE the decision, and a row that cannot be written refuses the decision.
        var audit = new SwitchableAuditLog();
        var (host, executions, first) = await StartPendingAsync(
            "t-audit",
            services => services.AddSingleton<IAuditLog>(audit));
        await using var _ = host;

        audit.Fail = true;

        var refused = await ResumeAsync(host, "t-audit", "r2", AGUIInputs.Resolved(first.Interrupts[0].Id!, new { approved = true }));

        refused.Status.ShouldNotBe(HttpStatusCode.OK, refused.Body);
        refused.Body.ShouldContain("was not applied because it could not be written to the audit trail", Case.Sensitive);
        executions.Value.ShouldBe(0);
        audit.Written.ShouldBeEmpty();

        // The interrupt is still pending, so the same decision goes through once the audit store recovers.
        audit.Fail = false;

        var retried = await ResumeAsync(host, "t-audit", "r3", AGUIInputs.Resolved(first.Interrupts[0].Id!, new { approved = true }));

        retried.Status.ShouldBe(HttpStatusCode.OK, retried.Body);
        executions.Value.ShouldBe(1);
        audit.Written.ShouldHaveSingleItem();
    }

    private sealed class SwitchableAuditLog : IAuditLog
    {
        private readonly List<AuditEntry> _written = [];
        private readonly Lock _gate = new();

        public bool Fail { get; set; }

        public IReadOnlyList<AuditEntry> Written
        {
            get
            {
                lock (_gate)
                {
                    return [.. _written];
                }
            }
        }

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("audit store is unreachable");
            }

            lock (_gate)
            {
                _written.Add(entry);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AuditEntry>>(Written);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
