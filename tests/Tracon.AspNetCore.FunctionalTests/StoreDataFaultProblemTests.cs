using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a caller is told when the store ANSWERED and refused a value: a <c>400</c> that says the value is what to change,
/// not the <c>503</c> that says the store is down and invites a retry that can never work.
/// </summary>
/// <remarks>
/// The provider's own classification (which SQL Server error number is a refusal) is held against a real server by the
/// provider integration tests; the HTTP contract against a real server is held by the two-node acceptance tests. What
/// is held here is the host's half, with a provider stood in for: the mapping, the sentinel that never leaks, and
/// every place the mapping must NOT apply.
/// </remarks>
public sealed class StoreDataFaultProblemTests
{
    private const string Canary = "CANARY-7f3a9c-the-value-the-provider-quotes";

    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task A_refused_text_is_a_400_that_says_the_value_is_the_problem()
    {
        await using var host = await StartAsync(new RefusedValueException(StoreDataFault.TextTooLong));

        using var response = await PostAgentAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("title").GetString().ShouldBe(StoreDataFaultProblemMiddleware.TextTooLongTitle);
        body.GetProperty("detail").GetString().ShouldBe(StoreDataFaultProblemMiddleware.TextTooLongDetail);
        body.GetProperty("status").GetInt32().ShouldBe(400);
    }

    [Fact]
    public async Task A_refused_number_is_a_400_with_its_own_title()
    {
        await using var host = await StartAsync(new RefusedValueException(StoreDataFault.NumberOutOfRange));

        using var response = await PostAgentAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("title").GetString().ShouldBe(StoreDataFaultProblemMiddleware.NumberOutOfRangeTitle);
        body.GetProperty("detail").GetString().ShouldBe(StoreDataFaultProblemMiddleware.NumberOutOfRangeDetail);
    }

    [Theory]
    [InlineData("TextTooLong")]
    [InlineData("NumberOutOfRange")]
    public async Task Neither_the_response_nor_the_log_carries_what_the_provider_said(string faultName)
    {
        var fault = Enum.Parse<StoreDataFault>(faultName);

        // The provider's message names the table, the column and the value it cut, and the value is the caller's data.
        await using var host = await StartAsync(new RefusedValueException(fault));

        using var response = await PostAgentAsync(host);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var log = host.Logs.AllText;

        // Per line, so a failure names the logger that wrote the value instead of the whole log.
        log.Split('\n').Where(static line => line.Contains(Canary, StringComparison.Ordinal)).ShouldBeEmpty();

        foreach (var text in new[] { raw, log })
        {
            text.ShouldNotContain(Canary);
            text.ShouldNotContain("mt_secret_schema");
            text.ShouldNotContain("agent_definitions");
            text.ShouldNotContain("display_name");
            text.ShouldNotContain("INSERT");
            text.ShouldNotContain("would be truncated");
        }

        // The log still tells the operator what happened, without the data.
        log.ShouldContain(fault.ToString());
        log.ShouldContain("Warning");
        log.ShouldNotContain("Error Tracon.StoreUnavailableProblemMiddleware");
    }

    [Fact]
    public async Task A_provider_failure_that_is_not_a_refused_value_is_still_a_503()
    {
        // The other half of the decision: a store that did not answer must stay the retryable 503.
        await using var host = await StartAsync(new MissingRelationException());

        using var response = await PostAgentAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("title").GetString().ShouldBe(StoreUnavailableProblemMiddleware.UnreachableProblemTitle);
    }

