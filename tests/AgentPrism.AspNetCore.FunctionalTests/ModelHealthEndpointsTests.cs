using System.Globalization;
using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the <c>/api/models/health</c> endpoints, the status field on
/// <c>/api/models</c>, and how the circuit breaker is reflected on the health endpoint.
/// </summary>
/// <remarks>
/// Ollama is not installed on this machine; F-05's local/keyless connection
/// mechanism is verified over a real socket with <see cref="FakeOpenAiCompatibleServer"/>.
/// No test calls a real OpenAI/OpenRouter <strong>at all</strong> — verification
/// that requires the network is done manually (consistent with the same
/// decision in Phase 3: <c>docs/arsiv/fazlar/03-SAGLAYICI-VE-DERLEYICI.md</c>).
/// </remarks>
public sealed class ModelHealthEndpointsTests
{
    [Fact]
    public async Task Provider_that_does_not_implement_a_health_check_returns_Unknown()
    {
        // AgentPrismTestHost registers the "echo" provider by default; it does
        // not implement IModelProviderHealthCheck — that is not an error.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/echo", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Unknown");
    }

    [Fact]
    public async Task Unknown_provider_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/no-such-provider", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Models_endpoint_reads_from_cache_and_does_not_hit_the_network_until_the_first_check()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress));

        // No check has run yet: /api/models reads from cache, it does not hit the network.
        using (var models = await host.Client.GetAsync(new Uri("/agentprism/api/models", UriKind.Relative)))
        {
            var body = await AgentPrismTestHost.ReadJsonAsync(models);
            body.EnumerateArray()
                .Single(static p => string.Equals(p.GetProperty("name").GetString(), "local-test", StringComparison.Ordinal))
                .GetProperty("status").GetString().ShouldBe("Unknown");
        }

        server.ModelsCallCount.ShouldBe(0);

        // Trigger a real check.
        using (var health = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
            var body = await AgentPrismTestHost.ReadJsonAsync(health);
            body.GetProperty("status").GetString().ShouldBe("Healthy");
            body.GetProperty("models").EnumerateArray().Select(static m => m.GetString())
                .ShouldContain(static m => string.Equals(m, "fake-local-model", StringComparison.Ordinal));
        }

        server.ModelsCallCount.ShouldBe(1);

        // /api/models now sees Healthy from cache, STILL does not hit the network.
        using (var modelsAfter = await host.Client.GetAsync(new Uri("/agentprism/api/models", UriKind.Relative)))
        {
            var body = await AgentPrismTestHost.ReadJsonAsync(modelsAfter);
            body.EnumerateArray()
                .Single(static p => string.Equals(p.GetProperty("name").GetString(), "local-test", StringComparison.Ordinal))
                .GetProperty("status").GetString().ShouldBe("Healthy");
        }

        server.ModelsCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_true_skips_the_cache_and_re_checks()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress));

        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
        }

        server.ModelsCallCount.ShouldBe(1);

        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
        }

        server.ModelsCallCount.ShouldBe(1); // returned from cache

        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test?refresh=true", UriKind.Relative)))
        {
        }

        server.ModelsCallCount.ShouldBe(2); // cache was skipped
    }

    [Fact]
    public async Task Server_error_returns_Unhealthy_and_the_detail_does_not_leak_the_address()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ModelsStatusCode = StatusCodes.Status503ServiceUnavailable;

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var detail = body.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldContain("503");
        detail.ShouldNotContain(server.BaseAddress.Host);
        detail.ShouldNotContain(server.BaseAddress.Port.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Unreachable_providers_detail_contains_neither_the_key_nor_the_address()
    {
        const string secret = "super-secret-openrouter-key-TEST";
        // A closed/reserved port (1): produces a real connection refusal.
        var deadEndpoint = new Uri("http://127.0.0.1:1");

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("dead", o =>
            {
                o.Endpoint = deadEndpoint;
                o.ApiKey = secret;
                o.Timeout = TimeSpan.FromSeconds(5);
            }));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/dead", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var detail = body.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldNotContain(secret);
        detail.ShouldNotContain("127.0.0.1");
        detail.ShouldNotContain(":1\"");

        var raw = await (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/dead", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        raw.ShouldNotContain(secret);
    }

    [Fact]
    public async Task Consecutive_failures_open_the_circuit_and_the_health_endpoint_reflects_it()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        // 400 (deliberately NOT 500): System.ClientModel's default retry policy
        // automatically retries 5xx/408/429, which would make the raw request
        // count unpredictable. 400 is not retried; the count stays deterministic.
        server.ChatCompletionStatusCode = StatusCodes.Status400BadRequest;

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: services => services.Configure<AgentPrismOptions>(options =>
            {
                options.CircuitBreaker.FailureThreshold = 2;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            }));

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();
        var binding = new ModelBinding { Provider = "local-test", Model = "fake-local-model" };
        ChatMessage[] messages = [new ChatMessage(ChatRole.User, "hello")];

        for (var i = 0; i < 2; i++)
        {
            using var chatClient = registry.CreateChatClient(binding);
            await Should.ThrowAsync<Exception>(() => chatClient.GetResponseAsync(messages));
        }

        // The threshold was exceeded: the third attempt NEVER reaches the provider.
        using (var chatClient = registry.CreateChatClient(binding))
        {
            await Should.ThrowAsync<AgentPrismProviderUnavailableException>(() => chatClient.GetResponseAsync(messages));
        }

        server.ChatCompletionCallCount.ShouldBe(2);

        using var health = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(health);

        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var circuitDetail = body.GetProperty("detail").GetString();
        circuitDetail.ShouldNotBeNull();
        circuitDetail.ShouldContain("Circuit breaker");
    }
}
