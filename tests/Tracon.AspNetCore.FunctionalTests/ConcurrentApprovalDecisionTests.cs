using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// The same alias reason as ApprovalEndpointTests: a blanket `using Tracon.Testing;` collides with this project's
// TraconTestHost.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Two requests that answer the same pending tool approval at the same moment run the tool once.
/// </summary>
/// <remarks>
/// <para>
/// The pending approval is read from the session history, and the session was written only when the run ended, so two
/// requests that loaded the session before either saved both saw the approval pending, both approved it and both ran the
/// tool. The loser was told so only afterwards, as a conflict on the save, when the tool had already run. A tool that
/// cancels an order or sends a message must not run twice.
/// </para>
/// <para>
/// The race is set by the store, not by timing: the first two loads of the session wait for each other, so both requests
/// hold the same version of it whatever the thread pool does.
/// </para>
/// </remarks>
public sealed class ConcurrentApprovalDecisionTests
{
    private const string AgentName = "approval-race-agent";
    private const string SessionId = "approval-race-session";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task Two_requests_that_answer_the_same_approval_together_run_the_tool_once_and_the_other_gets_a_conflict()
    {
        var sessions = new RendezvousSessionStore();
        var toolCalls = 0;

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider("approval-model")
                        .CallsTool("cancel_order", new { orderId = "ORD-7" })
                        .EchoesLastToolResult())
                    .AddTool(
                        (Func<string, string>)(orderId =>
                        {
                            Interlocked.Increment(ref toolCalls);

                            return $"{orderId} canceled.";
                        }),
                        name: "cancel_order",
                        description: "Cancels an order.",
                        configure: options => options.RequiresApproval = true)
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                        ToolNames = ["cancel_order"],
                    });
                builder.Services.AddSingleton<ISessionStore>(sessions);
            });

        using var first = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "cancel the order", SessionId = SessionId },
            TestContext.Current.CancellationToken);

        var requestId = await ReadApprovalRequestIdAsync(first);

        toolCalls.ShouldBe(0, "the tool waits for the approval");

        // From here on, the first two loads of the session wait for each other.
        sessions.Arm();

        var answers = await Task.WhenAll(AnswerAsync(host, requestId), AnswerAsync(host, requestId));

        sessions.BothLoaded.ShouldBeTrue("the race was not set: the two requests did not hold the session together");
        toolCalls.ShouldBe(1, "one approval was decided twice and the tool ran for each decision");
        answers.Count(static status => status == HttpStatusCode.OK).ShouldBe(1);
        answers.Count(static status => status == HttpStatusCode.Conflict).ShouldBe(1);
    }

    private static async Task<HttpStatusCode> AnswerAsync(TraconTestHost host, string requestId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                SessionId = SessionId,
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true }],
            }),
        };

        // A request with an idempotency key takes the buffered branch, the one that can answer with a status code.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        string? id = null;

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)))
        {
            if (!string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                continue;
            }

            id = JsonDocument.Parse(frame.Data).RootElement.EnumerateArray()
                .Select(static request => request.GetProperty("requestId").GetString())
                .FirstOrDefault();
            break;
        }

        id.ShouldNotBeNullOrEmpty("The run did not return a pending approval request to answer.");

        return id!;
    }

    /// <summary>
    /// A session store with the generation check of a real one, whose first two reads, once armed, wait for each other so
    /// that both hold the same generation of the session.
    /// </summary>
    private sealed class RendezvousSessionStore : ISessionStore
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly ConcurrentDictionary<string, SessionRecord> _records = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _bothLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _arrived;

        public bool BothLoaded => _bothLoaded.Task.IsCompletedSuccessfully;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.Id] = record;

            return default;
        }

        public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => new(_records.TryAdd(record.Id, record with { Version = 1 }));

        public ValueTask<bool> TryUpdateAsync(SessionRecord record, long expectedVersion, CancellationToken cancellationToken = default)
        {
            lock (_records)
            {
                if (!_records.TryGetValue(record.Id, out var current) || current.Version != expectedVersion)
                {
                    return new ValueTask<bool>(false);
                }

                _records[record.Id] = record with { Version = expectedVersion + 1 };

                return new ValueTask<bool>(true);
            }
        }

        public async ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            SessionRecord? held;

            lock (_records)
            {
                held = _records.GetValueOrDefault(sessionId);
            }

            if (Volatile.Read(ref _armed) == 1)
            {
                var arrival = Interlocked.Increment(ref _arrived);

                if (arrival == 2)
                {
                    _bothLoaded.TrySetResult();
                }

                if (arrival <= 2)
                {
                    // Both requests hold the generation they read before either goes on.
                    await _bothLoaded.Task.WaitAsync(Patience, cancellationToken).ConfigureAwait(false);
                }
            }

            return held;
        }

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => new(_records.TryRemove(sessionId, out _));

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => new([.. _records.Values]);
    }
}
