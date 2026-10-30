using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What <c>PUT /api/workflows/{name}</c> accepts for a node's <c>timeout</c> and a
/// definition's <c>runTimeout</c> (failure modes 9, 26 and 28), and what the
/// function catalog shows.
/// </summary>
/// <remarks>
/// <para>
/// A value a timer cannot arm is refused at the save, with a <c>400</c> that names the
/// field: armed in a run, it would throw long after the operator left. The host's
/// ceiling (<c>Tracon:Workflows:MaxRunTimeout</c>) is read through the engine's seam,
/// because this package cannot see <c>TraconWorkflowOptions</c>.
/// </para>
/// <para>
/// The values travel as <c>hh:mm:ss</c> strings in both directions; the round trip
/// through the store is the store contract's (<c>WorkflowDefinitionStoreContract</c>).
/// </para>
/// </remarks>
public sealed class WorkflowTimeoutSaveTests
{
    private const string Writer = "writer";
    private const string Render = "render";

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    [InlineData("50.00:00:00")]
    public async Task A_function_nodes_timeout_a_timer_cannot_arm_is_a_400(string timeout)
    {
        await using var host = await StartAsync();

        using var saved = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent"), Node(Render, "Function", timeout)]);

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var detail = await DetailAsync(saved);
        detail.ShouldContain($"'{Render}'");
        detail.ShouldContain("'timeout'");
    }

    [Fact]
    public async Task An_agent_nodes_timeout_is_accepted_and_read_back()
    {
        // The plan's first draft refused it; the earlier decision made it the step's own deadline.
        await using var host = await StartAsync();

        using (var saved = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent", "00:06:00"), Node(Render, "Function", "00:00:02.5")]))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var read = await host.Client.GetFromJsonAsync<JsonElement>("/tracon/api/workflows/chain", TestContext.Current.CancellationToken);
        var nodes = read.GetProperty("nodes");

        nodes[0].GetProperty("timeout").GetString().ShouldBe("00:06:00");
        nodes[1].GetProperty("timeout").GetString().ShouldBe("00:00:02.5000000");
    }

    [Fact]
    public async Task An_agent_nodes_timeout_with_no_room_for_its_hard_cutoff_is_a_400()
    {
        await using var host = await StartAsync();

        using var saved = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent", "49.17:02:47.2950000")]);

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailAsync(saved)).ShouldContain("hard cutoff");
    }

    [Fact]
    public async Task Without_a_ceiling_a_run_timeout_above_the_installations_is_a_400_that_names_the_ceiling()
    {
        await using var host = await StartAsync();

        using (var longer = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent")], runTimeout: "00:30:00"))
        {
            longer.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await DetailAsync(longer)).ShouldContain("Tracon:Workflows:MaxRunTimeout");
        }

        using var shorter = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent")], runTimeout: "00:03:00");

        shorter.StatusCode.ShouldBe(HttpStatusCode.OK, await shorter.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_a_ceiling_a_run_timeout_may_extend_up_to_it_and_not_past_it()
    {
        await using var host = await StartAsync(static options => options.MaxRunTimeout = TimeSpan.FromHours(1));

        using (var within = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent")], runTimeout: "00:30:00"))
        {
            within.StatusCode.ShouldBe(HttpStatusCode.OK, await within.Content.ReadAsStringAsync());
        }

        var read = await host.Client.GetFromJsonAsync<JsonElement>("/tracon/api/workflows/chain", TestContext.Current.CancellationToken);
        read.GetProperty("runTimeout").GetString().ShouldBe("00:30:00");

        using var past = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent")], runTimeout: "01:00:01");

        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailAsync(past)).ShouldContain("01:00:00");
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("50.00:00:00")]
    public async Task A_run_timeout_a_timer_cannot_arm_is_a_400_whatever_the_ceiling(string runTimeout)
    {
        await using var host = await StartAsync(static options => options.MaxRunTimeout = TimeSpan.FromDays(40));

        using var saved = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent")], runTimeout: runTimeout);

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailAsync(saved)).ShouldContain("'runTimeout'");
    }

    [Fact]
    public async Task A_save_without_the_fields_clears_them()
    {
        // PUT replaces the definition: the console must send both fields
        // back on every save, and the server must not keep an omitted one.
        await using var host = await StartAsync(static options => options.MaxRunTimeout = TimeSpan.FromHours(1));

        using (var first = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent"), Node(Render, "Function", "00:00:05")], runTimeout: "00:20:00"))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var second = await SaveAsync(host, "chain", nodes: [Node(Writer, "Agent"), Node(Render, "Function")]))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var definition = (await host.Services.GetRequiredService<IWorkflowDefinitionStore>()
            .GetAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, "chain", TestContext.Current.CancellationToken))!;

        definition.RunTimeout.ShouldBeNull();
        definition.Nodes[1].Timeout.ShouldBeNull();
    }

    [Fact]
    public async Task The_function_catalog_shows_the_registered_timeout()
    {
        await using var host = await StartAsync();

        var functions = await host.Client.GetFromJsonAsync<JsonElement>("/tracon/api/workflows/functions", TestContext.Current.CancellationToken);
        var render = functions.EnumerateArray().Single(static function => string.Equals(function.GetProperty("name").GetString(), Render, StringComparison.Ordinal));
        var plain = functions.EnumerateArray().Single(static function => string.Equals(function.GetProperty("name").GetString(), "plain", StringComparison.Ordinal));

        render.GetProperty("timeout").GetString().ShouldBe("00:02:00");
        plain.GetProperty("timeout").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static Task<TraconTestHost> StartAsync(Action<TraconWorkflowOptions>? workflows = null)
        => TraconTestHost.StartAsync(builder =>
        {
            builder
                .AddAgent(TestData.Definition(Writer))
                .UseWorkflows(workflows)
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    Render,
                    static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
                    static options => options.Timeout = TimeSpan.FromMinutes(2))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "plain",
                    static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));
        });

    private static object Node(string name, string kind, string? timeout = null)
        => timeout is null ? new { name, kind } : new { name, kind, timeout };

    private static Task<HttpResponseMessage> SaveAsync(TraconTestHost host, string name, object[] nodes, string? runTimeout = null)
        => host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{name}",
            runTimeout is null
                ? new { kind = "Sequential", nodes }
                : (object)new { kind = "Sequential", nodes, runTimeout });

    private static async Task<string> DetailAsync(HttpResponseMessage response)
        => (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString() ?? string.Empty;
}
