using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The provider's key is in no output of a job that a cron schedule opens and the built-in handlers run: a queued agent
/// run, an agent batch and a workflow batch, while the provider accepts and while it refuses and quotes the key.
/// </summary>
/// <remarks>
/// <para>
/// The only queued run the other canary classes drive is the one a client starts with <c>Prefer: respond-async</c>. A
/// scheduled job takes another way to the same failure: the dispatcher opens the job when the clock reaches the schedule's
/// time, the worker runs a handler that reads a payload written by hand, and the handler writes the failure to the job's
/// error text, the job item, the run and the log. The clock is the test's own: the schedules are due after it moves.
/// </para>
/// <para>
/// A search that finds nothing proves nothing unless the jobs ran, so the test asks for jobs that completed in the accepted
/// round and jobs that failed in the refused one, for the number of model calls that reached the provider, and for the key the
/// provider was sent. Each schedule is switched off once its job has ended, so it fires once.
/// </para>
/// </remarks>
public sealed class ScheduledJobSecretCanaryTests
{
    private const string Workflow = "scheduled-chain";

    [Theory]
    [InlineData(CanaryKeyKind.Setup)]
    [InlineData(CanaryKeyKind.Tenant)]
    public async Task The_provider_key_is_in_no_output_of_a_scheduled_job_of_a_built_in_handler(CanaryKeyKind kind)
    {
        const string Scenario = "scheduled-job";

        var clock = new ManualTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));

        using var database = new TempSqliteDatabase("scheduled-job-canary");

        await using var canary = await KeyedCanaryHost.StartAsync(
            kind,
            Scenario,
            database,
            configure: static builder => builder
                .UseWorkflows()
                .UseScheduling(static options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                    options.MaxAttempts = 1;
                }),
            configureServices: services => services.AddSingleton<TimeProvider>(clock),
            withTool: false);

        var drive = canary.Drive;
        var wire = canary.Wire;

        (await drive.SendAsync(
            HttpMethod.Put,
            $"/tracon/api/workflows/{Workflow}",
            new { description = "One step.", kind = "Sequential", agentNames = new[] { KeyedCanaryHost.AgentName } })).ShouldBe(HttpStatusCode.OK);

        var rounds = new List<(string Name, CanaryWireServer.Behavior Mode, string[] Outcome)>
        {
            ("accepted", CanaryWireServer.Behavior.Accept, ["Completed"]),
            ("refused", CanaryWireServer.Behavior.RefuseAndQuote, ["Failed", "Completed"]),
        };

        var statuses = new List<string>();
        var reached = 0;
        var jobsSeen = 0;

        foreach (var (name, mode, _) in rounds)
        {
            wire.Mode = mode;
            var before = wire.Requests;

            var schedules = new (string Name, object Body)[]
            {
                ($"{name}-run", new
                {
                    handlerKey = "tracon.agent-run",
                    targetName = KeyedCanaryHost.AgentName,
                    cron = "* * * * *",
                    timeZone = "UTC",
                    enabled = true,
                    payload = new { runId = Guid.NewGuid(), message = "hello" },
                }),
                ($"{name}-batch", new
                {
                    handlerKey = "tracon.agent-batch",
                    targetName = KeyedCanaryHost.AgentName,
                    cron = "* * * * *",
                    timeZone = "UTC",
                    enabled = true,
                    payload = new[] { "first", "second" },
                }),
                ($"{name}-workflow", new
                {
                    handlerKey = "tracon.workflow",
                    targetName = Workflow,
                    cron = "* * * * *",
                    timeZone = "UTC",
                    enabled = true,
                    payload = new[] { "hello" },
                }),
            };

            foreach (var (scheduleName, body) in schedules)
            {
                (await drive.SendAsync(HttpMethod.Put, $"/tracon/api/schedules/{scheduleName}", body)).ShouldBe(HttpStatusCode.OK);
            }

            // The schedules are due once the clock passes the next minute.
            clock.Advance(TimeSpan.FromMinutes(2));

            var expected = jobsSeen + schedules.Length;

            await WaitUntil.TrueAsync(
                async () =>
                {
                    var jobs = await ListJobsAsync(canary);

                    return jobs.Count >= expected && jobs.All(static job => job.Status is "Completed" or "Failed" or "Cancelled");
                },
                $"the {name} jobs to end",
                TimeSpan.FromSeconds(60));

            var ended = await ListJobsAsync(canary);

            jobsSeen = ended.Count;
            statuses.AddRange(ended.Select(job => $"{name}:{job.HandlerKey}:{job.Status}"));
            reached += wire.Requests - before;

            // Each schedule fires once: switch it off now that its job has ended.
            foreach (var (scheduleName, body) in schedules)
            {
                (await drive.SendAsync(HttpMethod.Put, $"/tracon/api/schedules/{scheduleName}", WithEnabled(body, enabled: false))).ShouldBe(HttpStatusCode.OK);
            }
        }

        // What the operator reads afterwards.
        var reader = canary.NewDrive();

        foreach (var job in await ListJobsAsync(canary))
        {
            await reader.SendAsync(HttpMethod.Get, $"/tracon/api/jobs/{job.Id}");
        }

        await reader.SendAsync(HttpMethod.Get, "/tracon/api/jobs?limit=200");
        await reader.SendAsync(HttpMethod.Get, "/tracon/api/schedules");

        var answered = await reader.ReadEverythingAsync();
        var bytes = database.ReadAllBytes();

        // Proof that the jobs ran and reached the provider.
        answered.ShouldBeGreaterThan(40, "the read sweep reached too few operations for an empty search to mean anything");
        reached.ShouldBeGreaterThanOrEqualTo(6, "the scheduled jobs did not reach the provider on every handler: " + string.Join(", ", statuses));
        wire.Credentials.ShouldContain(credential => credential.Contains(KeyedCanaryHost.KeyOf(kind, Scenario), StringComparison.Ordinal), "the provider was never sent the key");
        statuses.Count(static status => status.StartsWith("accepted:", StringComparison.Ordinal) && status.EndsWith(":Completed", StringComparison.Ordinal))
            .ShouldBe(3, "the jobs of the accepted round did not all complete: " + string.Join(", ", statuses));
        statuses.Count(static status => status.StartsWith("refused:", StringComparison.Ordinal) && status.EndsWith(":Failed", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(2, "the jobs of the refused round did not fail: " + string.Join(", ", statuses));
        SecretHits.FindInBytes(bytes, "refused-run").ShouldNotBeEmpty("the schedules are not in the database, so the scan reads the wrong place");

        var outputs = canary.Outputs([new("operator reads (jobs, schedules, runs)", reader.Seen)]);
        var hits = SecretHits.Find(outputs, [new("provider key", canary.Stem)]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, canary.Stem))
        {
            hits.Add($"the provider key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    private static System.Text.Json.Nodes.JsonObject WithEnabled(object body, bool enabled)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(body)!.AsObject();
        node["enabled"] = enabled;

        return node;
    }

    private static async Task<List<(Guid Id, string HandlerKey, string Status)>> ListJobsAsync(KeyedCanaryHost canary)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/jobs?limit=200", UriKind.Relative));

        if (canary.Tenant is { } tenant)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var reply = await canary.Host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var jobs = await TraconTestHost.ReadJsonAsync(reply);

        return [.. jobs.EnumerateArray().Select(static job => (job.GetProperty("id").GetGuid(), job.GetProperty("handlerKey").GetString()!, job.GetProperty("status").GetString()!))];
    }
}
