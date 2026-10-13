using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run with no session that stops at a tool approval can never be answered, so it
/// is closed as <see cref="RunStatus.Failed"/>, on every path that starts one.
/// </summary>
/// <remarks>
/// F-389: the queued agent run already had this rule. A batch item and an eval case
/// run without a session too; theirs ended <c>AwaitingApproval</c> with no inbox row
/// to answer, and the batch reported the item <c>Completed</c>. Both run through the
/// real worker, because the status a job reports and the run row are written in two
/// different places.
/// </remarks>
public sealed class SessionlessApprovalTests
{
    private const string AgentName = "refund-agent";
    private const string ModelName = "approval-model";
    private const string Suite = "approval-suite";

    [Fact]
    public async Task A_batch_item_that_stops_at_an_approval_fails_and_its_run_is_closed_as_failed()
    {
        await using var host = await StartAsync();

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/schedules/batch",
                   new JobScheduleSaveRequest
                   {
                       HandlerKey = JobHandlerKeys.AgentBatch,
                       TargetName = AgentName,
                       Payload = JsonDocument.Parse("""["refund order 7"]""").RootElement,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/batch/trigger", new { });
        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await WaitUntil.ValueAsync(
            async () => (await jobs.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the batch job to finish");

        var item = (await jobs.ListItemsAsync(jobId)).ShouldHaveSingleItem();

        item.Status.ShouldBe(JobItemStatus.Failed, "an item whose run waits for an approval nobody can give did not complete");
        item.Error.ShouldNotBeNull().ShouldContain("approval");

        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(item.RunId!.Value)).ShouldNotBeNull();
        run.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task An_eval_case_whose_run_stops_at_an_approval_fails_and_its_run_is_closed_as_failed()
    {
        await using var host = await StartAsync();

        using (var suite = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = AgentName,
                       Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
                   }))
        {
            suite.EnsureSuccessStatusCode();
        }

        using (var cases = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}/cases",
                   new object[] { new { query = "refund order 7" } }))
        {
            cases.EnsureSuccessStatusCode();
        }

        using var triggered = await host.Client.PostAsJsonAsync($"/tracon/api/evals/{Suite}/run", new { });
        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}");

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the eval run to finish");

        var result = detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();

        result.GetProperty("passed").GetBoolean().ShouldBeFalse();
        result.GetProperty("failureReason").GetString().ShouldNotBeNull().ShouldContain("approval");

        var runId = result.GetProperty("runId").GetGuid();
        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
        run.Status.ShouldBe(RunStatus.Failed);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddTool(
                    (Func<string, string>)RefundOrder,
                    name: "refund_order",
                    description: "Refunds an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddModelProvider(new FakeModelProvider("approval-provider")
                    .WithModel(new ModelDescriptor { Name = ModelName })
                    .CallsTool("refund_order", new { orderId = "7" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Refund the order the user names.",
                    Model = new ModelBinding { Provider = "approval-provider", Model = ModelName },
                    ToolNames = ["refund_order"],
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: static services => services.UseScheduling(static options =>
            {
                options.RunWorker = true;
                options.PollInterval = TimeSpan.FromMilliseconds(20);
            }));

    private static string RefundOrder(string orderId) => $"refunded {orderId}";
}
