using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a run request does when one call to one store fails once: for every store member a plain run calls, a table of
/// the answer.
/// </summary>
/// <remarks>
/// <para>
/// A store fails in two ways that look alike and are not. It fails before it did anything (a refused connection), or it
/// does the write and then fails to say so (a timeout after the commit). Each member is failed both ways where the
/// member returns a plain task, with the exception a SQL provider throws (a <see cref="DbException"/>).
/// </para>
/// <para>
/// The table is the point. A store that the run only RECORDS to must not stop the run (the response is still the answer);
/// a store that decides whether the run may start answers 503 with the problem document that says the store did not
/// answer. Which members are in the table is checked against the members the run really calls, so a new call on the run
/// path cannot go unclassified.
/// </para>
/// <para>
/// A row can also record an answer that is the wrong one, kept so the table stays true and a fix shows up as a changed
/// row. There is none now: a failing read of the tenant's provider binding or egress policy used to be reported as
/// <c>400 Agent compilation failed</c>, which tells the caller their agent is broken when the store is.
/// </para>
/// <para>
/// <c>IExperimentStore.GetRunningAsync</c> answers 503 on purpose, and it is the one row where the run could have gone on.
/// The lookup picks the version of the agent a session talks to; running on the current version when it fails would move a
/// session to another version in the middle of a conversation and skew the experiment, and the database that failed the
/// lookup is usually failing the other writes of the run as well.
/// </para>
/// </remarks>
public sealed class StoreFailureMatrixTests
{
    private const string AgentName = "matrix-agent";
    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    private static readonly IReadOnlyDictionary<string, HttpStatusCode> Answers =
        new Dictionary<string, HttpStatusCode>(StringComparer.Ordinal)
        {
            // The run only records to these: it goes on and the caller gets the answer.
            ["IRunStore.StartRunAsync"] = HttpStatusCode.OK,
            ["IRunStore.AppendEventAsync"] = HttpStatusCode.OK,
            ["IRunStore.CompleteRunAsync"] = HttpStatusCode.OK,
            ["IRunStore.GetLastEventSequenceAsync"] = HttpStatusCode.OK,
            ["IRunInputStore.SaveAsync"] = HttpStatusCode.OK,
            ["IQuotaStore.ListAsync"] = HttpStatusCode.OK,
            ["IQuotaStore.AddUsageAsync"] = HttpStatusCode.OK,
            ["IWebhookStore.FindForEventAsync"] = HttpStatusCode.OK,
            ["ITraceStore.WriteSpansAsync"] = HttpStatusCode.OK,

            // These decide whether the run may start, or whether the caller may retry it: 503, the store did not answer.
            ["IIdempotencyStore.ReserveAsync"] = HttpStatusCode.ServiceUnavailable,
            ["IIdempotencyStore.CompleteAsync"] = HttpStatusCode.ServiceUnavailable,
            ["IExperimentStore.GetRunningAsync"] = HttpStatusCode.ServiceUnavailable,

            // Read while the agent is compiled. The catalog wraps a source's failure as "the agent could not be compiled"
            // (a 400), but a store that did not answer is not a source that broke: it passes through and the host answers 503.
            ["ITenantProviderBindingStore.GetAsync"] = HttpStatusCode.ServiceUnavailable,
            ["ITenantEgressPolicyStore.GetAsync"] = HttpStatusCode.ServiceUnavailable,
        };

    /// <summary>
    /// Members a run calls only when the exporter happens to flush before the run is over. Whether it does depends on the
    /// machine, so these rows are not waited for; a call to one still needs its row.
    /// </summary>
    private static readonly HashSet<string> Optional = new(StringComparer.Ordinal) { "ITraceStore.WriteSpansAsync" };

    public static TheoryData<string> Members() => [.. Answers.Keys];

