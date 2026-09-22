using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon.ProviderCore.Tests;

/// <summary>
/// The shared health check body (phase 181). Linked into the four provider test
/// projects, so each assembly's compiled copy is tested.
/// </summary>
public sealed class ProviderHealthCheckCoreTests
{
    private const string ProviderName = "shared-test";
    private const string Secret = "not-a-real-key-core";

    [Fact]
    public async Task Success_returns_healthy_with_sorted_models_and_the_authorize_headers_on_the_wire()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"b"},{"id":"a"}]}""");

        var health = await new ProviderHealthCheckCore(ProviderName).CheckAsync(
            ProviderHealthCheckCore.JoinEndpoint(server.BaseAddress, "v1/models"),
            TimeSpan.FromSeconds(5),
            static (request, _) =>
            {
                request.Headers.Add("x-test-auth", "value");
                return ValueTask.CompletedTask;
            },
            static (response, token) => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", null, token),
            TestContext.Current.CancellationToken);

        health.ProviderName.ShouldBe(ProviderName);
        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        health.Models.ShouldBe(["a", "b"]);

        var head = await server.RequestHead;
        head.ShouldStartWith("GET /v1/models HTTP/1.1");
        head.ShouldContain("x-test-auth: value");
    }

    [Fact]
    public async Task Non_success_status_is_unhealthy_with_the_status_only()
    {
        await using var server = StubHttpServer.Respond(401, "Unauthorized", "{}");

        var health = await CheckAsync(server.BaseAddress, TimeSpan.FromSeconds(5));

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldBe("HTTP 401 Unauthorized");
    }

    [Fact]
    public async Task Invalid_json_is_degraded_not_an_exception()
    {
        await using var server = StubHttpServer.Respond(200, "OK", "not json");

        var health = await CheckAsync(server.BaseAddress, TimeSpan.FromSeconds(5));

        health.Status.ShouldBe(ModelProviderHealthStatus.Degraded);
        health.Detail.ShouldBe("The response is not valid JSON.");
    }

    [Fact]
    public async Task Server_that_never_answers_is_reported_as_a_timeout()
    {
        await using var server = StubHttpServer.NeverRespond();

        var health = await CheckAsync(server.BaseAddress, TimeSpan.FromMilliseconds(300));

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldBe("Timed out.");
    }

    [Fact]
    public async Task Authorize_step_that_hangs_is_bounded_by_the_same_timeout()
    {
        // The Azure token request runs inside the authorize delegate; a hung
        // token endpoint must not hang the health check.
        var health = await new ProviderHealthCheckCore(ProviderName).CheckAsync(
            new Uri("http://127.0.0.1:1/models"),
            TimeSpan.FromMilliseconds(300),
            static async (_, token) => await Task.Delay(Timeout.Infinite, token),
            static (_, _) => throw new InvalidOperationException("The request must not be sent."),
            TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldBe("Timed out.");
    }

    [Fact]
    public async Task Authorize_step_that_throws_is_a_credential_error_without_its_message()
    {
        var health = await new ProviderHealthCheckCore(ProviderName).CheckAsync(
            new Uri("http://127.0.0.1:1/models"),
            TimeSpan.FromSeconds(5),
            static (_, _) => throw new InvalidOperationException(Secret),
            static (_, _) => throw new InvalidOperationException("The request must not be sent."),
            TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldBe("Credential error (InvalidOperationException).");
    }

    [Fact]
    public async Task Credential_failure_reaches_the_log_with_its_exception()
    {
        // The response carries only the type name, so the log is the operator's
        // only route to the credential library's message.
        var logger = new CapturingLogger();

        await new ProviderHealthCheckCore(ProviderName, logger).CheckAsync(
            new Uri("http://127.0.0.1:1/models"),
            TimeSpan.FromSeconds(5),
            static (_, _) => throw new InvalidOperationException("no sign-in"),
            static (_, _) => throw new InvalidOperationException("The request must not be sent."),
            TestContext.Current.CancellationToken);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(ProviderName);
        entry.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("no sign-in");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_timeout_and_propagates()
    {
        await using var server = StubHttpServer.NeverRespond();
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(async () => await new ProviderHealthCheckCore(ProviderName).CheckAsync(
            server.BaseAddress,
            TimeSpan.FromSeconds(30),
            static (_, _) => ValueTask.CompletedTask,
            static (response, token) => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", null, token),
            caller.Token));
    }

    [Fact]
    public async Task Refused_connection_detail_carries_neither_the_address_nor_the_secret()
    {
        var health = await new ProviderHealthCheckCore(ProviderName).CheckAsync(
            new Uri("http://127.0.0.1:1/models"),
            TimeSpan.FromSeconds(5),
            static (request, _) =>
            {
                request.Headers.Add("x-api-key", Secret);
                return ValueTask.CompletedTask;
            },
            static (response, token) => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", null, token),
            TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldStartWith("Connection error (");
        health.Detail.ShouldNotContain("127.0.0.1");
        health.Detail.ShouldNotContain(Secret);
    }

    [Theory]
    [InlineData("https://example.test/v1", "models", "https://example.test/v1/models")]
    [InlineData("https://example.test/v1/", "models", "https://example.test/v1/models")]
    [InlineData("https://example.test/azure", "openai/models?api-version=1", "https://example.test/azure/openai/models?api-version=1")]
    public void Join_never_swallows_the_last_segment(string baseAddress, string relative, string expected)
        => ProviderHealthCheckCore.JoinEndpoint(new Uri(baseAddress), relative).ToString().ShouldBe(expected);

    [Fact]
    public async Task Model_ids_are_capped_at_the_reported_maximum()
    {
        var entries = string.Join(",", Enumerable.Range(0, ProviderHealthCheckCore.MaxReportedModels + 50).Select(static i => $$"""{"id":"m{{i:D4}}"}"""));
        using var response = Json($$"""{"data":[{{entries}}]}""");

        var models = await ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", null, TestContext.Current.CancellationToken);

        models.Count.ShouldBe(ProviderHealthCheckCore.MaxReportedModels);
    }

    [Fact]
    public async Task Prefix_is_stripped_only_where_it_is_present()
    {
        using var response = Json("""{"models":[{"name":"models/b"},{"name":"a"},{"name":""},{"other":"x"}]}""");

        var models = await ProviderHealthCheckCore.ReadModelIdsAsync(response, "models", "name", "models/", TestContext.Current.CancellationToken);

        models.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Body_that_is_not_json_throws_for_the_caller_to_map()
    {
        using var response = Json("<html>");

        await Should.ThrowAsync<JsonException>(async () =>
            await ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", null, TestContext.Current.CancellationToken));
    }

    private static ValueTask<ModelProviderHealth> CheckAsync(Uri endpoint, TimeSpan timeout)
        => new ProviderHealthCheckCore(ProviderName).CheckAsync(
            endpoint,
            timeout,
            static (_, _) => ValueTask.CompletedTask,
            static (response, token) => ProviderHealthCheckCore.ReadModelIdsAsync(response, "data", "id", null, token),
            TestContext.Current.CancellationToken);

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
