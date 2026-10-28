using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Batch items carry their own parameter values and documents.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-294: a batch bound every item to the agent's defaults, and an agent with
/// a required parameter and no default failed the WHOLE job. A structured item
/// now names its values; an invalid item fails alone and the rest run, while a
/// batch where no item carries a value keeps the F-330 rule (the job falls once,
/// up front). The cases run a real job through the real worker, because the item
/// text is parsed in two places (the schedule endpoint and the handler) that
/// must agree.
/// </para>
/// </remarks>
public sealed class AgentBatchParameterTests
{
    private const string Required = "billing-agent";
    private const string DefaultsOnly = "greeting-agent";
    private const string ModelName = "batch-model";

    [Fact]
    public async Task A_structured_item_binds_its_values_a_plain_item_binds_the_defaults_and_a_bad_item_fails_alone()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var job = await RunBatchAsync(
            host,
            DefaultsOnly,
            [
                "plain text",
                """{"message":"structured","parameters":{"customer":"Acme"}}""",
                """{"message":"bad","parameters":{"custmer":"typo"}}""",
                """{"message":"later"}""",
            ]);

        var items = job.Items;

        items.Select(static item => item.Status).ShouldBe(
            [JobItemStatus.Completed, JobItemStatus.Completed, JobItemStatus.Failed, JobItemStatus.Completed]);
        items[2].Error.ShouldNotBeNull().ShouldContain("custmer", Case.Sensitive);

        provider.Requests.Select(static request => request.Options!.Instructions).ShouldBe(
            [
                "Hello friend, how can I help?",
                "Hello Acme, how can I help?",
                "Hello friend, how can I help?",
            ]);

        // Each ran item's run recorded the final values it was bound with.
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        (await inputs.GetAsync(tenant, items[1].RunId!.Value))!.Parameters!["customer"].ShouldBe("Acme");
        (await inputs.GetAsync(tenant, items[0].RunId!.Value))!.Parameters!["customer"].ShouldBe("friend");
    }

    [Fact]
    public async Task A_batch_where_no_item_carries_a_value_keeps_the_f330_rule_and_fails_the_job_once()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var job = await RunBatchAsync(host, Required, ["one", "two"]);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldContain("'customer'", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_batch_with_one_valued_item_does_not_fall_for_the_item_that_forgot()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var job = await RunBatchAsync(
            host,
            Required,
            [
                """{"message":"has it","parameters":{"customer":"Acme"}}""",
                "forgot",
            ]);

        job.Items.Select(static item => item.Status).ShouldBe([JobItemStatus.Completed, JobItemStatus.Failed]);
        job.Items[1].Error.ShouldNotBeNull().ShouldContain("customer", Case.Sensitive);
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_item_with_documents_sends_them_before_the_message()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var job = await RunBatchAsync(
            host,
            DefaultsOnly,
            ["""{"message":"summarise","documents":[{"name":"brief.txt","content":"Ship on Friday."}]}"""]);

        job.Items.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);

        var texts = provider.Requests[^1].Messages.Select(static message => message.Text).ToList();

        texts.Count.ShouldBe(2);
        texts[0].ShouldContain("brief.txt", Case.Sensitive);
        texts[1].ShouldBe("summarise");

        var types = new List<RunEventType>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(job.Items[0].RunId!.Value))
        {
            types.Add(runEvent.Type);
        }

        types.ShouldContain(RunEventType.DocumentAttached);
    }

    [Fact]
    public async Task A_malformed_structured_item_is_refused_when_the_schedule_is_saved_and_when_it_is_triggered()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/schedules/bad-save",
            Schedule(DefaultsOnly, """["fine", {"message":"x","parameters":"nope"}]"""));

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(saved)).GetProperty("detail").GetString()
            .ShouldNotBeNull().ShouldContain("Item 1", Case.Sensitive);

        using var ok = await host.Client.PutAsJsonAsync("/tracon/api/schedules/good", Schedule(DefaultsOnly, """["fine"]"""));

        ok.EnsureSuccessStatusCode();

        // The trigger can override the payload; the same check runs there.
        using var triggered = await host.Client.PostAsJsonAsync(
            "/tracon/api/schedules/good/trigger",
            new { payload = JsonDocument.Parse("""[{"message":"x","documents":[{"name":"a"}]}]""").RootElement });

        triggered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_consumers_own_handler_keeps_its_item_text_whatever_it_looks_like()
    {
        // Only the two built-in batch handlers read the structured shape. A
        // consumer's handler owns its items; a JSON-looking item is none of our business.
        var provider = Provider();
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddJobHandler<NoopHandler>("app.custom");
                builder.UseScheduling(options =>
                {
                    options.RunWorker = false;
                    options.HttpSchedulableHandlerKeys.Add("app.custom");
                });
            });

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/schedules/custom",
            new JobScheduleSaveRequest
            {
                HandlerKey = "app.custom",
                TargetName = "anything",
                Payload = JsonDocument.Parse("""[{"message":"x","parameters":"not-an-object"}]""").RootElement,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static JobScheduleSaveRequest Schedule(string agent, string payload) => new()
    {
        HandlerKey = JobHandlerKeys.AgentBatch,
        TargetName = agent,
        Payload = JsonDocument.Parse(payload).RootElement,
    };

    private static AgentDefinition Definition(string name, params AgentParameter[] parameters) => new()
    {
        Name = name,
        Instructions = "Hello {{customer}}, how can I help?",
        Parameters = parameters,
        Model = new ModelBinding { Provider = "batch-provider", Model = ModelName },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider("batch-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(provider)
                .AddAgent(Definition(Required, new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }))
                .AddAgent(Definition(DefaultsOnly, new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" }));
                builder.UseScheduling(options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                });
            });

    /// <summary>Saves a schedule, triggers it and waits for the job to finish.</summary>
    private static async Task<BatchOutcome> RunBatchAsync(TraconTestHost host, string agent, string[] items)
    {
        var payload = "[" + string.Join(",", items.Select(static item => item.StartsWith('{') ? item : JsonSerializer.Serialize(item))) + "]";

        using (var saved = await host.Client.PutAsJsonAsync("/tracon/api/schedules/batch", Schedule(agent, payload)))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/batch/trigger", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
        var store = host.Services.GetRequiredService<IJobStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            async () => (await store.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the batch job to finish");

        return new BatchOutcome(job.Status, job.ErrorMessage, [.. (await store.ListItemsAsync(jobId)).OrderBy(static item => item.Seq)]);
    }

    private sealed class NoopHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed record BatchOutcome(JobStatus Status, string? ErrorMessage, IReadOnlyList<JobItemRecord> Items);
}