    [Fact]
    public async Task Every_store_member_a_plain_run_calls_has_a_recorded_answer()
    {
        var calls = new ConcurrentDictionary<(Type Service, string Member), int>();

        await using var host = await StartAsync(builder =>
        {
            foreach (var store in StoreTypes(builder.Services).ToList())
            {
                CallRecording.Record(builder.Services, store, calls);
            }
        });

        (await PostAsync(host)).ShouldBe(HttpStatusCode.OK);

        // Some of the calls happen after the response (the webhook lookup for the finished run is one), and a loaded
        // machine runs them late. Wait until every row has been called (the optional ones excepted), then look for a member
        // with no row: one that arrives after the comparison is not caught.
        await WaitUntil.TrueAsync(
            () => Answers.Keys.Where(static row => !Optional.Contains(row)).All(row => calls.Keys.Any(key => string.Equals($"{key.Service.Name}.{key.Member}", row, StringComparison.Ordinal))),
            "every store member of the table to be called by the run");

        var called = calls.Keys.Select(static key => $"{key.Service.Name}.{key.Member}").ToHashSet(StringComparer.Ordinal);
        var withoutRow = called.Except(Answers.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        withoutRow.ShouldBeEmpty(
            "The run calls a store member that has no row. Add the row and say what the run does when that call fails.");
    }

    [Theory]
    [MemberData(nameof(Members))]
    public async Task One_failing_store_call_has_the_recorded_answer(string member)
    {
        var (service, name) = Resolve(member);
        var expected = Answers[member];

        foreach (var afterCommit in ModesFor(service, name))
        {
            await using var host = await StartFailingAsync(service, name, afterCommit);

            var mode = afterCommit ? "after the write" : "before the write";
            var status = await PostAsync(host);

            status.ShouldBe(expected, $"{member} failing {mode}");
        }
    }

    /// <summary>
    /// The answer is the caller's half. The record is the other half: a run that was answered must not stay
    /// <c>Running</c> because one write of its record failed once. A lost event costs the stream after it; the closing
    /// write is tried again, so the record still ends.
    /// </summary>
    [Theory]
    [InlineData("IRunStore.AppendEventAsync")]
    [InlineData("IRunStore.CompleteRunAsync")]
    public async Task A_run_whose_record_write_failed_once_still_ends_in_a_terminal_state(string member)
    {
        var (service, name) = Resolve(member);

        foreach (var afterCommit in ModesFor(service, name))
        {
            await using var host = await StartFailingAsync(service, name, afterCommit);

            (await PostAsync(host)).ShouldBe(HttpStatusCode.OK);

            // The injected failure is spent by now, so this read goes to the real store. The closing write may land a
            // moment after the response (it waits before it asks again), so wait for it.
            var runs = host.Services.GetRequiredService<IRunStore>();

            await WaitUntil.TrueAsync(
                async () => (await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
                    .SingleOrDefault()?.Status is { } status && status != RunStatus.Running,
                $"the record to leave Running after {member} failed {(afterCommit ? "after" : "before")} the write");

            (await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
                .ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Completed);
        }
    }

    private static Task<TraconTestHost> StartFailingAsync(Type service, string member, bool afterCommit)
        => StartAsync(builder =>
        {
            var fault = (Func<Exception>)(static () => new InjectedDbException());
            var inject = typeof(FaultInjection)
                .GetMethod(afterCommit ? nameof(FaultInjection.ThrowAfter) : nameof(FaultInjection.ThrowFrom))!
                .MakeGenericMethod(service);

            inject.Invoke(
                null,
                afterCommit ? [builder.Services, member, fault, 1] : [builder.Services, member, fault, 1, null]);
        });

    /// <summary>
    /// A write that is reported as failed after it was done can only be built around a member that returns a plain task.
    /// </summary>
    private static IEnumerable<bool> ModesFor(Type service, string member)
    {
        yield return false;

        var returns = service.GetMethod(member)!.ReturnType;

        if (returns == typeof(ValueTask) || returns == typeof(Task))
        {
            yield return true;
        }
    }

    private static (Type Service, string Member) Resolve(string key)
    {
        var parts = key.Split('.', 2);
        var service = typeof(IRunStore).Assembly.GetTypes().Single(type => string.Equals(type.Name, parts[0], StringComparison.Ordinal));

        return (service, parts[1]);
    }

    private static IEnumerable<Type> StoreTypes(IServiceCollection services)
        => services
            .Select(static descriptor => descriptor.ServiceType)
            .Where(static type => type.IsInterface
                && type.IsPublic
                && string.Equals(type.Namespace, "Tracon", StringComparison.Ordinal)
                && (type.Name.EndsWith("Store", StringComparison.Ordinal) || type.Name.EndsWith("Log", StringComparison.Ordinal)))
            .Distinct();

    private static Task<TraconTestHost> StartAsync(Action<ITraconBuilder> configure)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Reply briefly.",
                Model = TestData.Model(),
                Origin = AgentDefinitionOrigin.Code,
            });
            configure(builder);
        });

    private static async Task<HttpStatusCode> PostAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private sealed class InjectedDbException : DbException
    {
        public InjectedDbException()
            : base("injected")
        {
        }
    }
}
