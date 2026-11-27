using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Tracon.Testing's fake, not the one of the infrastructure folder.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every shipped provider SDK, answered with an HTTP error and an EMPTY body: the
/// status is read, so a permanent 4xx neither switches provider nor counts against
/// the circuit.
/// </summary>
/// <remarks>
/// <para>
/// A System.ClientModel client writes an empty or non-OpenAI body as
/// "Service request failed. Status: 400 (Bad Request)" — no "HTTP nnn" text. The
/// status reader saw nothing: the fallback chain took a 401/403/404 for a
/// connection failure and moved to the next provider, and the
/// circuit counted a 400/404 against a credential every tenant shares.
/// </para>
/// <para>
/// The real SDK clients are driven against a local server, so the message each
/// one actually writes is what is read — a hand-written message would only prove
/// the reader matches what the test author guessed.
/// </para>
/// <para>
/// The empty body is the easy case for Gemini: the SDK then writes
/// "Request failed with status code 404", which the text reader finds. A Gemini
/// service answers with a JSON body, and the SDK then writes the service's own
/// sentence as the message, with no status in it; its exception also hides the
/// status the base class reads. The tests with a Google body below are the ones
/// that carry the status of a real answer.
/// </para>
/// </remarks>
public sealed class ProviderErrorStatusTests
{
    public static TheoryData<string, int> ProvidersAndStatuses()
    {
        var data = new TheoryData<string, int>();

        foreach (var provider in new[] { OpenAIProviderNames.ChatCompletions, AzureOpenAIProviderNames.AzureOpenAI, AnthropicProviderNames.Anthropic, GoogleProviderNames.Google })
        {
            foreach (var status in new[] { 400, 401, 403, 404 })
            {
                data.Add(provider, status);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProvidersAndStatuses))]
    public async Task An_empty_body_error_is_read_with_its_status(string providerName, int status)
    {
        await using var server = await StatusServer.StartAsync(status);
        await using var services = BuildServices(providerName, server.BaseAddress);

        var provider = services.GetServices<IModelProvider>().Single(candidate => string.Equals(candidate.Name, providerName, StringComparison.Ordinal));
        using var client = provider.CreateChatClient(new ModelBinding { Provider = providerName, Model = "test-model" });

        var failure = await Should.ThrowAsync<Exception>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken));

        var described = $"{failure.GetType().FullName}: {failure.Message}";

        ProviderHttpStatus.Read(failure).ShouldBe(status, described);
        FallbackRetryClassifier.IsRetryable(failure).ShouldBeFalse($"a permanent {status} must not switch provider: {described}");
        CircuitBreakingChatClient.CountsAgainstProvider(new ForeignProviderInvocationException(failure, ProviderSecretMask.Empty))
            .ShouldBe(status is 401 or 403, $"only an auth failure counts against the shared credential: {described}");
    }

    public static TheoryData<int, string> GoogleAnswers() => new()
    {
        { 400, "INVALID_ARGUMENT" },
        { 401, "UNAUTHENTICATED" },
        { 403, "PERMISSION_DENIED" },
        { 404, "NOT_FOUND" },
        { 429, "RESOURCE_EXHAUSTED" },
        { 500, "INTERNAL" },
        { 503, "UNAVAILABLE" },
    };

    [Theory]
    [MemberData(nameof(GoogleAnswers))]
    public async Task A_gemini_json_error_body_is_read_with_its_status_and_decides_the_retry(int status, string name)
    {
        await using var server = await FakeGeminiServer.StartAsync();
        server.Status = status;
        server.ErrorName = name;

        await using var services = BuildServices(GoogleProviderNames.Google, server.BaseAddress);

        var provider = services.GetServices<IModelProvider>().Single(candidate => string.Equals(candidate.Name, GoogleProviderNames.Google, StringComparison.Ordinal));
        using var client = provider.CreateChatClient(new ModelBinding { Provider = GoogleProviderNames.Google, Model = "test-model" });

        var buffered = await Should.ThrowAsync<Exception>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken));

        var streamed = await Should.ThrowAsync<Exception>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = update;
            }
        });

        var transient = status is 429 or >= 500;

        foreach (var failure in new[] { buffered, streamed })
        {
            var described = $"{failure.GetType().FullName}: {failure.Message}";

            ProviderHttpStatus.Read(failure).ShouldBe(status, described);
            FallbackRetryClassifier.IsRetryable(failure).ShouldBe(transient, described);

            // 401 and 403 count: the shared credential gets the same answer for every caller.
            CircuitBreakingChatClient.CountsAgainstProvider(new ForeignProviderInvocationException(failure, ProviderSecretMask.Empty))
                .ShouldBe(transient || status is 401 or 403, described);

            var invocation = ProviderInvocationException.UpstreamFailure(failure, GoogleProviderNames.Google, ProviderSecretMask.Empty);

            invocation.StatusCode.ShouldBe(status, described);
            invocation.IsTransient.ShouldBe(transient, described);
            invocation.ProviderErrorCode.ShouldBe(name, described);
        }

        server.Calls.ShouldBe(2, "the client does not retry on its own");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_gemini_503_moves_the_call_to_the_fallback_model(bool streamed)
    {
        await using var server = await FakeGeminiServer.StartAsync();
        server.Status = StatusCodes.Status503ServiceUnavailable;
        server.ErrorName = "UNAVAILABLE";

        var fallback = new FakeModelProvider("gemini-fallback").ForModel("fallback-1", static model => model.RespondsWith("from the fallback"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .UseGoogle("test-gemini-key", options => options.Endpoint = server.BaseAddress)
            .AddModelProvider(fallback)
            .AddAgent(GeminiAgent(new ModelBinding
            {
                Provider = GoogleProviderNames.Google,
                Model = "gemini-test",
                Fallbacks = [new ModelFallback { Provider = "gemini-fallback", Model = "fallback-1" }],
            })));

        // Without an idempotency key the run streams; with one it is buffered.
        using var request = new HttpRequestMessage(HttpMethod.Post, GeminiRun)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };

        if (!streamed)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("from the fallback");
        server.Calls.ShouldBe(1, "the client does not retry on its own, the chain moves on");

        var store = host.Services.GetRequiredService<IRunStore>();
        var run = (await store.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        var fallbackEvents = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(run.Id, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (runEvent.Type == RunEventType.ModelFallbackUsed)
            {
                fallbackEvents.Add(runEvent);
            }
        }

        using var payload = JsonDocument.Parse(fallbackEvents.ShouldHaveSingleItem().Payload.ShouldNotBeNull());

        payload.RootElement.GetProperty("reason").GetString().ShouldBe("http_error");
    }

    [Fact]
    public async Task A_gemini_400_does_not_count_against_the_circuit_of_the_shared_credential()
    {
        // The circuit of the setup-time key is shared by every tenant: a request the service rejects as malformed gets
        // the same answer on a healthy provider and says nothing about it. Without the status the 400 counted.
        await using var server = await FakeGeminiServer.StartAsync();
        server.Status = StatusCodes.Status400BadRequest;
        server.ErrorName = "INVALID_ARGUMENT";

        await using var host = await StartCircuitHostAsync(server);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var rejected = await PostBufferedAsync(host);
            rejected.IsSuccessStatusCode.ShouldBeFalse();
        }

        server.Status = StatusCodes.Status200OK;

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        server.Calls.ShouldBe(3, "a circuit that stayed closed lets the third request reach the provider");
    }

    [Fact]
    public async Task A_gemini_503_counts_against_the_circuit_of_the_shared_credential()
    {
        await using var server = await FakeGeminiServer.StartAsync();
        server.Status = StatusCodes.Status503ServiceUnavailable;
        server.ErrorName = "UNAVAILABLE";

        await using var host = await StartCircuitHostAsync(server);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var rejected = await PostBufferedAsync(host);
            rejected.IsSuccessStatusCode.ShouldBeFalse();
        }

        server.Status = StatusCodes.Status200OK;

        using var response = await PostBufferedAsync(host);

        response.IsSuccessStatusCode.ShouldBeFalse();
        server.Calls.ShouldBe(2, "the open circuit stops the third request before the provider");
    }

    private const string GeminiAgentName = "gemini-status-agent";

    private static readonly Uri GeminiRun = new($"/tracon/api/agents/{GeminiAgentName}/run", UriKind.Relative);

    private static AgentDefinition GeminiAgent(ModelBinding model)
        => new()
        {
            Name = GeminiAgentName,
            Instructions = "Answer briefly.",
            Model = model,
            Origin = AgentDefinitionOrigin.Code,
        };

    private static Task<TraconTestHost> StartCircuitHostAsync(FakeGeminiServer server)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseGoogle("test-gemini-key", options => options.Endpoint = server.BaseAddress)
                .AddAgent(GeminiAgent(new ModelBinding { Provider = GoogleProviderNames.Google, Model = "gemini-test" })),
            configureServices: static services => services.Configure<TraconOptions>(options =>
            {
                options.CircuitBreaker.FailureThreshold = 2;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            }));

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GeminiRun)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = Guid.NewGuid().ToString("N") }),
        };

        // The Idempotency-Key header selects the single JSON response.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildServices(string providerName, Uri endpoint)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var tracon = services.AddTracon();

        _ = providerName switch
        {
            OpenAIProviderNames.ChatCompletions => tracon.UseOpenAI("test-key", options => options.Endpoint = endpoint),
            AzureOpenAIProviderNames.AzureOpenAI => tracon.UseAzureOpenAI(endpoint, "test-key"),
            AnthropicProviderNames.Anthropic => tracon.UseAnthropic("test-key", options =>
            {
                options.Endpoint = endpoint;
                options.MaxRetries = 0;
            }),
            _ => tracon.UseGoogle("test-key", options => options.Endpoint = endpoint),
        };

        return services.BuildServiceProvider();
    }

    /// <summary>Answers every request with one status and an empty body.</summary>
    private sealed class StatusServer(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public static async Task<StatusServer> StartAsync(int status)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var app = builder.Build();
            app.Run(context =>
            {
                context.Response.StatusCode = status;

                return Task.CompletedTask;
            });

            await app.StartAsync().ConfigureAwait(false);

            return new StatusServer(app, new Uri($"{app.Urls.First()}/"));
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
