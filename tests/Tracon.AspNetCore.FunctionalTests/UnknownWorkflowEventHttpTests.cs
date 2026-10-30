using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A <c>WorkflowEvent</c> of the consumer's own, raised by a function node, is in
/// the run record that <c>GET /api/runs/{id}/events</c> serves, through a real DI-built host. The
/// runner's own behavior is covered in <c>Tracon.Workflows.UnitTests</c>; the options binding and the
/// HTTP read model are only real here.
/// </summary>
public sealed class UnknownWorkflowEventHttpTests
{
    [Fact]
    public async Task A_function_nodes_own_event_is_served_from_the_events_endpoint()
    {
        await using var host = await StartAsync(RaiseOrderShipped);

        await SaveAsync(host, NodeUsageHost.Workflow, Function("raise"));

        var runId = await RunAsync(host);
        var notice = NoticesOf(await OutputTruncationTests.EventsAsync(host, runId)).ShouldHaveSingleItem();

        notice.Type.ShouldBe(RunEventType.Custom);
        notice.Text.ShouldBe(nameof(OrderShippedEvent));

        using var payload = JsonDocument.Parse(notice.Payload!);
        payload.RootElement.GetProperty("eventType").GetString().ShouldBe(typeof(OrderShippedEvent).FullName);
        payload.RootElement.GetProperty("data").GetString().ShouldBe("order-7");

        (await RecordAsync(host, runId)).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task The_workflow_run_stream_sends_the_event_as_an_event_frame()
    {
        // Unlike an agent run, whose live stream mirrors a closed list of notices, a workflow run
        // streams every event it records.
        await using var host = await StartAsync(RaiseOrderShipped);

        await SaveAsync(host, NodeUsageHost.Workflow, Function("raise"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{NodeUsageHost.Workflow}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "go" }),
        };
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        frames.ShouldContain(static frame =>
            string.Equals(frame.Event, "event", StringComparison.Ordinal) &&
            frame.Data.Contains(RunEventCustomTypes.WorkflowEvent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_payloads_off_the_event_stays_and_its_payload_is_null()
    {
        await using var host = await StartAsync(
            RaiseOrderShipped,
            configureOptions: static options => options.RunRecording.RecordToolPayloads = false);

        await SaveAsync(host, NodeUsageHost.Workflow, Function("raise"));

        var runId = await RunAsync(host);
        var notice = NoticesOf(await OutputTruncationTests.EventsAsync(host, runId)).ShouldHaveSingleItem();

        notice.Text.ShouldBe(nameof(OrderShippedEvent));
        notice.Payload.ShouldBeNull();
    }

    private static void RaiseOrderShipped(ITraconBuilder builder)
        => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            "raise",
            _ => async (messages, context, cancellationToken) =>
            {
                await context.AddEventAsync(new OrderShippedEvent("order-7"), cancellationToken).ConfigureAwait(false);

                return messages;
            });

    private static List<RunEvent> NoticesOf(IEnumerable<RunEvent> events)
        => [.. events.Where(static e => string.Equals(e.CustomType, RunEventCustomTypes.WorkflowEvent, StringComparison.Ordinal))];

    private sealed class OrderShippedEvent(string orderId) : WorkflowEvent(orderId);
}
