using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.MixedCallRig;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A server tool that needs an approval stays gated when the model asks for it next to a client-side tool, and the client
/// call reaches the caller as an ordinary call once the approval is given.
/// </summary>
/// <remarks>
/// The strict server behind the agent answers 400 for a call without a result, so a history that held the approval-gated
/// call or the client call unanswered would fail every one of these turns.
/// </remarks>
public sealed class ClientToolMixedCallApprovalTests
{
    private static readonly MixedCallRigOptions WithApproval = new() { WithApprovalTool = true };

    private static AgentRunRequest NewMessage() => new() { SessionId = SessionId, Message = "cancel order 7 and open the page" };

    private static AgentRunRequest Approve(string requestId, bool approved = true)
        => new() { SessionId = SessionId, Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = approved }] };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_sibling_that_needs_approval_does_not_run_before_the_approval_and_the_client_call_follows_it(bool streaming)
    {
        await using var rig = await MixedCallRig.StartAsync(WithApproval);
        rig.Server
            .ThenCalls((ApprovalCallId, ApprovalTool, "{}"), (ClientCallId, ClientTool, "{}"))
            .ThenCalls(ClientCallOnly)
            .ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage(), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Contents.ShouldContain(static content => content.StartsWith("toolApprovalRequest(", StringComparison.Ordinal), "the call waits for a decision");
        first.Contents.ShouldNotContain(static content => content.StartsWith("functionCall(", StringComparison.Ordinal), "the client call is asked for again after the approval");
        rig.Probe.ApprovalRuns.ShouldBe(0, "a call that needs approval must not run before it is given");

        var second = await rig.RunAsync(Approve(first.ApprovalRequestId), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Probe.ApprovalRuns.ShouldBe(1);
        second.Contents.ShouldContain($"functionCall({ClientCallId}:{ClientTool})", StringComparer.Ordinal);

        var third = await rig.RunAsync(
            new AgentRunRequest { SessionId = SessionId, ToolResults = [new ClientToolResult { CallId = ClientCallId, Result = "page-title" }] },
            streaming);

        third.Status.ShouldBe(HttpStatusCode.OK, third.Body);
        third.Text.ShouldBe("final answer");
        rig.Server.Rejections.ShouldBeEmpty("a call without a result reached the provider");
    }

    [Fact]
    public async Task A_refused_approval_ends_the_gated_call_without_running_it_and_the_model_may_still_ask_for_the_client_call()
    {
        await using var rig = await MixedCallRig.StartAsync(WithApproval);
        rig.Server
            .ThenCalls((ApprovalCallId, ApprovalTool, "{}"), (ClientCallId, ClientTool, "{}"))
            .ThenCalls(ClientCallOnly)
            .ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage(), streaming: false);
        var second = await rig.RunAsync(Approve(first.ApprovalRequestId, approved: false), streaming: false);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Probe.ApprovalRuns.ShouldBe(0, "a refused call must not run");
        rig.Server.Rejections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_server_call_an_approval_gated_call_and_a_client_call_in_one_answer(bool streaming)
    {
        await using var rig = await MixedCallRig.StartAsync(WithApproval);
        rig.Server
            .ThenCalls((BackendCallId, BackendTool, "{}"), (ApprovalCallId, ApprovalTool, "{}"), (ClientCallId, ClientTool, "{}"))
            .ThenCalls(ClientCallOnly)
            .ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage(), streaming);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        rig.Probe.ApprovalRuns.ShouldBe(0);

        var second = await rig.RunAsync(Approve(first.ApprovalRequestId), streaming);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        rig.Probe.ApprovalRuns.ShouldBe(1);
        rig.Probe.BackendRuns.ShouldBe(1, "the ungated server call ran once, whichever turn it ran in");
        second.Contents.ShouldContain($"functionCall({ClientCallId}:{ClientTool})", StringComparer.Ordinal);
        rig.Server.Rejections.ShouldBeEmpty();
    }
}
