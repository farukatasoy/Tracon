using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host that opens one agent on several surfaces can tell, in its run authorization handler, which surface a question
/// came from.
/// </summary>
/// <remarks>
/// <para>
/// The handler has the agent and the user and, without the channel, nothing that separates "the chat front end" from "the
/// voice socket" from "the host's own code": a rule such as "this agent runs by voice and nowhere else" could only be
/// guessed from the ambient request, which is the wrong request on a socket turn and absent in process. Tracon sets the
/// channel at the place each surface asks, so a caller cannot choose it.
/// </para>
/// <para>
/// The A2A, MCP, voice and AG-UI cases sit with the tests of those surfaces (they own the hosts that start them).
/// </para>
/// </remarks>
public sealed class RunChannelTests
{
    private const string Agent = "kod-agent";

    [Fact]
    public async Task A_run_started_from_the_management_API_says_HttpApi()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var request = handler.RunRequests.ShouldHaveSingleItem();

        request.Access.ShouldBe(RunAccess.Start);
        request.Channel.ShouldBe(RunChannel.HttpApi);
    }

    [Fact]
    public async Task A_run_started_from_the_OpenAI_chat_completions_endpoint_says_OpenAIChatCompletions()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = Agent, messages = new[] { new { role = "user", content = "hello" } } },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        handler.RunRequests.ShouldHaveSingleItem().Channel.ShouldBe(RunChannel.OpenAIChatCompletions);
    }

    [Fact]
    public async Task A_run_started_from_the_OpenAI_responses_endpoint_says_OpenAIResponses()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = Agent, input = "hello" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        handler.RunRequests.ShouldHaveSingleItem().Channel.ShouldBe(RunChannel.OpenAIResponses);
    }

    [Fact]
    public async Task Reading_a_run_says_HttpApi_whatever_surface_started_it()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        // Started on a surface that is not the management API; read afterwards through it.
        using var started = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = Agent, messages = new[] { new { role = "user", content = "hello" } } },
            TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();

        using var list = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);
        list.EnsureSuccessStatusCode();

        var runId = (await TraconTestHost.ReadJsonAsync(list))[0].GetProperty("id").GetGuid();

        handler.Clear();

        using var read = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}", UriKind.Relative), TestContext.Current.CancellationToken);
        read.EnsureSuccessStatusCode();

        var request = handler.RunRequests.ShouldHaveSingleItem();

        request.Access.ShouldBe(RunAccess.Read);
        request.Channel.ShouldBe(RunChannel.HttpApi);
    }

    [Fact]
    public async Task The_hosts_own_call_to_RunAdmission_says_Embedded()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        var decision = await host.Services.GetRequiredService<RunAdmission>()
            .CheckAsync(Agent, "s1", TestContext.Current.CancellationToken);

        decision.IsAllowed.ShouldBeTrue();
        handler.RunRequests.ShouldHaveSingleItem().Channel.ShouldBe(RunChannel.Embedded);
    }

    private static Task<TraconTestHost> StartAsync(IRunAuthorizationHandler handler)
        => TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services => services.Replace(ServiceDescriptor.Singleton(handler)));

    private sealed class RecordingHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RunAuthorizationRequest> _runRequests = [];

        public IReadOnlyList<RunAuthorizationRequest> RunRequests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _runRequests];
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _runRequests.Clear();
            }
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _runRequests.Add(request);
            }

            return ValueTask.FromResult(RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
