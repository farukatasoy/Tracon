using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>PUT /api/workflows/{name}</c> refuses a sequential chain that is longer than a run may take, and says how long it may be.
/// </summary>
/// <remarks>
/// A chain of 2 000 steps used to be saved with a <c>200</c>, and every run of it ended <c>Failed</c> at the super-step limit
/// (<c>Tracon:Workflows:MaxSuperSteps</c>, 100) after 11 to 23 seconds. The limit is the host's setting, read through the
/// engine's seam, so the cases here set a small one and keep the chains short. How many super-steps a chain needs is measured
/// against the engine in <c>WorkflowSequentialStepCountTests</c>.
/// </remarks>
public sealed class WorkflowStepCountSaveTests
{
    private const int Limit = 5;

    [Fact]
    public async Task A_chain_of_nodes_as_long_as_the_limit_is_saved_and_one_longer_is_a_400_that_names_the_limit()
    {
        await using var host = await StartAsync();

        using (var atTheLimit = await SaveNodesAsync(host, Limit))
        {
            atTheLimit.StatusCode.ShouldBe(HttpStatusCode.OK, await atTheLimit.Content.ReadAsStringAsync());
        }

        using var past = await SaveNodesAsync(host, Limit + 1);

        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = await DetailAsync(past);

        detail.ShouldContain("'chain'");
        detail.ShouldContain("6 steps");
        detail.ShouldContain($"Tracon:Workflows:MaxSuperSteps");
        detail.ShouldContain($"at most {Limit} steps");
    }

    [Fact]
    public async Task A_chain_of_agent_names_needs_one_super_step_more_so_one_step_less_fits()
    {
        await using var host = await StartAsync();

        using (var fits = await SaveAgentNamesAsync(host, Limit - 1))
        {
            fits.StatusCode.ShouldBe(HttpStatusCode.OK, await fits.Content.ReadAsStringAsync());
        }

        using var past = await SaveAgentNamesAsync(host, Limit);

        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailAsync(past)).ShouldContain($"at most {Limit - 1} steps");
    }

    [Fact]
    public async Task A_save_that_replaces_a_workflow_with_a_chain_that_is_too_long_leaves_the_stored_one_in_place()
    {
        await using var host = await StartAsync();

        using (var first = await SaveNodesAsync(host, 2))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var refused = await SaveNodesAsync(host, Limit + 3))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var read = await host.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/tracon/api/workflows/chain", TestContext.Current.CancellationToken);

        read.GetProperty("nodes").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_concurrent_workflow_with_more_agents_than_the_limit_is_saved()
    {
        // Only a sequential chain takes a super-step for each of its steps.
        await using var host = await StartAsync();

        var names = Enumerable.Range(0, Limit * 3).Select(static index => $"agent{index}").ToArray();
        using var saved = await host.Client.PutAsJsonAsync("/tracon/api/workflows/wide", new { kind = "Concurrent", agentNames = names });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(builder =>
        {
            for (var index = 0; index < Limit * 3; index++)
            {
                builder.AddAgent(TestData.Definition($"agent{index}"));
            }

            builder.UseWorkflows(static options => options.MaxSuperSteps = Limit);
        });

    private static Task<HttpResponseMessage> SaveNodesAsync(TraconTestHost host, int steps)
        => host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/chain",
            new
            {
                kind = "Sequential",
                nodes = Enumerable.Range(0, steps).Select(static index => new { name = $"agent{index}", kind = "Agent" }).ToArray(),
            });

    private static Task<HttpResponseMessage> SaveAgentNamesAsync(TraconTestHost host, int steps)
        => host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/chain",
            new { kind = "Sequential", agentNames = Enumerable.Range(0, steps).Select(static index => $"agent{index}").ToArray() });

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString() ?? string.Empty;
}
