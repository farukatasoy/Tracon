using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.MixedCallRig;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The server tool of a mixed answer runs through the same checks as any other call: authorization, argument validation,
/// the output limit, the content guard and the run budget.
/// </summary>
/// <remarks>
/// The ring that sets the client call aside only changes which calls the loop sees; the tool wrapper chain is around each
/// tool and the guard is below the loop. These tests show that none of them was stepped around.
/// </remarks>
public sealed class ClientToolMixedCallGuardTests
{
    private static AgentRunRequest NewMessage() => new() { SessionId = SessionId, Message = "check order 7 and open the page" };

    [Fact]
    public async Task A_server_tool_the_authorization_handler_refuses_does_not_run_in_a_mixed_answer()
    {
        await using var rig = await MixedCallRig.StartAsync(new MixedCallRigOptions
        {
            Services = static services => services.AddSingleton<IToolAuthorizationHandler>(new DenyingHandler()),
        });

        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage(), streaming: false);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.Contents.ShouldContain($"functionCall({ClientCallId}:{ClientTool})", StringComparer.Ordinal);
        rig.Probe.BackendRuns.ShouldBe(0, "a refused call must not run");
        rig.Server.Rejections.ShouldBeEmpty("the refused call still gets a result");

        var run = (await rig.RunsAsync()).ShouldHaveSingleItem();
        var backend = (await rig.ToolRowsAsync(run.Id)).Single(static row => string.Equals(row.ToolName, BackendTool, StringComparison.Ordinal));

        backend.AuthorizationDenied.ShouldBeTrue();
    }

    [Fact]
    public async Task A_server_tool_the_argument_validator_refuses_does_not_run_in_a_mixed_answer()
    {
        await using var rig = await MixedCallRig.StartAsync(new MixedCallRigOptions
        {
            Services = static services => services.AddSingleton<IToolArgumentsValidator>(new RejectingValidator()),
        });

        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage(), streaming: false);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        rig.Probe.BackendRuns.ShouldBe(0, "a rejected call must not run");
        rig.Server.Rejections.ShouldBeEmpty();
        rig.Server.Transcripts[1].ShouldContain($"tool({BackendCallId})", Case.Sensitive);
    }

    [Fact]
    public async Task The_output_limit_cuts_down_the_result_of_the_server_tool_before_the_model_reads_it()
    {
        await using var rig = await MixedCallRig.StartAsync(new MixedCallRigOptions
        {
            BackendResult = new string('x', 400),
            BackendMaxOutputBytes = 64,
        });

        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        await rig.RunAsync(NewMessage(), streaming: false);

        rig.Probe.BackendRuns.ShouldBe(1);
        ToolMessageOf(rig.Server.Bodies[1], BackendCallId).ShouldNotContain(new string('x', 65), Case.Sensitive);
    }

    [Fact]
    public async Task The_content_guard_stops_a_result_of_the_server_tool_that_it_blocks_before_the_model_reads_it()
    {
        await using var rig = await MixedCallRig.StartAsync(new MixedCallRigOptions
        {
            BackendResult = "LEAK-MARKER " + new string('x', 40),
            Services = static services => services.AddSingleton<IContentGuard>(new MarkerGuard("LEAK-MARKER")),
        });

        rig.Server.ThenCalls(BackendAndClientCalls).ThenCalls(ClientCallOnly).ThenAnswers("final answer");

        var first = await rig.RunAsync(NewMessage(), streaming: false);

        first.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, first.Body);
        rig.Probe.BackendRuns.ShouldBe(1, "the tool ran; what it said is what the guard stops");
        rig.Server.Bodies.Count.ShouldBe(1, "the request that would have carried the result was never sent");
        rig.Server.Bodies.ShouldAllBe(static body => !body.ToJsonString().Contains("LEAK-MARKER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_run_budget_stops_the_second_server_tool_after_the_first_one_spent_the_ceiling()
    {
        await using var rig = await MixedCallRig.StartAsync(new MixedCallRigOptions
        {
            BackendSpendsTokens = 1_000,
            ConfigureOptions = static options => options.AgentGraph.MaxTotalTokens = 500,
        });

        rig.Server.ThenCalls(
            (BackendCallId, BackendTool, "{}"),
            (SecondBackendCallId, SecondBackendTool, "{}"),
            (ClientCallId, ClientTool, "{}"));

        await rig.RunAsync(NewMessage(), streaming: false);

        rig.Probe.BackendRuns.ShouldBe(1);
        rig.Probe.SecondBackendRuns.ShouldBe(0, "the budget was spent before the second tool started");

        var run = (await rig.RunsAsync()).ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
    }

    /// <summary>The text of the <c>tool</c> message that answers a call, as the provider received it.</summary>
    private static string ToolMessageOf(System.Text.Json.Nodes.JsonObject body, string callId)
    {
        var message = body["messages"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Single(message => string.Equals(message["role"]?.GetValue<string>(), "tool", StringComparison.Ordinal) &&
                               string.Equals(message["tool_call_id"]?.GetValue<string>(), callId, StringComparison.Ordinal));

        return message["content"]!.ToString();
    }

    private sealed class DenyingHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny("Not authorized to look orders up."));
    }

    private sealed class RejectingValidator : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolArgumentsValidationResult.Invalid("The order id is not recognized."));
    }

    private sealed class MarkerGuard(string marker) : IContentGuard
    {
        public string Name => "marker";

        public ValueTask<ContentGuardResult> InspectAsync(ContentGuardContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                context.Text.Contains(marker, StringComparison.Ordinal)
                    ? ContentGuardResult.Block("marker", "marker found")
                    : ContentGuardResult.Allow);
    }
}
