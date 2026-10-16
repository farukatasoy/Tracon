using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A streaming run whose producer is busy and quiet keeps its connection alive.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-325: the workflow stream wrote nothing while a node worked. The console
/// reads every run stream with a 30-second idle limit, so a node that took
/// longer looked like a dead connection: the console closed it, and a closed
/// connection cancels the workflow run (K-845). A healthy run ended
/// <c>Canceled</c> because it was quiet. The agent stream had the same gap for
/// any reader with an idle limit, a reverse proxy among them.
/// </para>
/// <para>
/// Each case reads the stream the way the console does — any read that waits
/// longer than an idle limit fails — and holds the producer until the stream
/// has proved it stays alive on its own.
/// </para>
/// </remarks>
public sealed class StreamKeepAliveTests
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMilliseconds(100);

    // Twenty keep-alive intervals: a stream that keeps itself alive never gets
    // near it, and one that does not fails here instead of hanging.
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task A_workflow_node_that_works_quietly_keeps_the_stream_alive_and_the_run_completes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddAgent(TestData.Definition("writer"))
                .UseWorkflows()
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "quiet",
                    _ => async (messages, _, cancellationToken) =>
                    {
                        await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                        return messages;
                    }),
            configureEndpoints: static options => options.StreamKeepAliveInterval = KeepAliveInterval);

        using (var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/quiet-chain",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "quiet", Kind = WorkflowNodeKind.Function },
                ],
            }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/workflows/quiet-chain/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "hello" }),
        };

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stream = await ReadAsync(response, () => release.TrySetResult());

        stream.KeepAlivesWhileHeld.ShouldBeGreaterThanOrEqualTo(3);
        stream.Frames[^1].Event.ShouldBe("done");

        var runId = JsonDocument.Parse(stream.Frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        using var run = await host.Client.GetAsync($"/tracon/api/runs/{runId}");
        var record = await TraconTestHost.ReadJsonAsync(run);

        record.GetProperty("status").GetString().ShouldBe("Completed");
    }

    [Fact]
    public async Task An_agent_waiting_on_a_slow_model_keeps_the_stream_alive()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: false);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition("slow") with
                {
                    Model = new ModelBinding { Provider = "hanging", Model = "hanging-1" },
                }),
            configureEndpoints: static options => options.StreamKeepAliveInterval = KeepAliveInterval);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/slow/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stream = await ReadAsync(response, () => hanging.Release("finally"));

        stream.KeepAlivesWhileHeld.ShouldBeGreaterThanOrEqualTo(3);
        stream.Frames[^1].Event.ShouldBe("done");
    }

    [Fact]
    public void The_keep_alive_interval_rejects_a_value_the_delay_cannot_take()
    {
        var options = new TraconEndpointOptions();

        options.StreamKeepAliveInterval.ShouldBe(TimeSpan.FromSeconds(15));
        Should.Throw<ArgumentOutOfRangeException>(() => options.StreamKeepAliveInterval = TimeSpan.Zero);
    }

    /// <summary>
    /// Reads the stream with an idle limit on every read. After the first
    /// frame, it counts keep-alive comments, and after three it calls
    /// <paramref name="release"/> so the producer can finish.
    /// </summary>
    private static async Task<HeldStream> ReadAsync(HttpResponseMessage response, Action release)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8);

        var frames = new List<SseFrame>();
        string? name = null;
        var data = new StringBuilder();
        var keepAlives = 0;
        var released = false;

        while (await ReadLineWithinIdleLimitAsync(reader) is { } line)
        {
            if (line.StartsWith(':'))
            {
                if (frames.Count > 0 && !released && ++keepAlives == 3)
                {
                    released = true;
                    release();
                }

                continue;
            }

            if (line.Length == 0)
            {
                if (name is not null || data.Length > 0)
                {
                    frames.Add(new SseFrame(null, name, data.ToString()));
                }

                name = null;
                data.Clear();

                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data.Append(line["data: ".Length..]);
            }
        }

        released.ShouldBeTrue("The stream ended before it sent three keep-alives.");

        return new HeldStream(frames, keepAlives);
    }

    private static async Task<string?> ReadLineWithinIdleLimitAsync(StreamReader reader)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        idle.CancelAfter(IdleLimit);

        try
        {
            return await reader.ReadLineAsync(idle.Token);
        }
        catch (OperationCanceledException ex) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new ShouldAssertException(
                $"The stream sent nothing for {IdleLimit.TotalSeconds} seconds; a reader with an idle limit closes it here.",
                ex);
        }
    }

    private sealed record HeldStream(List<SseFrame> Frames, int KeepAlivesWhileHeld);
}
