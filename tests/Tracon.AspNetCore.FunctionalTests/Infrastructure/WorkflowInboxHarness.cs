using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The host and graphs of the workflow request inbox tests (phase 209): three graphs that wait for
/// a yes or no, two of them with a request timeout.
/// </summary>
/// <remarks>
/// <para>
/// Every graph is registered in code, so the timeout reaches the engine through
/// <see cref="WorkflowRegistrationOptions.RequestTimeout"/>; the stored-definition path has its own
/// cases, which save the workflow over HTTP.
/// </para>
/// <para>
/// The expiry service reads the clock the host registers. A test that wants a request to run out
/// hands the host a <see cref="ManualTimeProvider"/> and moves it; the scan interval is short so the
/// service looks soon after.
/// </para>
/// </remarks>
internal static class WorkflowInboxHarness
{
    /// <summary>A graph whose requests may wait <see cref="Limit"/>.</summary>
    public const string Timed = "timed-gate";

    /// <summary>A second graph with the same limit, so a list can be narrowed to one workflow.</summary>
    public const string Other = "other-gate";

    /// <summary>A graph whose requests wait without limit.</summary>
    public const string Open = "open-gate";

    /// <summary>The tenant header of a host started with tenancy.</summary>
    public const string TenantHeader = "X-Tracon-Tenant";

    /// <summary>How long a request of <see cref="Timed"/> and <see cref="Other"/> may wait.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromHours(1);

    /// <summary>The list endpoint.</summary>
    public static readonly Uri InboxUri = new("/tracon/api/workflows/requests", UriKind.Relative);

    /// <summary>Starts a host with the three graphs.</summary>
    /// <param name="tenancy">Whether the tenant is read from <see cref="TenantHeader"/>.</param>
    /// <param name="clock">The clock the host and the expiry read; <see langword="null"/> for the real one.</param>
    /// <param name="configureTracon">More Tracon configuration, made after the graphs are registered.</param>
    /// <param name="configureServices">More service registrations, made before Tracon's own.</param>
    /// <param name="configureEndpoints">The endpoint options.</param>
    /// <returns>The host.</returns>
    public static Task<TraconTestHost> StartAsync(
        bool tenancy = false,
        TimeProvider? clock = null,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null,
        Action<TraconEndpointOptions>? configureEndpoints = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseWorkflows(static options => options.RequestExpirationScanInterval = TimeSpan.FromMilliseconds(50));

                builder.AddWorkflow(
                    Timed,
                    static _ => WorkflowResponseHarness.Build(Timed, static approved => new ValueTask<string>(approved ? "approved" : "rejected")),
                    static options => options.RequestTimeout = Limit);

                builder.AddWorkflow(
                    Other,
                    static _ => WorkflowResponseHarness.Build(Other, static approved => new ValueTask<string>(approved ? "approved" : "rejected")),
                    static options => options.RequestTimeout = Limit);

                builder.AddWorkflow(
                    Open,
                    static _ => WorkflowResponseHarness.Build(Open, static approved => new ValueTask<string>(approved ? "approved" : "rejected")));

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }

                configureServices?.Invoke(services);
            },
            configureEndpoints: configureEndpoints);

    /// <summary>Lists the tenant's requests.</summary>
    /// <param name="host">The host.</param>
    /// <param name="query">The query string without the question mark.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The items.</returns>
    public static async Task<List<JsonElement>> ListAsync(TraconTestHost host, string query = "", string? tenant = null)
    {
        using var response = await SendListAsync(host, query, tenant);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return [.. (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Select(static item => item.Clone())];
    }

    /// <summary>Sends the list request and returns the response as it is.</summary>
    /// <param name="host">The host.</param>
    /// <param name="query">The query string without the question mark.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    public static async Task<HttpResponseMessage> SendListAsync(TraconTestHost host, string query = "", string? tenant = null)
    {
        var uri = query.Length == 0 ? InboxUri : new Uri($"{InboxUri}?{query}", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return await host.Client.SendAsync(request);
    }

    /// <summary>Answers a request on the streamed path and reads the stream to its end.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The waiting run.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The run the answer started.</returns>
    public static async Task<Guid> AnswerAsync(TraconTestHost host, Guid runId, string requestId, string? tenant = null)
    {
        using var response = await WorkflowResponseHarness.RespondAsync(host, runId, new { requestId, approved = true }, tenant);

        return WorkflowResponseHarness.RunIdOf(await WorkflowResponseHarness.FramesAsync(response));
    }

    /// <summary>Saves a stored workflow whose plan needs a human's approval.</summary>
    /// <param name="host">The host.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="requestTimeout">How long a request may wait; <see langword="null"/> for no limit.</param>
    /// <returns>The response of the save.</returns>
    public static async Task<HttpResponseMessage> SavePlannedAsync(TraconTestHost host, string name, TimeSpan? requestTimeout)
        => await host.Client.PutAsJsonAsync($"/tracon/api/workflows/{name}", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["worker"],
            MaxIterations = 2,
            RequirePlanApproval = true,
            RequestTimeout = requestTimeout,
        });

    /// <summary>Reads a run through the API.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The run.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The run as JSON.</returns>
    public static async Task<JsonElement> RunAsync(TraconTestHost host, Guid runId, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/runs/{runId}");

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);

        return await TraconTestHost.ReadJsonAsync(response);
    }
}
