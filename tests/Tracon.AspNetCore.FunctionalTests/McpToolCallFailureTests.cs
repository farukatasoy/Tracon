using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An MCP server whose <c>tools/call</c> never answers, or answers with far too much, cannot hold a run, a connection or
/// the record.
/// </summary>
/// <remarks>
/// <para>
/// The server is a real MCP server on loopback; the agent calls its tool through the whole host, so the per-tool timeout,
/// the caller's own abort and the connection the call used are the ones a consumer runs. Measured when this was written:
/// a call that never answers ends the turn at the tool timeout (2.0 s for a 2 s limit) and the run is <c>Completed</c>,
/// because the model is told the tool timed out; a caller that gives up 1.5 s into the call leaves the run
/// <c>Canceled</c> 0.1 s later; in both cases the server sees the cancellation and the next call on the same connection
/// is answered at once.
/// </para>
/// <para>
/// A result of megabytes is another matter. With no limit a result of 20 MB reached the model request whole, and the run's
/// tool-call record and the session state held it whole as well (about 1.6 GB of memory); only the run's events were cut
/// (8 KB each). <see cref="TraconToolOptions.DefaultMaxOutputBytes"/> trims the result before any of the three, and it is
/// 1 MiB unless the installation says otherwise, so a server nobody configured a limit for is bounded too.
/// </para>
/// </remarks>
public sealed class McpToolCallFailureTests
{
    private const string ServerName = "hang-srv";
    private const string ToolName = ServerName + "_lookup";
    private const string AgentName = "mcp-hang-agent";
    private const string ProviderName = "mcp-hang-model";

    // A tool that is cut at its limit ends the turn within a second or two; the bound is many times that and is only
    // there so that a run which is never cut fails the test instead of hanging it.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_tools_call_that_never_answers_ends_the_turn_at_the_tool_timeout_and_frees_the_connection()
    {
        await using var server = await LookupMcpServer.StartAsync("fine");
        var model = NewModel();
        await using var host = await StartAsync(model, options => options.Tools.DefaultTimeout = TimeSpan.FromSeconds(1));
        await SaveServerAsync(host, server.Endpoint);

        server.CallHandler = HangUntilCancelledAsync;
        model.CallsTool(ToolName);

        using var bound = BoundedToken();
        using var response = await PostRunAsync(host, "hung", bound.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host, "hung");

        run.Status.ShouldBe(RunStatus.Completed, "the model is told the tool timed out, and answers");
        (await response.Content.ReadAsStringAsync(bound.Token)).ShouldContain("did not complete within");

        await WaitUntil.TrueAsync(() => server.CancelledCalls == 1, "the server to see the call cancelled");

        // The connection the cancelled call used is not left holding it.
        server.CallHandler = null;
        model.CallsTool(ToolName);

        using var next = await PostRunAsync(host, "after-the-hang", bound.Token);

        (await next.Content.ReadAsStringAsync(bound.Token)).ShouldContain("fine");
        (await SingleRunAsync(host, "after-the-hang")).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_caller_that_gives_up_on_a_tools_call_that_never_answers_leaves_a_canceled_run_and_a_free_connection()
    {
        await using var server = await LookupMcpServer.StartAsync("fine");
        var model = NewModel();

        // The tool timeout is the default (30 s): it must be the caller's abort that ends the turn.
        await using var host = await StartAsync(model, configure: null);
        await SaveServerAsync(host, server.Endpoint);

        server.CallHandler = HangUntilCancelledAsync;
        model.CallsTool(ToolName);

        using var abort = BoundedToken();
        var request = PostRunAsync(host, "abandoned", abort.Token);

        await WaitUntil.TrueAsync(() => server.Calls == 1, "the call to reach the server");
        await abort.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(request);

        var runs = host.Services.GetRequiredService<IRunStore>();

        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
                .SingleOrDefault()?.Status is { } status && status != RunStatus.Running,
            "the run to leave Running after its caller gave up");

        (await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Canceled);

        await WaitUntil.TrueAsync(() => server.CancelledCalls == 1, "the server to see the call cancelled");

        server.CallHandler = null;
        model.CallsTool(ToolName);

        using var bound = BoundedToken();
        using var next = await PostRunAsync(host, "after-the-abort", bound.Token);

        (await next.Content.ReadAsStringAsync(bound.Token)).ShouldContain("fine");
    }

