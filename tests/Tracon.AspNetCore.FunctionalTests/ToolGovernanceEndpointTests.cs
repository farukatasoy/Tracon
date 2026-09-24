using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ApprovalEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for tool authorization (F-113) and execution timeout (F-114) driven
/// through a REAL Microsoft Agent Framework tool-calling loop
/// (<c>FunctionInvokingChatClient</c>, via <see cref="FakeModelProvider"/> —
/// <c>ModelProviderRegistry</c> wraps every raw client it returns with
/// <c>UseFunctionInvocation()</c>), not by constructing
/// <c>AuthorizingAIFunction</c>/<c>TimeoutAIFunction</c> directly and calling
/// <c>InvokeAsync</c>. K-218's lesson: an isolated construction proves nothing
/// about the real pipeline.
/// </summary>
public sealed class ToolGovernanceEndpointTests
{
    private const string AgentName = "governance-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

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
        // docs/127, 127.2: unlike an authorization denial (recorded as an
        // ordinary successful result), a rejected call is recorded as
        // ToolFailed - the same conversion TimeoutAIFunction's thrown
        // exception already produces.
        await using var host = await TraconTestHost.StartAsync(
            builder => builder
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
                }),
            configureServices: static services =>
            {
                services.AddSingleton<IToolArgumentsValidator>(new RejectingValidator("The order id is not recognized."));
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
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
            builder => builder
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
                }),
            configureServices: static services =>
            {
                services.AddSingleton<IToolAuthorizationHandler>(new DenyingHandler("Not authorized to cancel orders."));
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "s-denied" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // The denial does NOT fail the run: the model receives the reason as
        // an ordinary tool result and finishes its turn (docs/69, 69.2).
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
                            // the non-cooperative case docs/69 Manual Case 8 covers.
                            await Task.Delay(TimeSpan.FromSeconds(30)); // delay: simulated
                            return "too late";
                        },
                        "slow_report"))
                {
                    Timeout = TimeSpan.FromMilliseconds(300),
                });
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        var stopwatch = Stopwatch.StartNew();

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "fetch the report", SessionId = "s-timeout" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // The timeout does NOT fail the run: the model receives a tool error
        // and finishes its turn. The wait is bounded by the tool's OWN
        // 300ms timeout, not by its real 30-second body.
        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        stopwatch.Stop();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));

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
        // Manual Case 5 (docs/69): ModelProviderCircuitBreaker wraps only the
        // IChatClient pipeline (CircuitBreakingChatClient); a tool exception
        // never reaches it structurally. This proves it BEHAVIORALLY: if a
        // tool timeout were miscounted as a provider failure, the circuit
        // would open after TraconCircuitBreakerOptions.FailureThreshold
        // (5) consecutive failures and turn run #6 into an immediate
        // TraconProviderUnavailableException (RunFailed), not a normal
        // Completed run with its own ToolFailed tool result.
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

                builder.Services.AddSingleton(new TraconToolRegistration(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30)); // delay: simulated
                            return "too late";
                        },
                        "slow_report"))
                {
                    Timeout = TimeSpan.FromMilliseconds(200),
                });
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        // TraconCircuitBreakerOptions.FailureThreshold defaults to 5; six
        // runs is one more than that threshold.
        for (var i = 0; i < 6; i++)
        {
            using var accepted = await PostQueuedAsync(
                host, new AgentRunRequest { Message = "fetch the report", SessionId = $"s-cb-{i}" });
            var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

            // An open circuit would make the run fail instead of completing.
            (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
        }
    }

    [Fact]
    public async Task Approval_wait_is_not_bounded_by_the_tools_own_timeout()
    {
        // Manual Case 6 (docs/69): a Destructive + RequiresApproval tool ALSO
        // carries a short timeout. The timeout must bound only the real
        // execution after approval, never the human wait — ApprovalRequiredAIFunction
        // defers before TimeoutAIFunction's race ever starts (K-368: the
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
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "s-approval-timeout" });
        var originalRunId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        // Wait well past the tool's own 200ms timeout before deciding. The
        // elapsed time IS the premise - no signal can stand in for it.
        await Task.Delay(TimeSpan.FromSeconds(1)); // delay: product

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
