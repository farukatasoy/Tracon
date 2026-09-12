using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The pre-flight context-window check on the HTTP surface (phase 62, F-59):
/// the diagnostic <c>POST /api/agents/{name}/estimate</c> endpoint and the
/// inline check on <c>POST /api/agents/{name}/run</c>.
/// </summary>
public sealed class PreflightEndpointTests
{
    private const string AgentName = "small-window-agent";
    private const string ModelName = "small-window-model";

    private static readonly Uri EstimateUri = new($"/tracon/api/agents/{AgentName}/estimate", UriKind.Relative);
    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task Estimate_returns_the_prompt_count_without_calling_the_provider()
    {
        var provider = SmallWindowProvider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            EstimateUri, new AgentRunRequest { Message = "hi" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var estimate = await response.Content.ReadFromJsonAsync<ContextWindowEstimate>(TestContext.Current.CancellationToken);

        estimate.ShouldNotBeNull();
        estimate.ContextWindowTokens.ShouldBe(50);
        estimate.WouldBeRejected.ShouldBeFalse();
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Estimate_reports_a_rejection_it_would_make_without_making_it()
    {
        var provider = SmallWindowProvider();
        await using var host = await StartAsync(provider);

        var longPrompt = string.Join(' ', Enumerable.Repeat("banana", 500));

        using var response = await host.Client.PostAsJsonAsync(
            EstimateUri, new AgentRunRequest { Message = longPrompt }, TestContext.Current.CancellationToken);

        var estimate = await response.Content.ReadFromJsonAsync<ContextWindowEstimate>(TestContext.Current.CancellationToken);

        estimate.ShouldNotBeNull();
        estimate.WouldBeRejected.ShouldBeTrue();

        // Diagnostic only: the endpoint itself never calls the provider,
        // regardless of what the estimate says.
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_by_default_a_large_prompt_still_reaches_the_provider()
    {
        var provider = SmallWindowProvider();
        await using var host = await StartAsync(provider);

        var longPrompt = string.Join(' ', Enumerable.Repeat("banana", 500));

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = longPrompt }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Enabled_a_prompt_over_the_window_is_rejected_before_reaching_the_provider()
    {
        var provider = SmallWindowProvider();
        await using var host = await StartAsync(provider, enablePreflight: true);

        var longPrompt = string.Join(' ', Enumerable.Repeat("banana", 500));

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = longPrompt }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        provider.Requests.ShouldBeEmpty();

        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("promptTokens").GetInt32().ShouldBeGreaterThan(50);
        problem.GetProperty("contextWindowTokens").GetInt32().ShouldBe(50);
    }

    [Fact]
    public async Task Enabled_a_prompt_under_the_window_still_runs()
    {
        var provider = SmallWindowProvider();
        await using var host = await StartAsync(provider, enablePreflight: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests.ShouldNotBeEmpty();
    }

    private static FakeModelProvider SmallWindowProvider()
        => new FakeModelProvider("small-window-provider")
            .WithModel(new ModelDescriptor { Name = ModelName, ContextWindowTokens = 50 })
            .EchoesUserMessage();

    private static Task<Infrastructure.TraconTestHost> StartAsync(FakeModelProvider provider, bool enablePreflight = false)
        => Infrastructure.TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = "small-window-provider", Model = ModelName },
                    Origin = AgentDefinitionOrigin.Code,
                })
                .AddModelProvider(provider),
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Preflight.Enabled = enablePreflight;
                options.Preflight.ReserveRatio = 0;
            }));
}
