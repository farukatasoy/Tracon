using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// An approval that a run applied before its front end left cannot be applied a second time.
/// </summary>
/// <remarks>
/// <para>
/// A run saves its session when it ends. The approval a request answers is applied when the run starts and is pending in the
/// stored history until then; a front end that closes the stream while the approved tool runs ends the run as cancelled and
/// the session is not saved, so the same approval, sent again, found the call pending and ran the tool a second time.
/// The request that applies an answer now writes its identity into the session before the run starts, and a request that
/// answers it again is refused as the answer to an interrupt that is no longer pending.
/// </para>
/// <para>
/// MAF closes an unanswered request itself on the next run of the session (it is rejected, not run), so a user who writes a
/// new message after the disconnect goes on in the same thread.
/// </para>
/// </remarks>
public sealed class AGUIApprovalDisconnectTests
{
    private const string Thread = "t-disconnect";

    [Fact]
    public async Task An_approval_whose_front_end_left_while_the_tool_ran_is_refused_when_it_is_sent_again_and_the_tool_does_not_run_twice()
    {
        var scenario = new BlockingTool();
        await using var host = await StartAsync(scenario);

        var first = await host.RunAsync(TestAgents.Approval, AGUIInputs.User(Thread, "cancel the order", "r1"));
        var interrupt = first.Interrupts.ShouldHaveSingleItem().Id!;

        scenario.Executions.ShouldBe(0, "the tool waits for the approval");

        // The front end answers and then goes away while the approved tool is running.
        using (var request = ResumeRequest("r2", interrupt))
        {
            var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

            await scenario.Started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            response.Dispose();
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = Thread }, TestContext.Current.CancellationToken))
                .Any(static run => run.Status == RunStatus.Canceled),
            "the run of the front end that left to be recorded as canceled");

        scenario.Executions.ShouldBe(1);

        var again = await host.RunAsync(TestAgents.Approval, AGUIInputs.With(Thread, "r3", [], [], [AGUIInputs.Resolved(interrupt, new { approved = true })]));

        again.Status.ShouldBe(HttpStatusCode.BadRequest, again.Body);
        again.Problem.GetProperty("title").GetString().ShouldBe("Invalid resume");
        scenario.Executions.ShouldBe(1, "an approval is applied once, whatever became of the run that applied it");
    }

    [Fact]
    public async Task A_new_message_after_the_front_end_left_goes_on_in_the_thread_and_does_not_run_the_tool()
    {
        var scenario = new BlockingTool();
        await using var host = await StartAsync(scenario);

        var first = await host.RunAsync(TestAgents.Approval, AGUIInputs.User(Thread, "cancel the order", "r1"));
        var interrupt = first.Interrupts.ShouldHaveSingleItem().Id!;

        using (var request = ResumeRequest("r2", interrupt))
        {
            var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

            await scenario.Started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            response.Dispose();
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = Thread }, TestContext.Current.CancellationToken))
                .Any(static run => run.Status == RunStatus.Canceled),
            "the run of the front end that left to be recorded as canceled");

        var next = await host.RunAsync(TestAgents.Approval, AGUIInputs.User(Thread, "never mind, what is the status?", "r3"));

        next.Status.ShouldBe(HttpStatusCode.OK, next.Body);
        next.Interrupts.ShouldBeEmpty("the call that nobody answered is closed by the framework, not asked about again");
        scenario.Executions.ShouldBe(1, "the unanswered request was rejected, so the tool did not run for it");
    }

    private static Task<AGUITestHost> StartAsync(BlockingTool scenario)
        => AGUITestHost.StartAsync(builder =>
        {
            var model = new FakeModelProvider("approval-model").CallsTool(TestAgents.CancelOrder, new { orderId = "ORD-7" });

            model.EchoesLastToolResult();

            builder
                .AddModelProvider(model)
                .AddTool(
                    (Func<string, CancellationToken, Task<string>>)scenario.RunAsync,
                    name: TestAgents.CancelOrder,
                    description: "Cancels an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddAgent(TestAgents.Definition(TestAgents.Approval, "approval-model", TestAgents.CancelOrder));
        });

    private static HttpRequestMessage ResumeRequest(string run, string interruptId)
    {
        var body = JsonSerializer.Serialize(
            AGUIInputs.With(Thread, run, [], [], [AGUIInputs.Resolved(interruptId, new { approved = true })]),
            AGUIJsonSerializerContext.Default.RunAgentInput);

        return new HttpRequestMessage(HttpMethod.Post, new Uri($"{AGUITestHost.Pattern}/{TestAgents.Approval}", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
    }

    /// <summary>A tool whose first run starts and waits until its run is cancelled; a later run answers at once.</summary>
    private sealed class BlockingTool
    {
        private int _executions;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Executions => Volatile.Read(ref _executions);

        public async Task<string> RunAsync(string orderId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _executions) == 1)
            {
                Started.TrySetResult();

                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            return $"{orderId} canceled.";
        }
    }
}
