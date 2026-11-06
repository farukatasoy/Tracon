using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A request that carries <c>Idempotency-Key</c> or <c>Prefer: respond-async</c> to a host that turned that feature off
/// is refused with <c>501</c>, and nothing runs.
/// </summary>
/// <remarks>
/// <para>
/// Both options default to on, so neither the sample host nor the OpenAPI fuzz can reach these two refusals. The body
/// of the idempotency refusal used to say the request "is processed anyway" while the filter answers without calling the
/// endpoint; the body now says what happens, and this class pins both the behavior (nothing runs, nothing is stored)
/// and the words, in every combination of store and job worker.
/// </para>
/// <para>
/// The refusal comes before the store is touched, so the in-memory and the SQLite store answer alike. The job worker
/// registered or not makes no difference either, for the same reason; both are driven so the claim is a measurement.
/// </para>
/// </remarks>
public sealed class HeaderDisabled501Tests
{
    private const string IdempotencyHeader = "Idempotency-Key";
    private const string PreferHeader = "Prefer";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Runs = new("/tracon/api/runs?limit=200", UriKind.Relative);

    public static TheoryData<string, bool, bool> Combinations
    {
        get
        {
            var data = new TheoryData<string, bool, bool>();

            foreach (var header in (string[])[IdempotencyHeader, PreferHeader])
            {
                foreach (var sqlite in (bool[])[false, true])
                {
                    foreach (var worker in (bool[])[true, false])
                    {
                        data.Add(header, sqlite, worker);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task A_header_of_a_feature_that_is_off_is_refused_with_501_and_nothing_runs(string header, bool sqlite, bool worker)
    {
        using var database = new TempSqliteDatabase("header-501");

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                if (sqlite)
                {
                    builder.UseSqlite(database.ConnectionString);
                }

                builder.AddAgent(TestData.Definition());
                builder.UseScheduling(options => options.RunWorker = worker);
            },
            configureServices: static services =>
            {
                services.Configure<TraconIdempotencyOptions>(static options => options.Enabled = false);
                services.Configure<TraconAsyncRunOptions>(static options => options.Enabled = false);
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }) };

        if (string.Equals(header, IdempotencyHeader, StringComparison.Ordinal))
        {
            request.Headers.Add(IdempotencyHeader, Guid.NewGuid().ToString("N"));
        }
        else
        {
            request.Headers.Add(PreferHeader, "respond-async");
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType.MediaType.ShouldBe("application/problem+json");

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("status").GetInt32().ShouldBe(501);
        var detail = problem.GetProperty("detail").GetString();

        detail.ShouldNotBeNullOrWhiteSpace();

        // The behavior is "refused, nothing ran": a body that says the request was processed would send a caller to look
        // for a run that does not exist.
        detail.ShouldNotContain("anyway", Case.Insensitive);
        detail.ShouldNotContain("is processed", Case.Insensitive);

        // Nothing ran: no run row (the placeholder row of a queued run included) and no call to the model.
        using var listed = await host.Client.GetAsync(Runs, TestContext.Current.CancellationToken);
        var runs = await TraconTestHost.ReadJsonAsync(listed);

        runs.GetArrayLength().ShouldBe(0, "a refused request must leave no run behind");
        host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single().Requests.Count.ShouldBe(0);
    }
}
