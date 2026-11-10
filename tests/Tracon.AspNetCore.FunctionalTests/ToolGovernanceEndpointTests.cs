using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
// Same rationale as ApprovalEndpointTests.cs: the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for tool authorization and execution timeout driven
/// through a REAL Microsoft Agent Framework tool-calling loop
/// (<c>FunctionInvokingChatClient</c>, via <see cref="FakeModelProvider"/> —
/// <c>ModelProviderRegistry</c> wraps every raw client it returns with
/// <c>UseFunctionInvocation()</c>), not by constructing
/// <c>AuthorizingAIFunction</c>/<c>TimeoutAIFunction</c> directly and calling
/// <c>InvokeAsync</c>. the lesson: an isolated construction proves nothing
/// about the real pipeline.
/// </summary>
public sealed class ToolGovernanceEndpointTests
{
    private const string AgentName = "governance-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    /// <summary>A pause the host's own loops make (the 20 ms job poll) stays on the system's timers; a tool timeout does not.</summary>
    private static readonly TimeSpan HostLoops = TimeSpan.FromMilliseconds(100);

    /// <summary>A host clock that moves only when a test moves it, except for the host's own short loops.</summary>
    private static ManualClock HostClock() => new() { SystemTimersWithin = HostLoops };

    private static async Task<HttpResponseMessage> PostQueuedAsync(TraconTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private sealed class DenyingHandler(string reason) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Deny(reason));
    }

    private sealed class RejectingValidator(string reason) : IToolArgumentsValidator
    {
        public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
            ToolDescriptor tool, Microsoft.Extensions.AI.AIFunctionArguments arguments, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolArgumentsValidationResult.Invalid(reason));
    }

    [Fact]
    public async Task Rejected_arguments_complete_the_run_and_are_recorded_as_ToolFailed()
    {
        // Unlike an authorization denial (recorded as an
        // ordinary successful result), a rejected call is recorded as
        // ToolFailed - the same conversion TimeoutAIFunction's thrown
        // exception already produces.
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(new FakeModelProvider("gov-model")
                    .CallsTool("cancel_order", new { orderId = "ORD-8" })
                    .EchoesLastToolResult())
                .AddTool(
                    (Func<string, string>)(orderId => $"{orderId} canceled."),
                    name: "cancel_order")
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "gov-model", Model = "gov-1" },
                    ToolNames = ["cancel_order"],
                });
                builder.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: static services =>
            {
                services.AddSingleton<IToolArgumentsValidator>(new RejectingValidator("The order id is not recognized."));
            });

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "s-rejected" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // The rejection does NOT fail the run: the model receives the reason
        // as its tool result and finishes its turn, same as a denial.
        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        var record = invocations.ShouldNotBeNull().ShouldHaveSingleItem();

        record.Succeeded.ShouldBeFalse();
        record.Error.ShouldBe("The order id is not recognized.");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var events = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        var toolFailed = events.Where(static item => item.Type == RunEventType.ToolFailed).ShouldHaveSingleItem();

        // 🚨 Security axis: the rejected call's TEXT must never carry the
        // argument value - only the validator's own safe reason.
        toolFailed.Text.ShouldBe("The order id is not recognized.");
        toolFailed.Text.ShouldNotBeNull();
        toolFailed.Text.ShouldNotContain("ORD-8");
    }

    [Fact]
    public async Task Denied_call_completes_the_run_and_marks_the_record_denied()
    {
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(new FakeModelProvider("gov-model")
                    .CallsTool("cancel_order", new { orderId = "ORD-7" })
                    .EchoesLastToolResult())
                .AddTool(
                    (Func<string, string>)(orderId => $"{orderId} canceled."),
                    name: "cancel_order")
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "gov-model", Model = "gov-1" },
                    ToolNames = ["cancel_order"],
                });
                builder.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: static services =>
            {
                services.AddSingleton<IToolAuthorizationHandler>(new DenyingHandler("Not authorized to cancel orders."));
            });

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "s-denied" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // The denial does NOT fail the run: the model receives the reason as
        // an ordinary tool result and finishes its turn.
        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        var record = invocations.ShouldNotBeNull().ShouldHaveSingleItem();

        record.AuthorizationDenied.ShouldBeTrue();
        record.TimedOut.ShouldBeFalse();
        record.Error.ShouldBeNull();
        record.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Timed_out_call_completes_the_run_well_before_the_real_body_and_marks_the_record()
    {
        var clock = HostClock();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider("gov-model")
                        .CallsTool("slow_report", new { reportId = "R-1" })
                        .EchoesLastToolResult())
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "gov-model", Model = "gov-1" },
                        ToolNames = ["slow_report"],
                    });

                // A registration with a real timeout cannot go through the
                // AddTool(...) convenience overload (it has no timeout
                // parameter); the registration is built directly, the same
                // way AddTool itself does internally.
                builder.Services.AddSingleton(new TraconToolRegistration(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        async () =>
                        {
                            // Deliberately does NOT accept a CancellationToken:
                            // the non-cooperative case, where only the registration's own timeout can end the call.
                            await Task.Delay(Timeout.InfiniteTimeSpan); // delay: simulated
                            return "too late";
                        },
                        "slow_report"))
                {
                    Timeout = TimeSpan.FromMilliseconds(300),
                });
                builder.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "fetch the report", SessionId = "s-timeout" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // The timeout does NOT fail the run: the model receives a tool error
        // and finishes its turn. The wait is bounded by the tool's OWN
        // 300ms timeout: its body never ends, so a run that waited for it never completes.
        var completed = host.WaitForRunStatusAsync(runId, "Completed");

        await clock.AdvanceUntilAsync(completed, within: TimeSpan.FromMilliseconds(300));

        clock.Steps.ShouldBe([TimeSpan.FromMilliseconds(300)], "the run went on at the tool's own timeout and not at another timer");
        (await completed).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        var record = invocations.ShouldNotBeNull().ShouldHaveSingleItem();

        record.TimedOut.ShouldBeTrue();
        record.AuthorizationDenied.ShouldBeFalse();
        record.Succeeded.ShouldBeFalse();
        record.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Repeated_tool_timeouts_never_open_the_providers_circuit_breaker()
    {
        const int Runs = 6;

        var clock = HostClock();

        // ModelProviderCircuitBreaker wraps only the
        // IChatClient pipeline (CircuitBreakingChatClient); a tool exception
        // never reaches it structurally. This proves it BEHAVIORALLY: if a
        // tool timeout were miscounted as a provider failure, the circuit
        // would open after TraconCircuitBreakerOptions.FailureThreshold
        // (5) consecutive failures and turn run #6 into an immediate
        // TraconProviderUnavailableException (RunFailed), not a normal
        // Completed run with its own ToolFailed tool result.
        //
        // The script is consumed once, in order, and then the fallback answers every turn: one call-and-answer pair
        // for each run, or only the first run would call the tool and the rest would not time out at all.
        var model = new FakeModelProvider("gov-model");

        for (var i = 0; i < Runs; i++)
        {
            model.CallsTool("slow_report", new { reportId = "R-1" }).RespondsWith("The report is late.");
        }

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(model)
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "gov-model", Model = "gov-1" },
                        ToolNames = ["slow_report"],
                    });

                builder.Services.AddSingleton(new TraconToolRegistration(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        async () =>
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan); // delay: simulated
                            return "too late";
                        },
                        "slow_report"))
                {
                    Timeout = TimeSpan.FromMilliseconds(200),
                });
                builder.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        // TraconCircuitBreakerOptions.FailureThreshold defaults to 5; six
        // runs is one more than that threshold.
        for (var i = 0; i < Runs; i++)
        {
            using var accepted = await PostQueuedAsync(
                host, new AgentRunRequest { Message = "fetch the report", SessionId = $"s-cb-{i}" });
            var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

            // An open circuit would make the run fail instead of completing.
            var completed = host.WaitForRunStatusAsync(runId, "Completed");

            await clock.AdvanceUntilAsync(completed, within: TimeSpan.FromMilliseconds(200));

            (await completed).ShouldBe("Completed");
        }

        clock.Steps.ShouldBe(Enumerable.Repeat(TimeSpan.FromMilliseconds(200), Runs), "every run was cut by the tool's own timeout, once");
    }

    [Fact]
    public async Task Approval_wait_is_not_bounded_by_the_tools_own_timeout()
    {
        var clock = HostClock();

        // A Destructive + RequiresApproval tool ALSO
        // carries a short timeout. The timeout must bound only the real
        // execution after approval, never the human wait — ApprovalRequiredAIFunction
        // defers before TimeoutAIFunction's race ever starts (the
        // decision resumes as a NEW run, never inside the pending call).
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider("gov-model")
                        .CallsTool("cancel_order", new { orderId = "ORD-9" })
                        .EchoesLastToolResult())
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "gov-model", Model = "gov-1" },
                        ToolNames = ["cancel_order"],
                    });

                builder.Services.AddSingleton(new TraconToolRegistration(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        (string orderId) => $"{orderId} canceled.",
                        "cancel_order"))
                {
                    RequiresApproval = true,
                    Timeout = TimeSpan.FromMilliseconds(200),
                });
                builder.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "s-approval-timeout" });
        var originalRunId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        // Wait well past the tool's own 200ms timeout before deciding. The time that passes IS the premise:
        // no timer is armed for the tool while the person decides, and the clock is moved past the limit.
        clock.Pending.ShouldAllBe(
            static due => due > TimeSpan.FromMilliseconds(200),
            "a timer at or inside the tool's limit was already armed while the call waited for a decision");
        clock.Advance(TimeSpan.FromSeconds(1));

        using var pendingResponse = await host.Client.GetAsync(new Uri("/tracon/api/approvals/pending", UriKind.Relative));
        var approvalId = (await TraconTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        using var decideResponse = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        decideResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        using var runningJobs = await host.Client.GetAsync(new Uri("/tracon/api/runs?sessionId=s-approval-timeout", UriKind.Relative));
        var resumedRunId = (await TraconTestHost.ReadJsonAsync(runningJobs))
            .EnumerateArray()
            .Select(static run => run.GetProperty("id").GetGuid())
            .Single(id => id != originalRunId);

        // Approved AFTER the 200ms window elapsed: if timeout wrapped the
        // approval wait, this run would carry a ToolTimeout error instead.
        (await host.WaitForRunStatusAsync(resumedRunId, "Completed")).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{resumedRunId}/tools", UriKind.Relative));

        var record = invocations.ShouldNotBeNull().ShouldHaveSingleItem();

        record.TimedOut.ShouldBeFalse();
        record.Succeeded.ShouldBeTrue();
    }
}
