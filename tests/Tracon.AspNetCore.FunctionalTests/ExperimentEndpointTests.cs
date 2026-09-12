using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>In-memory behavior tests for the A/B experiment endpoints (Phase 19.3-19.4).</summary>
public sealed class ExperimentEndpointTests
{
    private static readonly Uri Experiments = new("/tracon/api/experiments", UriKind.Relative);
    private static readonly Uri Experiment = new("/tracon/api/experiments/version-comparison", UriKind.Relative);
    private static readonly Uri Start = new("/tracon/api/experiments/version-comparison/start", UriKind.Relative);
    private static readonly Uri Stop = new("/tracon/api/experiments/version-comparison/stop", UriKind.Relative);
    private static readonly Uri Results = new("/tracon/api/experiments/version-comparison/results", UriKind.Relative);
    private static readonly Uri Canary = new("/tracon/api/experiments/version-comparison/canary", UriKind.Relative);

    [Fact]
    public async Task Experiment_is_created_and_listed()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var created = await host.Client.PutAsJsonAsync(Experiment, Request());
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(created);
        body.GetProperty("status").GetString().ShouldBe("Draft");
        body.GetProperty("variants").GetArrayLength().ShouldBe(2);

        using var listed = await host.Client.GetAsync(Experiments);
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Rejected_when_weight_total_is_not_100()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 40 },
                    new ExperimentVariant { Name = "v2", Version = 2, Weight = 40 },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Experiment_cannot_be_set_up_for_a_code_based_agent()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition(name: "code-agent")));

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with { AgentName = "code-agent" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("version history");
    }

    [Fact]
    public async Task Variant_with_a_nonexistent_version_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var response = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                    new ExperimentVariant { Name = "v2", Version = 99, Weight = 50 },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lifecycle_starts_and_stops()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var started = await host.Client.PostAsJsonAsync(Start, new { });
        started.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(started)).GetProperty("status").GetString().ShouldBe("Running");

        using var stopped = await host.Client.PostAsJsonAsync(Stop, new { });
        stopped.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(stopped)).GetProperty("status").GetString().ShouldBe("Stopped");
    }

    [Fact]
    public async Task A_second_running_experiment_for_the_same_agent_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PostAsJsonAsync(Start, new { });

        var secondUri = new Uri("/tracon/api/experiments/second-experiment", UriKind.Relative);
        using (var created = await host.Client.PutAsJsonAsync(secondUri, Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var secondStart = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/experiments/second-experiment/start", UriKind.Relative), new { });

        secondStart.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cannot_be_edited_while_running()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PostAsJsonAsync(Start, new { });

        using var response = await host.Client.PutAsJsonAsync(Experiment, Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Results_endpoint_returns_an_empty_list_for_an_empty_experiment()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var response = await host.Client.GetAsync(Results);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Canary_policy_is_defined_and_read()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var set = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(set)).GetProperty("canary").GetProperty("canaryVariant").GetString().ShouldBe("v2");

        using var fetched = await host.Client.GetAsync(Canary);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(fetched);
        body.GetProperty("policy").GetProperty("canaryVariant").GetString().ShouldBe("v2");

        // No runs yet -- evaluation returns "insufficient data", NOT a rollback.
        body.GetProperty("evaluation").GetProperty("decision").GetString().ShouldBe("InsufficientData");
    }

    [Fact]
    public async Task Canary_policy_is_removed_with_a_null_body()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());
        await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
        using var cleared = await host.Client.PutAsync(Canary, content);
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(cleared)).GetProperty("canary").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        using var fetched = await host.Client.GetAsync(Canary);
        var body = await TraconTestHost.ReadJsonAsync(fetched);
        body.GetProperty("policy").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("evaluation").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Canary_is_rejected_for_an_experiment_with_other_than_two_variants()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);

        using var created = await host.Client.PutAsJsonAsync(
            Experiment,
            Request() with
            {
                Variants =
                [
                    new ExperimentVariant { Name = "control", Version = 1, Weight = 34 },
                    new ExperimentVariant { Name = "v2", Version = 2, Weight = 33 },
                    new ExperimentVariant { Name = "v3", Version = 1, Weight = 33 },
                ],
            });
        created.EnsureSuccessStatusCode();

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("two-variant");
    }

    [Fact]
    public async Task Canary_with_a_nonexistent_variant_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();
        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Experiment, Request());

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy() with { CanaryVariant = "missing" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Canary_for_a_nonexistent_experiment_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Canary, CanaryPolicy());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static CanaryPolicy CanaryPolicy()
        => new()
        {
            CanaryVariant = "v2",
            MaxErrorRateDelta = 0.1,
            MinSampleSize = 20,
        };

    /// <summary>Creates a database agent named "customer-support-agent" with two versions.</summary>
    private static async Task CreateVersionedAgentAsync(TraconTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request(name: "customer-support-agent", instructions: "first"));
        created.EnsureSuccessStatusCode();

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/customer-support-agent", UriKind.Relative),
            TestData.Request(name: "customer-support-agent", instructions: "second"));
        updated.EnsureSuccessStatusCode();
    }

    private static ExperimentSaveRequest Request()
        => new()
        {
            AgentName = "customer-support-agent",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };
}
