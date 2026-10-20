using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-598 — the members of <c>ModelBinding.ToolLoop</c> that change what the loop does, through the whole host:
/// HTTP definition, store, compiler, model pipeline and the real <c>FunctionInvokingChatClient</c>.
/// </summary>
/// <remarks>
/// <c>AllowConcurrentToolCalls</c> is proven with a barrier in <c>ConcurrentToolInvocationTests</c>. The two
/// members here are measured by the model calls the loop makes: a tolerated failure goes back to the model, an
/// intolerable one ends the run before the next call.
/// </remarks>
public sealed class ToolLoopSettingsEndpointTests
{
    private const string AgentName = "loop-settings-agent";
    private const string ProviderName = "loop-settings-model";

    [Fact]
    public async Task By_default_a_failing_tool_goes_back_to_the_model_which_then_answers()
    {
        var provider = Provider("boom");
        await using var host = await StartAsync(provider);

        await SaveAsync(host, toolLoop: null);

        using var run = await RunAsync(host);

        run.IsSuccessStatusCode.ShouldBeTrue(await run.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(2, "the loop gives the model the failure and asks again");
    }

    [Fact]
    public async Task Zero_tolerated_errors_ends_the_run_at_the_first_failed_tool_without_asking_the_model_again()
    {
        var provider = Provider("boom");
        await using var host = await StartAsync(provider);

        await SaveAsync(host, toolLoop: new { maximumConsecutiveErrors = 0 });

        using var run = await RunAsync(host);

        run.IsSuccessStatusCode.ShouldBeFalse("the loop rethrows the tool's failure");
        provider.Requests.Count.ShouldBe(1, "no second model call after the rethrown failure");
    }

    [Fact]
    public async Task By_default_a_call_to_a_tool_the_agent_does_not_have_gets_an_error_result_and_the_model_answers()
    {
        var provider = Provider("ghost");
        await using var host = await StartAsync(provider);

        await SaveAsync(host, toolLoop: null);

        using var run = await RunAsync(host);

        run.IsSuccessStatusCode.ShouldBeTrue(await run.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Ending_the_loop_on_an_unknown_tool_stops_before_a_second_model_call()
    {
        var provider = Provider("ghost");
        await using var host = await StartAsync(provider);

        await SaveAsync(host, toolLoop: new { terminateOnUnknownCalls = true });

        // How the run ends once the loop hands a call back is not what this test measures.
        (await RunAsync(host)).Dispose();

        provider.Requests.Count.ShouldBe(1, "the loop hands the unknown call back instead of answering it");
    }

    private static FakeModelProvider Provider(string toolToCall)
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = "loop-1" })
            .CallsTool(toolToCall)
            .RespondsWith("all done");

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool((Func<string>)(() => throw new InvalidOperationException("the tool failed")), name: "boom", description: "Always fails.")
            .AddTool(() => "found", name: "lookup", description: "Looks something up."));

    private static async Task SaveAsync(TraconTestHost host, object? toolLoop)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new
            {
                name = AgentName,
                instructions = "Use the tool, then answer.",
                toolNames = new[] { "boom", "lookup" },
                model = new { provider = ProviderName, model = "loop-1", toolLoop },
            });

        created.IsSuccessStatusCode.ShouldBeTrue(await created.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "go" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }
}
