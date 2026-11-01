using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

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
/// Two rows record an answer that is the wrong one, kept here so the table is true and a fix shows up as a changed row:
/// a failing read of the tenant's provider binding or egress policy is reported as <c>400 Agent compilation failed</c>,
/// which tells the caller their agent is broken when the store is.
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

            // These decide whether the run may start, or whether the caller may retry it: 503, the store did not answer.
            ["IIdempotencyStore.ReserveAsync"] = HttpStatusCode.ServiceUnavailable,
            ["IIdempotencyStore.CompleteAsync"] = HttpStatusCode.ServiceUnavailable,
            ["IExperimentStore.GetRunningAsync"] = HttpStatusCode.ServiceUnavailable,

            // Wrong answers, recorded as they are: the agent source wraps any exception it did not recognize as "the agent
            // could not be compiled", so a store that failed while the tenant's policy was read looks like a broken agent.
            ["ITenantProviderBindingStore.GetAsync"] = HttpStatusCode.BadRequest,
            ["ITenantEgressPolicyStore.GetAsync"] = HttpStatusCode.BadRequest,
        };

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

        calls.Keys
            .Select(static key => $"{key.Service.Name}.{key.Member}")
            .Order(StringComparer.Ordinal)
            .ToList()
            .ShouldBe(
                Answers.Keys.Order(StringComparer.Ordinal).ToList(),
                customMessage: "A store member the run calls has no row, or a row names a member it no longer calls. Add or remove the row and say what the run does when that call fails.");
    }

    [Theory]
    [MemberData(nameof(Members))]
    public async Task One_failing_store_call_has_the_recorded_answer(string member)
    {
        var (service, name) = Resolve(member);
        var expected = Answers[member];

        foreach (var afterCommit in ModesFor(service, name))
        {
            await using var host = await StartAsync(builder =>
            {
                var fault = (Func<Exception>)(static () => new InjectedDbException());
                var inject = typeof(FaultInjection)
                    .GetMethod(afterCommit ? nameof(FaultInjection.ThrowAfter) : nameof(FaultInjection.ThrowFrom))!
                    .MakeGenericMethod(service);

                inject.Invoke(
                    null,
                    afterCommit ? [builder.Services, name, fault, 1] : [builder.Services, name, fault, 1, null]);
            });

            var mode = afterCommit ? "after the write" : "before the write";
            var status = await PostAsync(host);

            status.ShouldBe(expected, $"{member} failing {mode}");
        }
    }

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
