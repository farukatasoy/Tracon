using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A consumer's <see cref="IAgentDecorator"/> that adds Microsoft Agent
/// Framework's function-invocation middleware (<c>AIAgentBuilder.Use</c> with a
/// <c>FunctionInvocationContext</c> callback) sees every tool call of a run
/// that a Tracon endpoint starts.
/// </summary>
/// <remarks>
/// 🚨 MAF's middleware accepts only <see langword="null"/>, a plain
/// <see cref="AgentRunOptions"/>, or <c>ChatClientAgentRunOptions</c>, and throws
/// <see cref="NotSupportedException"/> for any other type. Every Tracon endpoint
/// passes <see cref="TraconRunOptions"/>, so the middleware threw on every run
/// until <c>RunRecordingAgent</c> stopped handing that type inward (MAF
/// capability matrix, G-07). The two tests cover the two run paths of
/// <c>RunRecordingAgent</c>: the SSE endpoint streams, the queued run buffers.
/// </remarks>
public sealed class MafFunctionMiddlewareTests
{
    private const string AgentName = "middleware-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task Streaming_run_passes_every_tool_call_through_the_consumer_middleware()
    {
        var middleware = new CountingFunctionMiddleware();
        await using var host = await StartAsync(middleware);

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "look it up", SessionId = "s-middleware-stream" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await response.Content.ReadAsStringAsync();

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs?sessionId=s-middleware-stream", UriKind.Relative));
        var run = (await TraconTestHost.ReadJsonAsync(runs)).EnumerateArray().ShouldHaveSingleItem();

        run.GetProperty("status").GetString().ShouldBe("Completed");
        middleware.ToolNames.ShouldBe(["lookup"]);
    }

    [Fact]
    public async Task Queued_run_passes_every_tool_call_through_the_consumer_middleware()
    {
        var middleware = new CountingFunctionMiddleware();
        await using var host = await StartAsync(
            middleware,
            static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "look it up", SessionId = "s-middleware-queued" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
        middleware.ToolNames.ShouldBe(["lookup"]);
    }

    private static Task<TraconTestHost> StartAsync(
        CountingFunctionMiddleware middleware,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("middleware-model")
                    .CallsTools(("lookup", null))
                    .RespondsWith("found it"))
                .AddTool((Func<string>)(static () => "42"), name: "lookup")
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Look the answer up.",
                    Model = new ModelBinding { Provider = "middleware-model", Model = "middleware-1" },
                    ToolNames = ["lookup"],
                })
                .AddAgentDecorator(middleware),
            configureServices: configureServices);

    /// <summary>
    /// Records the name of every tool call the MAF middleware sees. Order 100
    /// places it inside Tracon's own decorators (0, 10, 20), next to the agent.
    /// </summary>
    private sealed class CountingFunctionMiddleware : IAgentDecorator
    {
        private readonly List<string> _toolNames = [];

        public IReadOnlyList<string> ToolNames
        {
            get
            {
                lock (_toolNames)
                {
                    return [.. _toolNames];
                }
            }
        }

        public int Order => 100;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
            => agent.AsBuilder()
                .Use(async (innerAgent, context, next, cancellationToken) =>
                {
                    lock (_toolNames)
                    {
                        _toolNames.Add(context.Function.Name);
                    }

                    return await next(context, cancellationToken).ConfigureAwait(false);
                })
                .Build();
    }
}
