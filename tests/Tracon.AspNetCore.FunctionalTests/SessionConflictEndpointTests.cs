using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The HTTP shape of a session conflict, on both surfaces that can produce one.
/// </summary>
/// <remarks>
/// <para>
/// A conflict means another turn wrote the same session while this one was
/// running. It is a CLIENT-side race and the caller should retry the turn, so
/// the answer is <c>409</c> — a <c>502</c> would send the caller looking at the
/// model provider instead.
/// </para>
/// <para>
/// 🚨 Neither mapping had a test. The <c>/api/agents</c> branch was written for
/// HATA-004, when a conflict needed two concurrent FIRST turns on a brand-new
/// session and was therefore nearly unreachable; the OpenAI-compatible branch
/// was missed entirely and answered <c>502</c>. Once every later save became
/// guarded too, the condition became reachable in ordinary operation and an
/// untested mapping stopped being acceptable.
/// </para>
/// <para>
/// The conflict is produced DETERMINISTICALLY by a store whose
/// <c>TryUpdateAsync</c> always refuses, rather than by racing two real
/// requests: a timing race would make this test flaky, and what is under test
/// is the mapping, not the store.
/// </para>
/// </remarks>
public sealed class SessionConflictEndpointTests
{
    private const string SessionId = "conflicting-session";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Fact]
    public async Task Agent_run_answers_409_with_the_session_conflict_error_type()
    {
        await using var host = await StartAsync();

        // The first turn creates the session; only a LATER save is guarded.
        (await PostRunAsync(host)).Dispose();

        using var response = await PostRunAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        problem.GetProperty("errorType").GetString().ShouldBe("session_conflict");
    }

    /// <summary>
    /// HATA-S1-011: the run record carries the conflict.
    /// </summary>
    /// <remarks>
    /// The run stays <see cref="RunStatus.Completed"/> — it completed, and
    /// calling it failed would misreport both its cost and its answer. What
    /// was missing is any trace at all: the operator saw two successful runs
    /// for the same session, and the losing side lived only in a response the
    /// caller had already consumed.
    /// </remarks>
    [Fact]
    public async Task The_run_record_carries_the_conflict_and_still_reads_Completed()
    {
        await using var host = await StartAsync();

        (await PostRunAsync(host)).Dispose();
        (await PostRunAsync(host)).Dispose();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var records = await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken);

        records.Count.ShouldBe(2);
        records.ShouldAllBe(record => record.Status == RunStatus.Completed);

        var conflicted = new List<string?>();

        foreach (var record in records)
        {
            await foreach (var runEvent in runs.ReadEventsAsync(record.Id, 0, TestContext.Current.CancellationToken))
            {
                if (runEvent.Type == RunEventType.SessionWriteConflicted)
                {
                    conflicted.Add(runEvent.Text);
                }
            }
        }

        // Exactly one side lost the race, and the event names the session.
        conflicted.ShouldHaveSingleItem().ShouldBe(SessionId);
    }

    [Fact]
    public async Task Openai_responses_answers_409_instead_of_502()
    {
        // 🚨 The sibling path that was missed: it reported a client-side race
        // as an upstream failure.
        await using var host = await StartAsync();

        (await PostResponsesAsync(host)).Dispose();

        using var response = await PostResponsesAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        body.GetProperty("error").GetProperty("type").GetString().ShouldBe("session_conflict");
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder =>
        {
            builder.AddAgent(TestData.Definition());
            builder.Services.AddSingleton<ISessionStore, AlwaysConflictingSessionStore>();
        });

    /// <summary>
    /// Enters the non-streaming branch, the only one that can answer with a
    /// status code: on a streaming request the headers are already sent.
    /// </summary>
    private static async Task<HttpResponseMessage> PostRunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = SessionId }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> PostResponsesAsync(TraconTestHost host)
        => await host.Client.PostAsJsonAsync(
            Responses,
            new
            {
                model = "kod-agent",
                input = "hello",
                conversation = SessionId,
                stream = false,
            },
            TestContext.Current.CancellationToken).ConfigureAwait(false);

    /// <summary>
    /// A store that creates normally and refuses every conditional update —
    /// the state a real store reaches when another turn wrote first.
    /// </summary>
    private sealed class AlwaysConflictingSessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, SessionRecord> _records = new(StringComparer.Ordinal);

        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.Id] = record;
            return default;
        }

        public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => new(_records.TryAdd(record.Id, record with { Version = 1 }));

        public ValueTask<bool> TryUpdateAsync(
            SessionRecord record,
            long expectedVersion,
            CancellationToken cancellationToken = default)
            => new(false);

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => new(_records.GetValueOrDefault(sessionId));

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => new(_records.TryRemove(sessionId, out _));

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(
            SessionQuery query,
            CancellationToken cancellationToken = default)
            => new([.. _records.Values]);
    }
}