    [Fact]
    public async Task Without_a_sql_provider_nothing_is_a_refused_value()
    {
        // A regression gate, not a proof of the mapping: the host stays a 503 with the middleware in place or not. What
        // it holds is the one branch that has no classifier, which must not be required (a lookup that throws when the
        // seam is missing would break every in-memory host).
        // Persistence in memory registers no classifier: it has no column to overflow, so an exception that looks like a
        // refusal is not read as one.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Replace(
                ServiceDescriptor.Singleton<IAgentDefinitionStore>(
                    new ThrowingAgentDefinitionStore(new RefusedValueException(StoreDataFault.TextTooLong)))));

        host.Services.GetService<IStoreDataFaultClassifier>().ShouldBeNull();

        using var response = await PostAgentAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_consumers_own_endpoint_is_not_touched()
    {
        await using var host = await StartAsync(
            new RefusedValueException(StoreDataFault.TextTooLong),
            configureApp: static app => app.MapGet(
                "/own",
                static () => Task.FromException<IResult>(new RefusedValueException(StoreDataFault.TextTooLong))));

        // The exception reaches the consumer's own handler or the server, as it did before: it is not Tracon's to answer.
        await Should.ThrowAsync<RefusedValueException>(async () =>
            await host.Client.GetAsync(new Uri("/own", UriKind.Relative), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_response_that_already_started_is_left_alone()
    {
        // Writing a second response after the first byte went out would corrupt the stream, so the exception goes on.
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IStoreDataFaultClassifier>(new RefusingClassifier())
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new TagsAttribute("Tracon")),
            "test"));

        await Should.ThrowAsync<RefusedValueException>(async () =>
            await StoreDataFaultProblemMiddleware.InvokeAsync(
                context,
                static _ => throw new RefusedValueException(StoreDataFault.TextTooLong)));
    }

    [Fact]
    public async Task Memory_and_sqlite_accept_the_value_that_sql_server_refuses()
    {
        // 🚨 A regression gate, not a proof: the three stores do not agree on the longest text, and this fixes the
        // difference. SQL Server holds 200 characters in this column; the other two hold any length. The day one
        // limit is chosen for all of them, this test fails and the change is a decision, not an accident.
        var longName = new string('a', 300);

        await using var memory = await TraconTestHost.StartAsync();
        using var inMemory = await memory.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/acme", UriKind.Relative),
            new { displayName = longName },
            TestContext.Current.CancellationToken);
        inMemory.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var database = new TempSqliteDatabase("data-fault-parity");
        await using var sqlite = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseSqlite(database.ConnectionString));
        using var onSqlite = await sqlite.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/acme", UriKind.Relative),
            new { displayName = longName },
            TestContext.Current.CancellationToken);
        onSqlite.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> PostAgentAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(Agents, TestData.Request(), TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(Exception fault, Action<WebApplication>? configureApp = null)
        => TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore>(new ThrowingAgentDefinitionStore(fault)));
                services.AddSingleton<IStoreDataFaultClassifier>(new RefusingClassifier());
            },
            configureApp: configureApp);

    /// <summary>Stands in for a provider's refusal; its message is what a real provider's carries.</summary>
    private sealed class RefusedValueException(StoreDataFault fault)
        : DbException($"String or binary data would be truncated in table 'mt_secret_schema.agent_definitions', column 'display_name'. Truncated value: '{Canary}'. INSERT INTO ...")
    {
        public StoreDataFault Fault { get; } = fault;
    }

    private sealed class MissingRelationException() : DbException("relation \"mt_secret_schema.agent_definitions\" does not exist");

    private sealed class RefusingClassifier : IStoreDataFaultClassifier
    {
        public bool TryClassify(Exception exception, out StoreDataFault fault)
        {
            if (exception is RefusedValueException refused)
            {
                fault = refused.Fault;
                return true;
            }

            fault = default;
            return false;
        }
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    /// <summary>
    /// A store that reads as empty and refuses the write, which is the one place a value can overflow: a read compares a
    /// value to a column and never stores it.
    /// </summary>
    private sealed class ThrowingAgentDefinitionStore(Exception fault) : IAgentDefinitionStore
    {
        public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
            => new((AgentDefinition?)null);

        public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
            => new((AgentDefinition?)null);

        public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => new([]);

        public ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default)
            => throw fault;

        public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
            => new(false);

        public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
            => new([]);

        public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
            => throw fault;
    }
}