    [Fact]
    public async Task A_result_of_megabytes_is_trimmed_before_the_model_and_the_record_when_the_installation_sets_a_limit()
    {
        const int Limit = 64 * 1024;
        var big = new string('x', 8 * 1024 * 1024);

        await using var server = await LookupMcpServer.StartAsync("fine");
        var model = NewModel();
        await using var host = await StartAsync(model, options => options.Tools.DefaultMaxOutputBytes = Limit);
        await SaveServerAsync(host, server.Endpoint);

        server.CallHandler = _ => Task.FromResult(big);
        model.CallsTool(ToolName);

        using var bound = BoundedToken();
        using var response = await PostRunAsync(host, "big", bound.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host, "big");
        var store = host.Services.GetRequiredService<IRunStore>();

        run.Status.ShouldBe(RunStatus.Completed);

        // What the model read.
        var resultSeenByModel = model.Requests[^1].Messages
            .SelectMany(static message => message.Contents)
            .OfType<Microsoft.Extensions.AI.FunctionResultContent>()
            .Single()
            .Result?.ToString();

        resultSeenByModel.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(Limit);

        // What the run recorded of the call, and what the session keeps for the next turn.
        var invocation = (await store.ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        invocation.Result.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(Limit);

        var session = await host.Services.GetRequiredService<ISessionStore>().GetAsync("big", cancellationToken: TestContext.Current.CancellationToken);

        session.ShouldNotBeNull().State.GetRawText().Length.ShouldBeLessThan(Limit * 4, "the session keeps the trimmed result, not the megabytes");

        var truncated = 0;

        await foreach (var runEvent in store.ReadEventsAsync(run.Id, cancellationToken: TestContext.Current.CancellationToken))
        {
            truncated += runEvent.Type == RunEventType.ToolOutputTruncated ? 1 : 0;
        }

        truncated.ShouldBe(1, "the run says that it cut the result");
    }

    [Fact]
    public async Task A_result_of_megabytes_is_trimmed_by_the_default_limit_when_nobody_set_one()
    {
        var big = new string('x', 4 * 1024 * 1024);

        await using var server = await LookupMcpServer.StartAsync("fine");
        var model = NewModel();
        await using var host = await StartAsync(model, configure: null);
        await SaveServerAsync(host, server.Endpoint);

        server.CallHandler = _ => Task.FromResult(big);
        model.CallsTool(ToolName);

        using var bound = BoundedToken();
        using var response = await PostRunAsync(host, "default-limit", bound.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = await SingleRunAsync(host, "default-limit");
        var store = host.Services.GetRequiredService<IRunStore>();

        run.Status.ShouldBe(RunStatus.Completed);

        var resultSeenByModel = model.Requests[^1].Messages
            .SelectMany(static message => message.Contents)
            .OfType<Microsoft.Extensions.AI.FunctionResultContent>()
            .Single()
            .Result?.ToString();

        resultSeenByModel.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(new TraconToolOptions().DefaultMaxOutputBytes!.Value);

        var invocation = (await store.ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        invocation.Result.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(new TraconToolOptions().DefaultMaxOutputBytes!.Value);
    }

    private static async Task<string> HangUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated

        return "never";
    }

    private static CancellationTokenSource BoundedToken()
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(Bound); // cancel: bound

        return source;
    }

    private static Tracon.Testing.FakeModelProvider NewModel()
        => new Tracon.Testing.FakeModelProvider(ProviderName).EchoesLastToolResult();

    private static Task<TraconTestHost> StartAsync(Tracon.Testing.FakeModelProvider model, Action<TraconOptions>? configure)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseMcp();
                builder.AddModelProvider(model);
            },
            configureServices: services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);

                if (configure is not null)
                {
                    services.Configure(configure);
                }
            });

    private static async Task SaveServerAsync(TraconTestHost host, Uri endpoint)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative),
                   new { endpoint = endpoint.ToString(), requiresApproval = false },
                   TestContext.Current.CancellationToken))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        using (var refreshed = await host.Client.PostAsync(
                   new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null, TestContext.Current.CancellationToken))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var agent = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Look the value up.",
                Model = new ModelBinding { Provider = ProviderName, Model = "lookup" },
                ToolNames = [ToolName],
            },
            TestContext.Current.CancellationToken);

        agent.StatusCode.ShouldBe(HttpStatusCode.Created, await agent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static Task<HttpResponseMessage> PostRunAsync(TraconTestHost host, string session, CancellationToken cancellationToken)
        => host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "look it up", SessionId = session },
            cancellationToken);

    private static async Task<RunRecord> SingleRunAsync(TraconTestHost host, string session)
    {
        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri($"/tracon/api/runs?sessionId={session}", UriKind.Relative), TestContext.Current.CancellationToken);

        return await host.WaitForTerminalRunAsync(runs.ShouldHaveSingleItem().Id);
    }
}
