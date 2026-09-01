using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Same rationale as ToolGovernanceEndpointTests.cs (K-269): the package's
// AgentPrism.Testing.AgentPrismTestHost and this project's own AgentPrismTestHost
// share the same name; a blanket `using AgentPrism.Testing;` would produce CS0104.
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>AddScopedTool</c> (docs/127, 127.3), driven through a REAL
/// <c>FunctionInvokingChatClient</c> loop and the FULL application's own root
/// provider — not by constructing <c>ScopedAIFunction</c> directly. The
/// unit-level coverage in <c>AgentPrism.Core.UnitTests</c> proves the wrapper's
/// own mechanics against a bare <c>ServiceCollection</c>; this class proves the
/// DI boundary <c>AddScopedTool</c> itself crosses: the scope factory it
/// resolves at call time is the application's real one.
/// </summary>
public sealed class ScopedToolLifetimeTests
{
    private const string AgentName = "scoped-tool-agent";

    private static async Task<HttpResponseMessage> PostQueuedAsync(AgentPrismTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }

    /// <summary>Polls a run until it reaches the expected status. See ApprovalEndpointTests.cs for the 30s rationale.</summary>
    private static async Task<string> WaitForStatusAsync(AgentPrismTestHost host, Guid runId, string expected)
    {
        var uri = new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var poll = await host.Client.GetAsync(uri);
            status = (await AgentPrismTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                return status!;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            $"Run {runId} did not reach status '{expected}' within 30 seconds; last seen status: '{status}'.");
    }

    /// <summary>A scoped dependency whose identity and disposal are observable from outside the scope.</summary>
    private sealed class ScopedRepository : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>Shared across the whole app so a tool call can report what it saw.</summary>
    private sealed class ObservedScopes
    {
        public List<ScopedRepository> Seen { get; } = [];

        public object Gate { get; } = new();
    }

    [Fact]
    public async Task Each_call_gets_a_fresh_scope_that_closes_once_the_call_completes()
    {
        var observed = new ObservedScopes();

        await using var host = await AgentPrismTestHost.StartAsync(
            builder =>
            {
                builder.Services.AddScoped<ScopedRepository>();
                builder.Services.AddSingleton(observed);

                builder
                    .AddModelProvider(new FakeModelProvider("scoped-model")
                        .CallsTool("get_order", new { orderId = "ORD-1" })
                        .EchoesLastToolResult())
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "scoped-model", Model = "scoped-1" },
                        ToolNames = ["get_order"],
                    });

                builder.AddScopedTool(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        (string orderId, Microsoft.Extensions.AI.AIFunctionArguments arguments) =>
                        {
                            var repository = arguments.Services!.GetRequiredService<ScopedRepository>();

                            lock (observed.Gate)
                            {
                                observed.Seen.Add(repository);
                            }

                            return $"{orderId} found via scope {repository.Id}.";
                        },
                        "get_order"));
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "look up the order", SessionId = "s-scoped-1" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        var repository = observed.Seen.ShouldHaveSingleItem();

        // The scope closed once InvokeCoreAsync returned - well before this
        // assertion runs, since the run itself already completed.
        repository.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Two_sequential_calls_each_get_their_own_scope()
    {
        var observed = new ObservedScopes();

        await using var host = await AgentPrismTestHost.StartAsync(
            builder =>
            {
                builder.Services.AddScoped<ScopedRepository>();
                builder.Services.AddSingleton(observed);

                builder
                    .AddModelProvider(new FakeModelProvider("scoped-model")
                        .CallsTools(("get_order", new { orderId = "ORD-1" }), ("get_order", new { orderId = "ORD-2" }))
                        .RespondsWith("done"))
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "scoped-model", Model = "scoped-1" },
                        ToolNames = ["get_order"],
                    });

                builder.AddScopedTool(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        (string orderId, Microsoft.Extensions.AI.AIFunctionArguments arguments) =>
                        {
                            var repository = arguments.Services!.GetRequiredService<ScopedRepository>();

                            lock (observed.Gate)
                            {
                                observed.Seen.Add(repository);
                            }

                            return $"{orderId} found.";
                        },
                        "get_order"));
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "look up two orders", SessionId = "s-scoped-2" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        observed.Seen.Count.ShouldBe(2);
        observed.Seen[0].Id.ShouldNotBe(observed.Seen[1].Id);
        observed.Seen.ShouldAllBe(static repository => repository.Disposed);
    }

    [Fact]
    public async Task Two_concurrent_calls_never_share_a_scope()
    {
        var observed = new ObservedScopes();
        using var barrier = new Barrier(2);

        await using var host = await AgentPrismTestHost.StartAsync(
            builder =>
            {
                builder.Services.AddScoped<ScopedRepository>();
                builder.Services.AddSingleton(observed);

                builder
                    .AddModelProvider(new FakeModelProvider("scoped-model")
                        .CallsTools(("tool_a", null), ("tool_b", null))
                        .RespondsWith("done"))
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding
                        {
                            Provider = "scoped-model",
                            Model = "scoped-1",
                            AllowConcurrentToolCalls = true,
                        },
                        ToolNames = ["tool_a", "tool_b"],
                    });

                foreach (var name in new[] { "tool_a", "tool_b" })
                {
                    builder.AddScopedTool(
                        Microsoft.Extensions.AI.AIFunctionFactory.Create(
                            (Microsoft.Extensions.AI.AIFunctionArguments arguments) => Task.Run(() =>
                            {
                                var repository = arguments.Services!.GetRequiredService<ScopedRepository>();

                                lock (observed.Gate)
                                {
                                    observed.Seen.Add(repository);
                                }

                                // Neither call may return before BOTH have
                                // resolved their own scope - guarantees genuine
                                // overlap instead of a Task.Delay approximation.
                                barrier.SignalAndWait(TimeSpan.FromSeconds(10));

                                return "ok";
                            }),
                            name));
                }
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "run both", SessionId = "s-scoped-concurrent" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        observed.Seen.Count.ShouldBe(2);
        observed.Seen[0].Id.ShouldNotBe(observed.Seen[1].Id);
        observed.Seen.ShouldAllBe(static repository => repository.Disposed);
    }
}
