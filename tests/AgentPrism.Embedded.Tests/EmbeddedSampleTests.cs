using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism;
using AgentPrism.Embedded.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Embedded.Tests;

/// <summary>
/// Runs <c>samples/AgentPrism.Embedded</c>'s actual entry point end to end
/// (Phase 85, F-140). Confirms the DoD this sample exists to prove: all six
/// embedding points report as bound, a background job with no HTTP request
/// behind it still carries the right tenant and user, and a congested event
/// bridge drops events instead of slowing the run down.
/// </summary>
public sealed class EmbeddedSampleTests
{
    [Fact]
    public async Task All_six_extension_points_report_the_samples_own_types()
    {
        await using var host = new EmbeddedSampleHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("agentprism/api/diagnostics", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var extensionPoints = body.GetProperty("extensionPoints").EnumerateArray().ToArray();

        extensionPoints.Length.ShouldBe(6);
        extensionPoints.ShouldAllBe(static point => !point.GetProperty("isBuiltInDefault").GetBoolean());

        var implementations = extensionPoints.ToDictionary(
            static point => point.GetProperty("contract").GetString()!,
            static point => point.GetProperty("implementation").GetString(),
            StringComparer.Ordinal);

        implementations["ITenantContext"].ShouldBe("EmbeddedTenantContext");
        implementations["IRunAttributionContext"].ShouldBe("EmbeddedRunAttributionContext");
        implementations["IToolAuthorizationHandler"].ShouldBe("EmbeddedToolAuthorizationHandler");
        implementations["IRunAuthorizationHandler"].ShouldBe("EmbeddedRunAuthorizationHandler");
        implementations["IRunEventSink"].ShouldBe("BoundedChannelRunEventSink");
        implementations["IAttachmentStorage"].ShouldBe("InMemoryBufferAttachmentStorage");
    }

    /// <summary>
    /// The mandatory scenario (85.3): a job with no HTTP request behind it
    /// still records the right tenant and user, which only works if
    /// <c>AmbientTenantScope</c>/<c>AmbientRunAttributionScope</c> survive
    /// every <c>await</c> between the worker's own method body and the model
    /// call inside <c>AIAgent.RunAsync</c>. Also proves
    /// <c>AgentPrismRunContext.Current</c> itself: the sample's
    /// <c>EchoModelProvider</c> always calls the <c>current_account</c> tool
    /// first, and that tool reads its answer only from
    /// <c>AgentPrismRunContext.Current</c> — a stale or empty scope there
    /// would surface as a wrong tenant/run id in the recorded tool result,
    /// not as a compile-time or mocked value.
    /// </summary>
    [Fact]
    public async Task Background_job_with_no_HTTP_request_carries_tenant_and_user_through_the_run()
    {
        await using var host = new EmbeddedSampleHost();
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("jobs", UriKind.Relative),
            new { tenantId = "acme", userId = "user-42", message = "What is my account balance?" });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);

        var run = await PollForRunAsync(host, "acme", timeout: TimeSpan.FromSeconds(10));

        run.TenantId.ShouldBe("acme");
        run.UserId.ShouldBe("user-42");
        run.Status.ShouldBe(RunStatus.Completed);

        var probe = await PollForToolInvocationAsync(host, "acme", run.Id, timeout: TimeSpan.FromSeconds(10));

        probe.ToolName.ShouldBe("current_account");
        probe.Result.ShouldBe($"tenant=acme run={run.Id} session=(none)");
    }

    /// <summary>
    /// The event bridge drops under backpressure rather than blocking the run
    /// (85.2's "queue and return" rule). A burst overflows the sink's 8-item
    /// channel; the runs it belongs to still complete in milliseconds.
    /// </summary>
    [Fact]
    public async Task Event_bridge_drops_under_backpressure_without_slowing_the_run()
    {
        await using var host = new EmbeddedSampleHost();
        using var client = host.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            using var response = await client.PostAsJsonAsync(
                new Uri("jobs", UriKind.Relative),
                new { tenantId = "acme", message = $"burst {i}" });

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        }

        var dropped = await PollForDropAsync(client, timeout: TimeSpan.FromSeconds(10));
        dropped.ShouldBeGreaterThan(0);

        var runStore = host.Services.GetRequiredService<IRunStore>();
        var runs = await runStore.QueryRunsAsync(new RunQuery { TenantId = "acme", Status = RunStatus.Completed });

        runs.ShouldNotBeEmpty();
        runs.ShouldAllBe(static run =>
            run.CompletedAt.HasValue && run.CompletedAt.Value - run.StartedAt < TimeSpan.FromSeconds(1));
    }

    private static async Task<RunRecord> PollForRunAsync(EmbeddedSampleHost host, string tenantId, TimeSpan timeout)
    {
        var runStore = host.Services.GetRequiredService<IRunStore>();
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var runs = await runStore.QueryRunsAsync(new RunQuery { TenantId = tenantId, Status = RunStatus.Completed });

            if (runs.Count > 0)
            {
                return runs[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"No completed run for tenant '{tenantId}' within {timeout}.");
    }

    /// <summary>
    /// <see cref="IRunStore.ListToolInvocationsAsync"/> resolves the tenant
    /// from the ambient <see cref="ITenantContext"/> internally (it takes no
    /// tenant parameter, unlike <see cref="IRunStore.QueryRunsAsync"/>) —
    /// called from a test thread with no HTTP request and no ambient scope,
    /// it would silently resolve to the DEFAULT tenant and find nothing for
    /// "acme". <see cref="AmbientTenantScope.Begin"/> is the same seam
    /// <c>Jobs/EmbeddedJobWorker.cs</c> itself uses for the same reason.
    /// </summary>
    private static async Task<ToolInvocationRecord> PollForToolInvocationAsync(EmbeddedSampleHost host, string tenantId, Guid runId, TimeSpan timeout)
    {
        var runStore = host.Services.GetRequiredService<IRunStore>();
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            IReadOnlyList<ToolInvocationRecord> invocations;

            using (AmbientTenantScope.Begin(tenantId))
            {
                invocations = await runStore.ListToolInvocationsAsync(runId);
            }

            if (invocations.Count > 0)
            {
                return invocations[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"No tool invocation recorded for run '{runId}' within {timeout}.");
    }

    private static async Task<int> PollForDropAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var body = JsonDocument.Parse(
                await client.GetStringAsync(new Uri("jobs/bridge-state", UriKind.Relative))).RootElement;

            var dropped = body.GetProperty("dropped").GetInt32();

            if (dropped > 0)
            {
                return dropped;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return 0;
    }
}
