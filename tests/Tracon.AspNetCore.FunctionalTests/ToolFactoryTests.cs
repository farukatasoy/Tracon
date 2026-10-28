using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs (K-269).
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 258 (F-752): <c>AddTool(provider =&gt; ...)</c> builds a tool from the host's service provider,
/// the compile-time answer to the empty provider a tool sees at call time (K-218).
/// </summary>
/// <remarks>
/// <para>
/// DI and HTTP tests on purpose. The claim is about the container: the factory runs against the final
/// provider, once, and what it resolved reaches the tool in a real run. A unit test over a bare
/// <c>ServiceCollection</c> cannot show either.
/// </para>
/// <para>
/// Failure modes asked: cancellation (no new path), concurrency (the registry is a singleton, so the
/// factory runs once however many runs follow), empty input (a null tool, an invalid name), another tenant
/// (the tool is shared by every tenant, like every singleton tool), subsystem failure (a factory that asks
/// for the registry it is part of).
/// </para>
/// </remarks>
public sealed class ToolFactoryTests
{
    private const string AgentName = "tool-factory-agent";
    private const string SecondAgentName = "tool-factory-agent-2";

    [Fact]
    public async Task The_dependency_reaches_the_tool_in_a_run()
    {
        var calls = new FactoryCalls();

        await using var host = await StartQueuedAsync(calls);

        var runId = await QueueAsync(host, "s-tool-factory-1");

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        // The tool read the repository its factory took from the provider; MAF gives the tool an empty provider.
        calls.Results.ShouldBe(["ORD-1 is open (repository A)"]);

        var tools = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative),
            TestContext.Current.CancellationToken);

        tools.ShouldNotBeNull().ShouldHaveSingleItem().ToolName.ShouldBe("get_order_status");
    }

    [Fact]
    public async Task The_factory_runs_once_however_many_runs_follow()
    {
        var calls = new FactoryCalls();

        await using var host = await StartQueuedAsync(calls);

        // Two agents, so each run is a model turn that calls the tool (the fake provider's script is spent by one run).
        foreach (var (agent, session) in new[] { (AgentName, "s-tool-factory-2"), (SecondAgentName, "s-tool-factory-3") })
        {
            var runId = await QueueAsync(host, session, agent);

            (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
        }

        calls.FactoryRuns.ShouldBe(1);
        calls.Results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_configure_callback_reaches_the_registered_tool()
    {
        var calls = new FactoryCalls();

        await using var host = await StartQueuedAsync(calls);

        var registry = host.Services.GetRequiredService<IToolRegistry>();

        registry.TryGet("get_order_status", out _).ShouldBeTrue();

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.RequiredPermission.ShouldBe("orders.read");
        descriptor.Effect.ShouldBe(ToolEffect.Read);
    }

    [Fact]
    public async Task A_factory_that_asks_for_the_tool_registry_fails_with_a_named_error()
    {
        // The registry is built from the result of the factory, so a factory that asks for the registry
        // asks for itself. The container does not see through a factory delegate and recurses until the
        // process dies with a stack overflow; the registry stops it with an error that names the cause.
        var exception = await Should.ThrowAsync<TraconException>(async () => await TraconTestHost.StartAsync(
            static builder => builder.AddTool(static provider =>
            {
                _ = provider.GetRequiredService<IToolRegistry>();

                return AIFunctionFactory.Create(static () => "ok", "needs_registry");
            })));

        exception.Message.ShouldContain("IToolRegistry");
        exception.Message.ShouldContain("AddTool");
    }

    [Fact]
    public async Task A_factory_that_asks_for_a_service_that_needs_the_registry_fails_the_same_way()
    {
        // IAgentCatalog reaches IToolRegistry through the compiler: the guard catches the loop at the registry,
        // not at the service the factory happened to name.
        var exception = await Should.ThrowAsync<TraconException>(async () => await TraconTestHost.StartAsync(
            static builder => builder.AddTool(static provider =>
            {
                _ = provider.GetRequiredService<IAgentCatalog>();

                return AIFunctionFactory.Create(static () => "ok", "needs_catalog");
            })));

        exception.Message.ShouldContain("IToolRegistry");
    }

    [Fact]
    public async Task A_factory_that_returns_null_stops_the_host_with_a_named_error()
    {
        var exception = await Should.ThrowAsync<TraconException>(async () => await TraconTestHost.StartAsync(
            static builder => builder.AddTool(static (IServiceProvider _) => null!)));

        exception.Message.ShouldContain("AddTool");
        exception.Message.ShouldContain("null");
    }

    [Fact]
    public async Task A_factory_that_builds_a_tool_with_an_invalid_name_stops_the_host()
    {
        var exception = await Should.ThrowAsync<TraconException>(async () => await TraconTestHost.StartAsync(
            static builder => builder.AddTool(static provider =>
            {
                _ = provider;

                return AIFunctionFactory.Create(static () => "ok", "Not A Valid Name");
            })));

        exception.Message.ShouldContain("Not A Valid Name");
    }

    [Fact]
    public async Task A_factory_that_throws_stops_the_host_with_its_own_error()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await TraconTestHost.StartAsync(
            static builder => builder.AddTool(static (IServiceProvider _) => throw new InvalidOperationException("The repository is missing."))));

        exception.Message.ShouldContain("The repository is missing.");
    }

    private static Task<TraconTestHost> StartQueuedAsync(FactoryCalls calls)
        => TraconTestHost.StartAsync(builder =>
        {
            builder.Services.AddSingleton(calls);
            builder.Services.AddSingleton(new OrderRepository("repository A"));

            builder
                .AddModelProvider(new FakeModelProvider("tool-factory-model")
                    .CallsTool("get_order_status", new { orderId = "ORD-1" })
                    .EchoesLastToolResult())
                .AddModelProvider(new FakeModelProvider("tool-factory-model-2")
                    .CallsTool("get_order_status", new { orderId = "ORD-1" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "tool-factory-model", Model = "tool-factory-1" },
                    ToolNames = ["get_order_status"],
                })
                .AddAgent(new AgentDefinition
                {
                    Name = SecondAgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "tool-factory-model-2", Model = "tool-factory-1" },
                    ToolNames = ["get_order_status"],
                })
                .AddTool(
                    provider =>
                    {
                        var repository = provider.GetRequiredService<OrderRepository>();
                        var factoryCalls = provider.GetRequiredService<FactoryCalls>();

                        Interlocked.Increment(ref factoryCalls.FactoryRunCount);

                        return AIFunctionFactory.Create(
                            (string orderId) =>
                            {
                                var result = $"{orderId} is open ({repository.Name})";

                                factoryCalls.Record(result);

                                return result;
                            },
                            "get_order_status");
                    },
                    options => options.RequiredPermission = "orders.read")
                .UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
        });

    private static async Task<Guid> QueueAsync(TraconTestHost host, string sessionId, string agentName = AgentName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "is order ORD-1 open?", SessionId = sessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    /// <summary>A dependency the factory takes from the provider and the tool reads at call time.</summary>
    private sealed class OrderRepository(string name)
    {
        public string Name { get; } = name;
    }

    /// <summary>Counts how often the factory ran and what the tool returned.</summary>
    private sealed class FactoryCalls
    {
        private readonly List<string> _results = [];

        public int FactoryRunCount;

        public int FactoryRuns => Volatile.Read(ref FactoryRunCount);

        public IReadOnlyList<string> Results
        {
            get
            {
                lock (_results)
                {
                    return [.. _results];
                }
            }
        }

        public void Record(string result)
        {
            lock (_results)
            {
                _results.Add(result);
            }
        }
    }
}
