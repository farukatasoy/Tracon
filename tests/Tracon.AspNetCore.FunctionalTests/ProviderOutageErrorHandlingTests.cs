using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Family H (HATA-S2-003/HATA-S3-005) regression tests: the narrow exception
/// filter on the non-streaming run endpoints let real provider SDK exceptions
/// escape and leak into ASP.NET Core's generic handler — the client got a bare
/// <c>500</c> instead of the expected <c>502</c>/<c>upstream_error</c>. K-296/K-384
/// already fixed this pattern on the sibling streaming (SSE) paths; the three
/// non-streaming paths fixed here are verified with the SAME <see cref="ThrowingModelProvider"/>.
/// </summary>
public sealed class ProviderOutageErrorHandlingTests
{
    private const string AgentName = "broken-agent";
    private const string IdempotencyHeader = "Idempotency-Key";

    // Section 103.2.3 secret-like fake message: proves the raw provider exception
    // text never reaches a public response surface, whatever it happens to contain.
    private const string SecretLikeMessage =
        "https://tenant-private.example; Authorization=Bearer sk-secret-preview1; account=acct-42";

    private static void ConfigureBrokenAgent(ITraconBuilder builder)
    {
        builder
            .AddModelProvider(new ThrowingModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "kirik", Model = "kirik-1" },
            });
    }

    private static void ConfigureBrokenAgentWithSecretLikeMessage(ITraconBuilder builder)
    {
        builder
            .AddModelProvider(new ThrowingModelProvider(message: SecretLikeMessage))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "kirik", Model = "kirik-1" },
            });
    }

    [Fact]
    public async Task Non_streaming_run_endpoint_returns_502_ProblemDetails_on_provider_error()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgent);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Add(IdempotencyHeader, Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Non_streaming_run_endpoint_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgentWithSecretLikeMessage);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Add(IdempotencyHeader, Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(SecretLikeMessage);
        body.ShouldNotContain("sk-secret-preview1");
    }

    [Fact]
    public async Task Streaming_run_endpoint_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgentWithSecretLikeMessage);

        // No Idempotency-Key header: the run endpoint's default (SSE) path.
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba" });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(SecretLikeMessage);
        body.ShouldNotContain("sk-secret-preview1");
    }

    [Fact]
    public async Task Responses_endpoint_returns_502_upstream_error_on_provider_error()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgent);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = AgentName, input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("error").GetProperty("type").GetString().ShouldBe("upstream_error");
    }

    [Fact]
    public async Task Responses_endpoint_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgentWithSecretLikeMessage);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = AgentName, input = "merhaba" });

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(SecretLikeMessage);
        body.ShouldNotContain("sk-secret-preview1");
    }

    [Fact]
    public async Task ChatCompletions_endpoint_returns_502_upstream_error_on_provider_error()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgent);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new
            {
                model = AgentName,
                messages = new[] { new { role = "user", content = "merhaba" } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("error").GetProperty("type").GetString().ShouldBe("upstream_error");
    }

    [Fact]
    public async Task ChatCompletions_endpoint_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgentWithSecretLikeMessage);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new
            {
                model = AgentName,
                messages = new[] { new { role = "user", content = "merhaba" } },
            });

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(SecretLikeMessage);
        body.ShouldNotContain("sk-secret-preview1");
    }

    [Fact]
    public async Task Responses_streaming_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgentWithSecretLikeMessage);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = AgentName, input = "merhaba", stream = true });

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(SecretLikeMessage);
        body.ShouldNotContain("sk-secret-preview1");
    }

    [Fact]
    public async Task ChatCompletions_streaming_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(ConfigureBrokenAgentWithSecretLikeMessage);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new
            {
                model = AgentName,
                messages = new[] { new { role = "user", content = "merhaba" } },
                stream = true,
            });

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(SecretLikeMessage);
        body.ShouldNotContain("sk-secret-preview1");
    }

    [Fact]
    public async Task Mcp_tool_call_never_exposes_a_secret_like_provider_message()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                ConfigureBrokenAgentWithSecretLikeMessage(builder);
                builder.UseMcpServer(options => options.ExposedAgents.Add(AgentName));
            },
            configureAfterMap: app => app.MapTraconMcpServer());

        var (_, body) = await McpTestClient.SendAsync(
            host.Client,
            "/tracon/mcp",
            "tools/call",
            new { name = $"tracon_{AgentName}", arguments = new { message = "merhaba" } });

        var text = body!.Value.GetRawText();

        text.ShouldNotContain(SecretLikeMessage);
        text.ShouldNotContain("sk-secret-preview1");
    }
}
