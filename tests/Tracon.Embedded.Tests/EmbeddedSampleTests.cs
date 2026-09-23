using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon;
using Tracon.Embedded.Tests.Infrastructure;

namespace Tracon.Embedded.Tests;

/// <summary>
/// Runs <c>samples/Tracon.Embedded</c>'s actual entry point end to end
/// (Phase 85, F-140). Confirms the DoD this sample exists to prove: the six
/// embedding points it customizes report as bound, a background job with no
/// HTTP request behind it still carries the right tenant and user, and a
/// congested event bridge drops events instead of slowing the run down.
/// </summary>
public sealed class EmbeddedSampleTests
{
    [Fact]
    public async Task Six_of_the_seven_extension_points_report_the_samples_own_types()
    {
        await using var host = new EmbeddedSampleHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("tracon/api/diagnostics", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var extensionPoints = body.GetProperty("extensionPoints").EnumerateArray().ToArray();

        extensionPoints.Length.ShouldBe(7);

        var implementations = extensionPoints.ToDictionary(
            static point => point.GetProperty("contract").GetString()!,
            static point => point.GetProperty("implementation").GetString(),
            StringComparer.Ordinal);
        var builtIn = extensionPoints.ToDictionary(
            static point => point.GetProperty("contract").GetString()!,
            static point => point.GetProperty("isBuiltInDefault").GetBoolean(),
            StringComparer.Ordinal);

        implementations["ITenantContext"].ShouldBe("EmbeddedTenantContext");
        implementations["IRunAttributionContext"].ShouldBe("EmbeddedRunAttributionContext");
        implementations["IToolAuthorizationHandler"].ShouldBe("EmbeddedToolAuthorizationHandler");
        implementations["IRunAuthorizationHandler"].ShouldBe("EmbeddedRunAuthorizationHandler");
        implementations["IRunEventSink"].ShouldBe("BoundedChannelRunEventSink");
        implementations["IAttachmentStorage"].ShouldBe("InMemoryBufferAttachmentStorage");
        builtIn["ITenantContext"].ShouldBeFalse();
        builtIn["IRunAttributionContext"].ShouldBeFalse();
        builtIn["IToolAuthorizationHandler"].ShouldBeFalse();
        builtIn["IRunAuthorizationHandler"].ShouldBeFalse();
        builtIn["IRunEventSink"].ShouldBeFalse();
        builtIn["IAttachmentStorage"].ShouldBeFalse();

        // This sample does not customize tool-approval presentation (phase 142)
        // — it carries no tool that requires approval at all — so the seventh
        // point stays Tracon's own built-in default, unlike the other six.
        implementations["IToolApprovalPresenter"].ShouldBe("NullToolApprovalPresenter");
        builtIn["IToolApprovalPresenter"].ShouldBeTrue();
    }

    /// <summary>
    /// The mandatory scenario (85.3): a job with no HTTP request behind it
    /// still records the right tenant and user, which only works if
    /// <c>AmbientTenantScope</c>/<c>AmbientRunAttributionScope</c> survive
    /// every <c>await</c> between the worker's own method body and the model
    /// call inside <c>AIAgent.RunAsync</c>. Also proves
    /// <c>TraconRunContext.Current</c> itself: the sample's
    /// <c>EchoModelProvider</c> always calls the <c>current_account</c> tool
    /// first, and that tool reads its answer only from
    /// <c>TraconRunContext.Current</c> — a stale or empty scope there
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

        var run = await PollForRunAsync(host, "acme");

        run.TenantId.ShouldBe("acme");
        run.UserId.ShouldBe("user-42");
        run.Status.ShouldBe(RunStatus.Completed);

        var probe = await PollForToolInvocationAsync(host, "acme", run.Id);

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

        var dropped = await PollForDropAsync(client);
        dropped.ShouldBeGreaterThan(0);

        var runStore = host.Services.GetRequiredService<IRunStore>();
        var runs = await runStore.QueryRunsAsync(new RunQuery { TenantId = "acme", Status = RunStatus.Completed });

        runs.ShouldNotBeEmpty();
        runs.ShouldAllBe(static run =>
            run.CompletedAt.HasValue && run.CompletedAt.Value - run.StartedAt < TimeSpan.FromSeconds(1));
    }

    private static async Task<RunRecord> PollForRunAsync(EmbeddedSampleHost host, string tenantId)
    {
        var runStore = host.Services.GetRequiredService<IRunStore>();

        var runs = await WaitUntil.ValueAsync(
            () => runStore.QueryRunsAsync(new RunQuery { TenantId = tenantId, Status = RunStatus.Completed }).AsTask(),
            static runs => runs.Count > 0,
            $"a completed run for tenant '{tenantId}'");

        return runs[0];
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
    private static async Task<ToolInvocationRecord> PollForToolInvocationAsync(EmbeddedSampleHost host, string tenantId, Guid runId)
    {
        var runStore = host.Services.GetRequiredService<IRunStore>();

        var invocations = await WaitUntil.ValueAsync(
            async () =>
            {
                using (AmbientTenantScope.Begin(tenantId))
                {
                    return await runStore.ListToolInvocationsAsync(runId);
                }
            },
            static invocations => invocations.Count > 0,
            $"a tool invocation recorded for run '{runId}'");

        return invocations[0];
    }

    private static Task<int> PollForDropAsync(HttpClient client)
        => WaitUntil.ValueAsync(
            async () => JsonDocument.Parse(
                    await client.GetStringAsync(new Uri("jobs/bridge-state", UriKind.Relative))).RootElement
                .GetProperty("dropped").GetInt32(),
            static dropped => dropped > 0,
            "the event bridge to drop an event");
}
