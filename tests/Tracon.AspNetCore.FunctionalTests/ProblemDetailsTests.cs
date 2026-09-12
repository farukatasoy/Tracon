using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the error contract.
/// </summary>
/// <remarks>
/// Two different contracts exist <strong>by design</strong>: the management API
/// (<c>/api/*</c>) returns <c>ProblemDetails</c>, while the OpenAI-compatible
/// endpoints (<c>/v1/*</c>) return OpenAI's <c>{"error":{...}}</c> shape. Without
/// the latter, stock OpenAI SDKs cannot parse the error.
/// </remarks>
public sealed class ProblemDetailsTests
{
    public static TheoryData<string, HttpStatusCode> ManagementErrors => new()
    {
        { "/tracon/api/agents/no-such-thing", HttpStatusCode.NotFound },
        { "/tracon/api/sessions/no-such-thing", HttpStatusCode.NotFound },
        { "/tracon/api/runs/00000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound },
    };

    [Theory]
    [MemberData(nameof(ManagementErrors))]
    public async Task Management_errors_return_ProblemDetails(string path, HttpStatusCode expected)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("status").GetInt32().ShouldBe((int)expected);
    }

    [Fact]
    public async Task Access_denial_also_returns_ProblemDetails()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Add(TraconTestHost.RemoteIpHeader, "203.0.113.7");

        using var response = await host.Client.SendAsync(request);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_definition_returns_ProblemDetails()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new { name = "missing-model" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task OpenAI_endpoints_return_OpenAI_format_NOT_ProblemDetails()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = "no-such-thing", input = "hello" });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.TryGetProperty("title", out _).ShouldBeFalse();
        json.GetProperty("error").GetProperty("message").ValueKind.ShouldBe(JsonValueKind.String);
        json.GetProperty("error").GetProperty("type").ValueKind.ShouldBe(JsonValueKind.String);
    }
}
