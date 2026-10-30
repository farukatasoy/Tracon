using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The circuit of the setup-time credential is shared by every tenant.
/// One tenant's malformed requests must not stop the runs of another.
/// </summary>
/// <remarks>
/// <para>
/// Crosses the boundaries a unit test cannot: the tenant comes from the HTTP
/// header, the pipeline is the one DI builds, and the provider is a real HTTP
/// server behind the OpenAI-compatible client. 400 and 401 are both outside the
/// client's own retry policy, so the request count stays deterministic.
/// </para>
/// <para>
/// The errors carry an OpenAI-shaped JSON body, as OpenAI's own API sends, so
/// the client writes the status as <c>HTTP 400</c>. With an empty body it writes
/// <c>Status: 400 (Bad Request)</c> instead, which the status reader does not
/// recognize yet (measured 2026-10-12; reported as a separate defect).
/// </para>
/// </remarks>
public sealed class SharedCredentialCircuitTests
{
    private const string AgentName = "shared-key-agent";
    private const string ProviderName = "local-test";
    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task One_tenants_rejected_requests_do_not_stop_another_tenants_runs()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = StatusCodes.Status400BadRequest;
        server.ChatCompletionErrorHasOpenAiBody = true;

        await using var host = await StartAsync(server);

        // Tenant A sends as many malformed requests as the threshold allows.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var rejected = await PostBufferedAsync(host, "tenant-a");
            rejected.IsSuccessStatusCode.ShouldBeFalse();
        }

        server.ChatCompletionStatusCode = StatusCodes.Status200OK;

        using var response = await PostBufferedAsync(host, "tenant-b");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        server.ChatCompletionCallCount.ShouldBe(3, "an open shared circuit stops tenant B before the provider.");
    }

    [Fact]
    public async Task A_rejected_shared_credential_still_stops_every_tenant()
    {
        // The other half of the rule: a 401 on the shared credential is the
        // same answer for every tenant, so the shared circuit still opens
        // (an earlier defect depends on this).
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = StatusCodes.Status401Unauthorized;
        server.ChatCompletionErrorHasOpenAiBody = true;

        await using var host = await StartAsync(server);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var rejected = await PostBufferedAsync(host, "tenant-a");
            rejected.IsSuccessStatusCode.ShouldBeFalse();
        }

        server.ChatCompletionStatusCode = StatusCodes.Status200OK;

        using var response = await PostBufferedAsync(host, "tenant-b");

        response.IsSuccessStatusCode.ShouldBeFalse();
        server.ChatCompletionCallCount.ShouldBe(2, "the open circuit stops tenant B before the provider.");
    }

    private static Task<TraconTestHost> StartAsync(FakeOpenAiCompatibleServer server)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseOpenAICompatible(ProviderName, o => o.Endpoint = server.BaseAddress)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "fake-local-model" },
                }),
            configureServices: static services => services.Configure<TraconOptions>(options =>
            {
                options.CircuitBreaker.FailureThreshold = 2;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            }));

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = Guid.NewGuid().ToString("N") }),
        };

        // The Idempotency-Key header selects the single JSON response.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-Tracon-Tenant", tenant);

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
