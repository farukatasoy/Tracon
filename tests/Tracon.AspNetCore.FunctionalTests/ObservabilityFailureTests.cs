using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A sink that records what a run did, and throws, changes nothing the caller or the record can see.
/// </summary>
/// <remarks>
/// <para>
/// The run records to places that are not the run: a consumer's <see cref="IRunEventSink"/>, the trace store that
/// keeps the spans, the webhook store and the job queue behind a webhook. <c>StoreFailureMatrixTests</c> fails one call
/// of a store once and looks at the status code. This class asks more of the same promise: the sink throws every time,
/// in each way a sink can throw (before it returns a task, after it awaited, with a cancellation of its own), at the
/// first event, in the middle and at the last one, and the run is compared with the same run against a healthy sink,
/// field by field: the answer, the status, the tokens, the events the store holds and the frames of the stream.
/// </para>
/// <para>
/// The run is the echo of one message, so the baseline is exact: nothing in it varies between two runs but the ids
/// and the clock, and those are left out of the comparison.
/// </para>
/// </remarks>
public sealed class ObservabilityFailureTests
{
    private const string AgentName = "observed-agent";
    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    /// <summary>The ways a sink can throw, and the events it throws at.</summary>
    public static TheoryData<SinkFault, SinkTrigger, bool> Sinks()
    {
        var data = new TheoryData<SinkFault, SinkTrigger, bool>();

        foreach (var fault in Enum.GetValues<SinkFault>())
        {
            foreach (var trigger in Enum.GetValues<SinkTrigger>())
            {
                data.Add(fault, trigger, false);
                data.Add(fault, trigger, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sinks))]
    public async Task A_run_event_sink_that_throws_changes_neither_the_answer_nor_the_record(SinkFault fault, SinkTrigger trigger, bool streamed)
    {
        var baseline = await ObserveAsync(streamed, configure: null);

        var sink = new ThrowingSink(fault, trigger);
        var observed = await ObserveAsync(streamed, builder => builder.Services.AddSingleton<IRunEventSink>(sink));

        sink.Thrown.ShouldBeGreaterThan(0, "the sink never threw, so the test proved nothing");
        observed.ShouldBe(baseline, $"{fault} at {trigger}, {(streamed ? "streamed" : "buffered")}");
    }

    public static TheoryData<string, bool> FailingStores() => new()
    {
        { "trace store throws before it writes", false },
        { "trace store throws before it writes", true },
        { "trace store throws after it wrote", false },
        { "trace store throws after it wrote", true },
        { "trace store is cancelled", false },
        { "trace store is cancelled", true },
        { "webhook lookup throws", false },
        { "webhook lookup throws", true },
        { "webhook delivery cannot be created", false },
        { "webhook delivery cannot be created", true },
        { "webhook job cannot be queued", false },
        { "webhook job cannot be queued", true },
    };

    [Theory]
    [MemberData(nameof(FailingStores))]
    public async Task A_store_that_only_observes_the_run_and_fails_changes_neither_the_answer_nor_the_record(string failure, bool streamed)
    {
        var baseline = await ObserveAsync(streamed, configure: null, withWebhook: true);

        var hits = 0;
        var fault = new Func<Exception>(() =>
        {
            Interlocked.Increment(ref hits);

            return string.Equals(failure, "trace store is cancelled", StringComparison.Ordinal)
                ? new OperationCanceledException("a cancellation nobody asked for")
                : new InjectedDbException();
        });

        var observed = await ObserveAsync(
            streamed,
            builder =>
            {
                switch (failure)
                {
                    case "trace store throws before it writes":
                    case "trace store is cancelled":
                        builder.Services.ThrowFrom<ITraceStore>(nameof(ITraceStore.WriteSpansAsync), fault);
                        break;
                    case "trace store throws after it wrote":
                        builder.Services.ThrowAfter<ITraceStore>(nameof(ITraceStore.WriteSpansAsync), fault);
                        break;
                    case "webhook lookup throws":
                        builder.Services.ThrowFrom<IWebhookStore>(nameof(IWebhookStore.FindForEventAsync), fault);
                        break;
                    case "webhook delivery cannot be created":
                        builder.Services.ThrowFrom<IWebhookStore>(nameof(IWebhookStore.CreateDeliveryAsync), fault);
                        break;
                    default:
                        builder.Services.ThrowFrom<IJobStore>(nameof(IJobStore.EnqueueAsync), fault);
                        break;
                }
            },
            withWebhook: true,
            afterRun: () => hits);

        hits.ShouldBeGreaterThan(0, $"{failure}: the store was never asked, so the test proved nothing");
        observed.ShouldBe(baseline, $"{failure}, {(streamed ? "streamed" : "buffered")}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tool_that_puts_a_value_on_its_span_that_cannot_be_written_down_does_not_fail_the_run(bool streamed)
    {
        var tag = new UnwritableTag();
        var unwritable = await RunWithTaggingToolAsync(streamed, tag);
        var plain = await RunWithTaggingToolAsync(streamed, "plain");

        tag.Reads.ShouldBeGreaterThan(0, "nothing tried to write the value down, so the test proved nothing");
        unwritable.ShouldBe(plain);
    }

    private static async Task<(HttpStatusCode Status, RunStatus Run, string? Error, long EventCount)> RunWithTaggingToolAsync(bool streamed, object tag)
    {
        var model = new Tracon.Testing.FakeModelProvider("tagging-model").EchoesLastToolResult();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(model);
                builder.AddTool(
                    (Func<string>)(() =>
                    {
                        System.Diagnostics.Activity.Current?.SetTag("tool.note", tag);

                        return "tagged";
                    }),
                    name: "tagging_tool",
                    description: "Tags its own span.");
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Use the tool.",
                    Model = new ModelBinding { Provider = "tagging-model", Model = "m" },
                    ToolNames = ["tagging_tool"],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1.0));

        model.CallsTool("tagging_tool");

        HttpStatusCode status;

        if (streamed)
        {
            using var response = await host.Client.PostAsJsonAsync(
                RunUri, new AgentRunRequest { Message = "go" }, TestContext.Current.CancellationToken);

            status = response.StatusCode;
            await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            _ = await SseReader.ReadAllAsync(stream);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
            {
                Content = JsonContent.Create(new AgentRunRequest { Message = "go" }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

            using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            status = response.StatusCode;
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);
        var run = await host.WaitForTerminalRunAsync(runs.ShouldHaveSingleItem().Id);

        return (status, run.Status, run.Error?.Message, run.EventCount);
    }

    /// <summary>
    /// Runs the agent once on a fresh host and returns everything a caller or an operator can see of the run.
    /// </summary>
    private static async Task<Observation> ObserveAsync(
        bool streamed,
        Action<ITraconBuilder>? configure,
        bool withWebhook = false,
        Func<int>? afterRun = null)
    {
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Reply briefly.",
                    Model = TestData.Model(),
                    Origin = AgentDefinitionOrigin.Code,
                });
                configure?.Invoke(builder);
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1.0));

