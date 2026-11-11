using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The refusals the endpoint filter itself writes (an unknown key, a key without the route's scope, a malformed tenant
/// header) reach the OpenAI-compatible routes in the OpenAI error shape, as the published document says.
/// </summary>
/// <remarks>
/// 🚨 A stock OpenAI SDK parses <c>{"error":{"message":…,"type":…}}</c>. The refusal of a role already followed that rule;
/// the filter wrote <c>application/problem+json</c> for everything else, so the document listed one content type and the
/// route answered another, and the first scheduled schema fuzz of the public repository went red on it.
/// </remarks>
public sealed class OpenAIFilterDenialShapeTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static TheoryData<string, string> RoutesThatNeedAScopeOtherThanAgentsRead => new()
    {
        { "POST", "/tracon/v1/chat/completions" },
        { "POST", "/tracon/v1/responses" },
        { "POST", "/tracon/v1/conversations" },
        { "GET", "/tracon/v1/conversations/conv_missing" },
        { "DELETE", "/tracon/v1/conversations/conv_missing" },
        { "GET", "/tracon/v1/conversations/conv_missing/items" },
    };

    [Theory]
    [MemberData(nameof(RoutesThatNeedAScopeOtherThanAgentsRead))]
    public async Task A_key_without_the_scope_of_the_route_gets_the_openai_permission_error(string method, string path)
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = Request(method, path, created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        await AssertOpenAIErrorAsync(response, HttpStatusCode.Forbidden, "permission_error", "scope");
    }

    [Theory]
    [MemberData(nameof(RoutesThatNeedAScopeOtherThanAgentsRead))]
    public async Task An_unknown_key_gets_the_openai_authentication_error(string method, string path)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = Request(method, path, "no-such-key");

        using var response = await host.Client.SendAsync(request);

        await AssertOpenAIErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_error", "Bearer");
    }

    [Fact]
    public async Task A_malformed_tenant_header_gets_the_openai_invalid_request_error()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "runner", "RunsWrite");

        using var request = Request("POST", "/tracon/v1/chat/completions", created.PlaintextKey);
        request.Headers.Add(TenantHeader, "not a tenant!");

        using var response = await host.Client.SendAsync(request);

        await AssertOpenAIErrorAsync(response, HttpStatusCode.BadRequest, "invalid_request_error", TenantHeader);
    }

    // --- The idempotency filter sits behind the access filter and refuses in the same way ---

    public static TheoryData<string, object> RunStartingRoutes => new()
    {
        { "/tracon/v1/chat/completions", new { model = "kod-agent", messages = new[] { new { role = "user", content = "hello" } } } },
        { "/tracon/v1/responses", new { model = "kod-agent", input = "hello" } },
    };

    [Theory]
    [MemberData(nameof(RunStartingRoutes))]
    public async Task A_key_while_idempotency_is_off_gets_the_openai_error(string path, object body)
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<TraconIdempotencyOptions>(static options => options.Enabled = false));

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(IdempotencyHeader, "key-1");

        using var response = await host.Client.SendAsync(request);

        await AssertOpenAIErrorAsync(response, HttpStatusCode.NotImplemented, "invalid_request_error", IdempotencyHeader);
    }

    [Theory]
    [MemberData(nameof(RunStartingRoutes))]
    public async Task A_key_that_is_too_long_gets_the_openai_error(string path, object body)
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(IdempotencyHeader, new string('k', 400));

        using var response = await host.Client.SendAsync(request);

        await AssertOpenAIErrorAsync(response, HttpStatusCode.BadRequest, "invalid_request_error", "characters");
    }

    [Fact]
    public async Task A_key_on_a_streaming_request_gets_the_openai_error()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "kod-agent", stream = true, messages = new[] { new { role = "user", content = "hello" } } }),
        };
        request.Headers.Add(IdempotencyHeader, "key-stream");

        using var response = await host.Client.SendAsync(request);

        await AssertOpenAIErrorAsync(response, HttpStatusCode.BadRequest, "invalid_request_error", "streaming");
    }

    [Fact]
    public async Task A_key_reused_for_another_body_gets_the_openai_error()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var first = new HttpRequestMessage(HttpMethod.Post, "/tracon/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "kod-agent", messages = new[] { new { role = "user", content = "one" } } }),
        };
        first.Headers.Add(IdempotencyHeader, "key-reused");
        using var firstResponse = await host.Client.SendAsync(first);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/tracon/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "kod-agent", messages = new[] { new { role = "user", content = "two" } } }),
        };
        second.Headers.Add(IdempotencyHeader, "key-reused");
        using var secondResponse = await host.Client.SendAsync(second);

        await AssertOpenAIErrorAsync(secondResponse, HttpStatusCode.UnprocessableEntity, "invalid_request_error", "key-reused");
    }

    [Fact]
    public async Task The_rate_limit_answers_an_openai_route_in_the_openai_shape_and_keeps_retry_after()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 1;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        var route = new Uri("/tracon/v1/conversations/conv_missing", UriKind.Relative);

        using var allowed = await host.Client.GetAsync(route);
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        using var blocked = await host.Client.GetAsync(route);

        await AssertOpenAIErrorAsync(blocked, HttpStatusCode.TooManyRequests, "rate_limit_error", "requests per");
        blocked.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_native_api_keeps_problem_details_for_the_same_refusal()
    {
        // The rule is the OpenAI-compatible routes' alone (they answer in the shape SDKs parse); the native API answers
        // every refusal as ProblemDetails, and a change that moved it too would break its clients.
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = Request("GET", "/tracon/api/api-keys", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("title").GetString().ShouldBe("Insufficient scope");
    }

    private static HttpRequestMessage Request(string method, string path, string key)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method is "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    private static async Task AssertOpenAIErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string type,
        string messageMentions)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        // Not ProblemDetails: no title, no status, no type at the top level.
        root.TryGetProperty("title", out _).ShouldBeFalse();
        root.TryGetProperty("status", out _).ShouldBeFalse();

        var error = root.GetProperty("error");
        error.GetProperty("type").GetString().ShouldBe(type);
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain(messageMentions);
    }
}
