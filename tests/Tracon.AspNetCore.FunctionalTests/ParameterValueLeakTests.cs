using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A parameter value is user content: it reaches the model's instructions and the
/// protected <c>run_inputs</c> column, and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-294: a value is as sensitive as a message, and unlike a message it also
/// travels through error paths (a refused name, an over-long value, a failed
/// step). The cases scan every sink a run writes - the host's logs, the run
/// events of the whole tree, the span tags, a refusal body and a job item's error
/// - for a sentinel that only the caller knew.
/// </para>
/// </remarks>
public sealed class ParameterValueLeakTests
{
    private const string Sentinel = "SENTINEL-VALUE-7731-xq";

    [Fact]
    public async Task A_workflow_run_with_a_value_leaves_it_in_no_log_event_or_span()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        var tags = new List<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.StartsWith("Tracon", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (tags)
                {
                    tags.AddRange(activity.TagObjects.Select(static tag => $"{tag.Key}={tag.Value}"));
                    tags.AddRange(activity.Events.SelectMany(static activityEvent => activityEvent.Tags.Select(static tag => $"{tag.Key}={tag.Value}")));
                    tags.Add(activity.DisplayName);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);

        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", Sentinel)),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        // The model DID get it: that is what a value is for.
        provider.Requests.Select(static request => request.Options!.Instructions)
            .ShouldContain($"Write the script in a {Sentinel} tone.", StringComparer.Ordinal);

        host.Logs.AllText.ShouldNotContain(Sentinel, Case.Sensitive);

        var store = host.Services.GetRequiredService<IRunStore>();
        var tree = await store.QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = false });

        foreach (var row in tree)
        {
            (row.Error?.Message ?? string.Empty).ShouldNotContain(Sentinel, Case.Sensitive);

            await foreach (var runEvent in store.ReadEventsAsync(row.Id))
            {
                (runEvent.Text ?? string.Empty).ShouldNotContain(Sentinel, Case.Sensitive);
                (runEvent.Payload ?? string.Empty).ShouldNotContain(Sentinel, Case.Sensitive);
            }
        }

        lock (tags)
        {
            tags.ShouldNotContain(tag => tag.Contains(Sentinel, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_refusal_names_the_parameter_and_never_echoes_its_value()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        using var workflowRefusal = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/podcast/run",
            new WorkflowRunHttpRequest
            {
                Message = "Episode 12",
                Parameters = WorkflowParameterBindingTests.Values(("tone", "formal"), ("tonne", Sentinel)),
            });

        workflowRefusal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await workflowRefusal.Content.ReadAsStringAsync();

        body.ShouldContain("tonne", Case.Sensitive);
        body.ShouldNotContain(Sentinel, Case.Sensitive);

        // The runner's own refusal (a consumer calling it directly) is just as quiet.
        var runner = host.Services.GetRequiredService<IWorkflowRunner>();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "podcast",
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal"), ("tonne", Sentinel)),
        }))
        {
            // Drain.
        }

        var failed = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
            .Single(static row => row.Status == RunStatus.Failed);

        failed.Error.ShouldNotBeNull().Message.ShouldContain("tonne", Case.Sensitive);
        failed.Error.Message.ShouldNotContain(Sentinel, Case.Sensitive);
        host.Logs.AllText.ShouldNotContain(Sentinel, Case.Sensitive);
    }

    [Fact]
    public async Task A_batch_item_error_never_carries_a_value_or_a_document_body()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddAgent(WorkflowParameterBindingTests.Definition(
                    "writer",
                    "Write in a {{tone}} tone.",
                    WorkflowParameterBindingTests.Tone(required: true))),
            configureServices: services => services.UseScheduling(options =>
            {
                options.RunWorker = true;
                options.PollInterval = TimeSpan.FromMilliseconds(20);
            }));

        var payload = System.Text.Json.JsonDocument.Parse(
            "[" +
            "{\"message\":\"a\",\"parameters\":{\"tonne\":\"" + Sentinel + "\"}}," +
            "{\"message\":\"b\",\"parameters\":{\"tone\":\"formal\"},\"documents\":[{\"name\":\"n\",\"content\":\"" + Sentinel + "\"},7]}" +
            "]").RootElement;

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/schedules/leak",
            new JobScheduleSaveRequest { HandlerKey = JobHandlerKeys.AgentBatch, TargetName = "writer", Payload = payload });

        // The shape check refuses the second item before any job exists, and its
        // text names the position, not the content.
        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await saved.Content.ReadAsStringAsync()).ShouldNotContain(Sentinel, Case.Sensitive);

        var firstOnly = System.Text.Json.JsonDocument.Parse(
            "[{\"message\":\"a\",\"parameters\":{\"tonne\":\"" + Sentinel + "\"}}]").RootElement;

        using var ok = await host.Client.PutAsJsonAsync(
            "/tracon/api/schedules/leak",
            new JobScheduleSaveRequest { HandlerKey = JobHandlerKeys.AgentBatch, TargetName = "writer", Payload = firstOnly });

        ok.EnsureSuccessStatusCode();

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/leak/trigger", new { });
        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await WaitUntil.ValueAsync(
            async () => (await jobs.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the batch job to finish");

        var item = (await jobs.ListItemsAsync(jobId)).ShouldHaveSingleItem();

        item.Status.ShouldBe(JobItemStatus.Failed);
        item.Error.ShouldNotBeNull().ShouldContain("tonne", Case.Sensitive);
        item.Error.ShouldNotContain(Sentinel, Case.Sensitive);
        host.Logs.AllText.ShouldNotContain(Sentinel, Case.Sensitive);
    }
}
