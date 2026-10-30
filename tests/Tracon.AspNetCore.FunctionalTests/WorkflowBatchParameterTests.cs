using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow batch item carries its own parameter values.
/// </summary>
/// <remarks>
/// The workflow handler turned every item into a bare message, so a
/// scheduled workflow could only ever run a parameterized workflow on its
/// defaults, and a workflow with a required parameter could not be scheduled at
/// all. An item that names a document must fail, not run without it: a workflow
/// run takes no documents.
/// </remarks>
public sealed class WorkflowBatchParameterTests
{
    [Fact]
    public async Task A_structured_item_runs_the_workflow_with_its_values_and_a_bad_item_fails_alone()
    {
        var provider = WorkflowParameterBindingTests.Provider();

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            static _ => { });

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        var (status, items) = await RunBatchAsync(
            host,
            """
            [
              {"message":"one","parameters":{"tone":"formal"}},
              {"message":"two"},
              {"message":"three","parameters":{"tone":"casual"}}
            ]
            """);

        status.ShouldBe(JobStatus.Completed);

        // Item two supplied no 'tone', which the workflow requires: it fails
        // alone, and item three still ran.
        items.Select(static item => item.Status).ShouldBe([JobItemStatus.Completed, JobItemStatus.Failed, JobItemStatus.Completed]);
        items[1].Error.ShouldNotBeNull().ShouldContain("tone", Case.Sensitive);

        var writerCalls = provider.Requests
            .Select(static request => request.Options!.Instructions)
            .Where(static text => text!.StartsWith("Write the script", StringComparison.Ordinal))
            .ToList();

        writerCalls.ShouldBe(["Write the script in a formal tone.", "Write the script in a casual tone."]);
    }

    [Fact]
    public async Task An_item_that_names_a_document_fails_instead_of_running_without_it()
    {
        var provider = WorkflowParameterBindingTests.Provider();

        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        var (_, items) = await RunBatchAsync(
            host,
            """[{"message":"one","parameters":{"tone":"formal"},"documents":[{"name":"a.txt","content":"A"}]}]""");

        var item = items.ShouldHaveSingleItem();

        item.Status.ShouldBe(JobItemStatus.Failed);
        item.Error.ShouldNotBeNull().ShouldContain("does not accept documents", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Plain_items_still_run_the_workflow_with_the_message_alone()
    {
        var provider = WorkflowParameterBindingTests.Provider();

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            static builder => builder.AddAgent(WorkflowParameterBindingTests.Definition("plain", "Be plain.")));

        // A workflow that takes no parameters, over an agent that takes none.
        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/podcast",
                   new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = ["plain"] }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var (status, items) = await RunBatchAsync(host, """["Episode 12"]""");

        status.ShouldBe(JobStatus.Completed);
        items.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
        provider.Requests.Select(static request => request.Options!.Instructions).ShouldContain("Be plain.", StringComparer.Ordinal);
    }

    private static async Task<(JobStatus Status, IReadOnlyList<JobItemRecord> Items)> RunBatchAsync(
        TraconTestHost host,
        string payload)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/schedules/wf-batch",
                   new JobScheduleSaveRequest
                   {
                       HandlerKey = JobHandlerKeys.Workflow,
                       TargetName = "podcast",
                       Payload = JsonDocument.Parse(payload).RootElement,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/wf-batch/trigger", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
        var store = host.Services.GetRequiredService<IJobStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            async () => (await store.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the workflow batch job to finish");

        return (job.Status, [.. (await store.ListItemsAsync(jobId)).OrderBy(static item => item.Seq)]);
    }
}