        if (withWebhook)
        {
            using var saved = await host.Client.PutAsJsonAsync(
                new Uri("/tracon/api/webhooks/observer", UriKind.Relative),
                new { url = "https://example.com/hook", events = new[] { "run.completed" }, enabled = true },
                TestContext.Current.CancellationToken);

            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        string answer;
        HttpStatusCode status;
        var frames = string.Empty;

        if (streamed)
        {
            using var response = await host.Client.PostAsJsonAsync(
                RunUri, new AgentRunRequest { Message = "hello" }, TestContext.Current.CancellationToken);

            status = response.StatusCode;
            await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var read = await SseReader.ReadAllAsync(stream);

            frames = string.Join('\n', read.Select(static frame => $"{frame.Event}"));
            answer = string.Join('\n', read.Select(static frame => frame.Data));
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
            {
                Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

            using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            status = response.StatusCode;
            answer = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);
        var run = await host.WaitForTerminalRunAsync(runs.ShouldHaveSingleItem().Id);

        // The store and the sinks are written after the response on some paths. A store that is meant to be asked
        // (the injected one) is waited for; the rest of the record is read once the run is terminal.
        if (afterRun is not null)
        {
            await WaitUntil.TrueAsync(() => afterRun() > 0, "the failing store to be asked");
        }

        var events = new List<RunEvent>();

        await foreach (var stored in host.Services.GetRequiredService<IRunStore>()
            .ReadEventsAsync(run.Id, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(stored);
        }

        run = await host.Client.GetFromJsonAsync<RunRecord>(
            new Uri($"/tracon/api/runs/{run.Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        return new Observation(
            status,
            Normalize(answer, run!.Id),
            frames,
            run.Status,
            run.Error?.Message,
            run.Usage?.InputTokens,
            run.Usage?.OutputTokens,
            run.EventCount,
            string.Join('\n', events.Select(static e => string.Create(CultureInfo.InvariantCulture, $"{e.Sequence}|{e.Type}|{e.Text}"))));
    }

    /// <summary>Takes the ids and the clock out of a body, so two runs of the same request compare equal.</summary>
    private static string Normalize(string body, Guid runId)
    {
        var text = body.Replace(runId.ToString(), "<run>", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (JsonNode.Parse(text) is JsonObject json)
            {
                json.Remove("runId");

                return json.ToJsonString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // A stream: the frames are compared as text.
        }

        return text;
    }

    public enum SinkFault
    {
        /// <summary>The call throws before it returns a task.</summary>
        ThrowsBeforeReturning,

        /// <summary>The call returns a task that fails after the sink awaited.</summary>
        ThrowsAfterAwaiting,

        /// <summary>The call throws a cancellation that is not the caller's.</summary>
        ThrowsOperationCanceled,
    }

    public enum SinkTrigger
    {
        /// <summary>Every event.</summary>
        EveryEvent,

        /// <summary>The first event of the run only.</summary>
        FirstEvent,

        /// <summary>The event that closes the run only.</summary>
        LastEvent,
    }

    private sealed record Observation(
        HttpStatusCode Status,
        string Answer,
        string Frames,
        RunStatus Run,
        string? Error,
        long? InputTokens,
        long? OutputTokens,
        long EventCount,
        string Events);

    private sealed class ThrowingSink(SinkFault fault, SinkTrigger trigger) : IRunEventSink
    {
        private int _thrown;

        public int Thrown => Volatile.Read(ref _thrown);

        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            var throws = trigger switch
            {
                SinkTrigger.EveryEvent => true,
                SinkTrigger.FirstEvent => runEvent.Type == RunEventType.RunStarted,
                _ => runEvent.Type is RunEventType.RunCompleted or RunEventType.RunFailed,
            };

            if (!throws)
            {
                return ValueTask.CompletedTask;
            }

            Interlocked.Increment(ref _thrown);

            return fault switch
            {
                SinkFault.ThrowsBeforeReturning => throw new InvalidOperationException("the sink broke"),
                SinkFault.ThrowsAfterAwaiting => FailAfterAwaitingAsync(),
                _ => throw new OperationCanceledException("a cancellation nobody asked for"),
            };
        }

        private static async ValueTask FailAfterAwaitingAsync()
        {
            await Task.Yield();

            throw new InvalidOperationException("the sink broke after it awaited");
        }
    }

    private sealed class UnwritableTag
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public override string ToString()
        {
            Interlocked.Increment(ref _reads);

            throw new InvalidOperationException("this value cannot be turned into text");
        }
    }

    private sealed class InjectedDbException : DbException
    {
        public InjectedDbException()
            : base("injected")
        {
        }
    }
}
