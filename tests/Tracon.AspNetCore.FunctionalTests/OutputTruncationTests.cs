using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-320 — a model response stopped at the output token limit, through a real
/// DI-built host on the non-streaming (buffered) path. The decorator's own
/// rules are covered at unit level in
/// <c>Tracon.Core.UnitTests.Models.FinishReasonDetectingChatClientTests</c>; the
/// run status, error class, repair decision, usage and event are only real here.
/// </summary>
public sealed class OutputTruncationTests
{
    internal const string AgentName = "truncation-agent";

    [Fact]
    public async Task A_structured_response_cut_at_the_limit_fails_the_run_as_output_truncated()
    {
        var provider = new FinishReasonModelProvider()
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 40, outputTokens: 200);

        await using var host = await StartAsync(provider, Structured(maxOutputTokens: 200));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error.Type.ShouldBe(TraconOutputTruncatedException.OutputTruncatedErrorType);
        run.Error.Class.ShouldBe(RunErrorClass.OutputTruncated);
        run.Error.Message.ShouldNotContain("unfinish", Case.Sensitive, "the run record never carries the response text");
        provider.Calls.ShouldBe(1);

        // 🚨 The tokens were spent even though the response never reached the
        // recorder: without the fold the run said it cost nothing.
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(40);
        run.Usage.OutputTokens.ShouldBe(200);
    }

    [Fact]
    public async Task Repair_does_not_run_for_a_truncated_response()
    {
        var provider = new FinishReasonModelProvider()
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 40, outputTokens: 200);

        await using var host = await StartAsync(
            provider,
            Structured(maxOutputTokens: 200),
            services => services.Configure<TraconStructuredResponseOptions>(static options =>
            {
                options.Enabled = true;
                options.MaxRepairAttempts = 2;
            }));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);
        provider.Calls.ShouldBe(1, "a repair turn would hit the same limit, so none runs");
        run.Usage!.OutputTokens.ShouldBe(200, "the usage is counted once, not again by the validator");

        var events = await EventsAsync(host, run.Id);

        events.ShouldNotContain(static e => e.Type == RunEventType.StructuredResponseRepairAttempted);

        var rejected = events.Where(static e => e.Type == RunEventType.StructuredResponseRejected).ShouldHaveSingleItem();
        rejected.Text.ShouldBe("The model stopped at its output token limit before the response was complete.");

        using var payload = JsonDocument.Parse(rejected.Payload!);
        payload.RootElement.GetProperty("repairSkippedTruncated").GetBoolean().ShouldBeTrue();
        payload.RootElement.GetProperty("attempt").GetInt32().ShouldBe(1);
        payload.RootElement.GetProperty("maxAttempts").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task A_repair_turn_cut_at_the_limit_ends_the_run_on_attempt_two_and_keeps_both_turns_usage()
    {
        // Audit finding: the truncation catch on a REPAIR turn had no end-to-end
        // test. The first answer is invalid JSON, so the validator folds its
        // usage and asks again; the repair answer is cut off, so the detector
        // folds that one. Both turns were paid for, and the run says so.
        var provider = new FinishReasonModelProvider()
            .Text("not json at all", ChatFinishReason.Stop, inputTokens: 40, outputTokens: 20)
            .Text("{\"title\": \"An unfinish", ChatFinishReason.Length, inputTokens: 60, outputTokens: 200);

        await using var host = await StartAsync(
            provider,
            Structured(maxOutputTokens: 200),
            services => services.Configure<TraconStructuredResponseOptions>(static options =>
            {
                options.Enabled = true;
                options.MaxRepairAttempts = 2;
            }));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.OutputTruncated);
        provider.Calls.ShouldBe(2, "the third call the budget allows would hit the same limit, so it does not run");
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(100);
        run.Usage.OutputTokens.ShouldBe(220);

        var events = await EventsAsync(host, run.Id);

        events.Where(static e => e.Type == RunEventType.StructuredResponseRepairAttempted).ShouldHaveSingleItem();

        var rejections = events.Where(static e => e.Type == RunEventType.StructuredResponseRejected).ToList();
        rejections.Count.ShouldBe(2);

        using var first = JsonDocument.Parse(rejections[0].Payload!);
        first.RootElement.GetProperty("attempt").GetInt32().ShouldBe(1);
        first.RootElement.GetProperty("repairSkippedTruncated").ValueKind.ShouldBe(JsonValueKind.Null);

        using var last = JsonDocument.Parse(rejections[1].Payload!);
        rejections[1].Text.ShouldBe("The model stopped at its output token limit before the response was complete.");
        last.RootElement.GetProperty("attempt").GetInt32().ShouldBe(2);
        last.RootElement.GetProperty("maxAttempts").GetInt32().ShouldBe(3);
        last.RootElement.GetProperty("repairSkippedTruncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_plain_text_response_cut_at_the_limit_completes_and_records_the_notice()
    {
        var provider = new FinishReasonModelProvider()
            .Text("The first half of a long ans", ChatFinishReason.Length, inputTokens: 30, outputTokens: 50);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        run.Usage!.OutputTokens.ShouldBe(50);

        var notice = (await EventsAsync(host, run.Id))
            .Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal)).ShouldHaveSingleItem();

        notice.Type.ShouldBe(RunEventType.Custom);

        using var payload = JsonDocument.Parse(notice.Payload!);
        payload.RootElement.GetProperty("provider").GetString().ShouldBe("scripted");
        payload.RootElement.GetProperty("maxOutputTokens").GetInt32().ShouldBe(50);
        notice.Payload!.ShouldNotContain("long ans", Case.Sensitive, "the notice never carries the response text");
    }

    [Fact]
    public async Task A_complete_response_writes_no_notice()
    {
        var provider = new FinishReasonModelProvider()
            .Text("A full answer.", ChatFinishReason.Stop, inputTokens: 30, outputTokens: 5);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        (await EventsAsync(host, run.Id)).ShouldNotContain(static e => string.Equals(e.CustomType, RunEventCustomTypes.OutputTruncated, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_filtered_empty_response_still_records_the_tokens_it_spent()
    {
        // The same fold as a truncated response, found while measuring it: the
        // recorder writes no usage for a run that ended in an exception, so a
        // filtered run said it spent nothing although the input was billed.
        var provider = new FinishReasonModelProvider()
            .Text(string.Empty, ChatFinishReason.ContentFilter, inputTokens: 900, outputTokens: 0);

        await using var host = await StartAsync(provider, PlainText(maxOutputTokens: 50));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Error!.Class.ShouldBe(RunErrorClass.ContentFiltered);
        run.Usage.ShouldNotBeNull();
        run.Usage.InputTokens.ShouldBe(900);
    }

    internal static AgentDefinition Structured(int? maxOutputTokens, IReadOnlyList<string>? toolNames = null) => new()
    {
        Name = AgentName,
        DisplayName = "Truncation Agent",
        Description = "An agent used by the output truncation tests.",
        Instructions = "Reply briefly.",
        ToolNames = toolNames ?? [],
        Model = new ModelBinding
        {
            Provider = "scripted",
            Model = "scripted-1",
            MaxOutputTokens = maxOutputTokens,
            ResponseFormat = new AgentResponseFormat
            {
                Kind = AgentResponseFormatKind.JsonSchema,
                Schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
                SchemaName = "answer",
            },
        },
        Origin = AgentDefinitionOrigin.Code,
    };

    internal static AgentDefinition PlainText(int? maxOutputTokens, IReadOnlyList<string>? toolNames = null)
        => Structured(maxOutputTokens, toolNames) with
        {
            Model = Structured(maxOutputTokens).Model with { ResponseFormat = null },
        };

    internal static Task<TraconTestHost> StartAsync(
        FinishReasonModelProvider provider,
        AgentDefinition agent,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider).AddAgent(agent);
                configureTracon?.Invoke(builder);
            },
            configureServices: configureServices);

    /// <summary>
    /// The non-streaming branch: a request carrying <c>Idempotency-Key</c> runs
    /// with one JSON response instead of SSE (Phase 43).
    /// </summary>
    internal static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The streaming branch: the run's own SSE stream, read to the end.</summary>
    internal static async Task<List<SseFrame>> PostStreamingAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hello" }, TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await SseReader.ReadAllAsync(stream);
    }

    internal static async Task<RunRecord> SingleRunAsync(TraconTestHost host)
    {
        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        var run = runs.ShouldHaveSingleItem();

        return await host.WaitForTerminalRunAsync(run.Id);
    }

    internal static async Task<List<RunEvent>> EventsAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var frames = await SseReader.ReadAllAsync(stream);

        return [.. frames
            .Where(static frame => frame.Data.StartsWith('{'))
            .Select(static frame => JsonSerializer.Deserialize<RunEvent>(frame.Data, JsonOptions))
            .OfType<RunEvent>()];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
}
