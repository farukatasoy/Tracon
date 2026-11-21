using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// Every shipped provider SDK, answered with a <c>200</c> whose body cannot be read: the call did reach the provider
/// and an answer came back, so sending it again can change the result, and the call moves to the next model of a
/// fallback chain.
/// </summary>
/// <remarks>
/// <para>
/// An empty body, a body cut short and a page that is not JSON make the SDK throw a JSON reader failure of its own.
/// It carries no HTTP status (the answer was a <c>200</c>) and it is not one of the transport types the retry rules
/// name, so it used to read as a failure no later try could change.
/// </para>
/// <para>
/// The real SDK clients are driven against a local server, so the exception each one throws is the one a run meets.
/// The client is wrapped by the same ring that tags a failure at the provider boundary in a real pipeline, because
/// the ring is where the failure gets its mark.
/// </para>
/// </remarks>
public sealed class UnreadableProviderResponseTests
{
    private const string TruncatedJson = "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"par";

    public static TheoryData<string, string> ProvidersAndBodies()
    {
        var data = new TheoryData<string, string>();

        foreach (var provider in new[] { OpenAIProviderNames.ChatCompletions, AzureOpenAIProviderNames.AzureOpenAI, AnthropicProviderNames.Anthropic, GoogleProviderNames.Google })
        {
            foreach (var body in new[] { "empty", "truncated", "html" })
            {
                data.Add(provider, body);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProvidersAndBodies))]
    public async Task An_answer_that_cannot_be_read_is_transient_and_carries_no_status(string providerName, string bodyKind)
    {
        await using var server = await AnswerServer.StartAsync(bodyKind);
        await using var services = BuildServices(providerName, server.BaseAddress);

        var provider = services.GetServices<IModelProvider>().Single(candidate => string.Equals(candidate.Name, providerName, StringComparison.Ordinal));
        using var raw = provider.CreateChatClient(new ModelBinding { Provider = providerName, Model = "test-model" });
        using var client = new ProviderFailureTaggingChatClient(raw, ProviderSecretMask.Empty);

        var failure = await Should.ThrowAsync<ForeignProviderInvocationException>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken));

        var described = $"{providerName}, {bodyKind}: {failure.Original.GetType().FullName}: {failure.Original.Message}";

        ProviderHttpStatus.Read(failure).ShouldBeNull(described);
        FallbackRetryClassifier.IsRetryable(failure).ShouldBeTrue(described);
        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.UnreadableResponse, described);
        ProviderInvocationException.UpstreamFailure(failure.Original, providerName, ProviderSecretMask.Empty).IsTransient.ShouldBeTrue(described);
        CircuitBreakingChatClient.CountsAgainstProvider(failure).ShouldBeTrue($"an unreadable answer counts against the provider like a failed connection: {described}");
    }

    public static TheoryData<string> Providers()
        => [OpenAIProviderNames.ChatCompletions, AzureOpenAIProviderNames.AzureOpenAI, AnthropicProviderNames.Anthropic, GoogleProviderNames.Google];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_streamed_event_that_cannot_be_read_is_transient_before_the_first_update(string providerName)
    {
        // The Anthropic stream reader only reads an event that names its type; the others read a data line alone.
        var kind = string.Equals(providerName, AnthropicProviderNames.Anthropic, StringComparison.Ordinal) ? "sse-typed" : "sse-garbled";

        await using var server = await AnswerServer.StartAsync(kind);
        await using var services = BuildServices(providerName, server.BaseAddress);

        var provider = services.GetServices<IModelProvider>().Single(candidate => string.Equals(candidate.Name, providerName, StringComparison.Ordinal));
        using var raw = provider.CreateChatClient(new ModelBinding { Provider = providerName, Model = "test-model" });
        using var client = new ProviderFailureTaggingChatClient(raw, ProviderSecretMask.Empty);
        var updates = 0;

        var failure = await Should.ThrowAsync<ForeignProviderInvocationException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken))
            {
                updates++;
                _ = update;
            }
        });

        var described = $"{providerName}: {updates} update(s), {failure.Original.GetType().FullName}: {failure.Original.Message}";

        updates.ShouldBe(0, described);
        ProviderHttpStatus.Read(failure).ShouldBeNull(described);
        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.UnreadableResponse, described);
    }

    /// <summary>
    /// A limit, pinned so it is visible: the Google stream reader reports some malformed streams with an exception that
    /// carries no line number and no inner exception, which nothing can tell from any other failure.
    /// </summary>
    [Fact]
    public async Task A_google_stream_that_ends_inside_a_segment_is_not_classified()
    {
        await using var server = await AnswerServer.StartAsync("sse-typed");
        await using var services = BuildServices(GoogleProviderNames.Google, server.BaseAddress);

        var provider = services.GetServices<IModelProvider>().Single(candidate => string.Equals(candidate.Name, GoogleProviderNames.Google, StringComparison.Ordinal));
        using var raw = provider.CreateChatClient(new ModelBinding { Provider = GoogleProviderNames.Google, Model = "test-model" });
        using var client = new ProviderFailureTaggingChatClient(raw, ProviderSecretMask.Empty);

        var failure = await Should.ThrowAsync<ForeignProviderInvocationException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = update;
            }
        });

        failure.Original.ShouldBeOfType<InvalidOperationException>();
        FallbackRetryClassifier.Classify(failure).ShouldBe(FallbackSkipReason.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unreadable_answer_from_the_primary_moves_the_call_to_the_fallback_model(bool streamed)
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionOverride = async (context, _) =>
        {
            context.Response.StatusCode = 200;

            if (streamed)
            {
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync("data: {this is not json}\n\n", Encoding.UTF8, context.RequestAborted);

                return;
            }

            context.Response.ContentType = "application/json";
            context.Response.ContentLength = 0;
        };

        var fallback = new FakeModelProvider("unreadable-fallback").ForModel("fallback-1", static model => model.RespondsWith("from the fallback"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .UseOpenAICompatible("unreadable-primary", options => options.Endpoint = server.BaseAddress)
            .AddModelProvider(fallback)
            .AddAgent(new AgentDefinition
            {
                Name = "unreadable-agent",
                Instructions = "Answer.",
                Model = new ModelBinding
                {
                    Provider = "unreadable-primary",
                    Model = "fake-local-model",
                    Fallbacks = [new ModelFallback { Provider = "unreadable-fallback", Model = "fallback-1" }],
                },
                Origin = AgentDefinitionOrigin.Code,
            }));

        // Without an idempotency key the run streams; with one it is buffered.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/unreadable-agent/run", UriKind.Relative))
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
        server.ChatCompletionCallCount.ShouldBe(1, "the SDK does not retry an answer it cannot read, the chain moves on");

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

        payload.RootElement.GetProperty("reason").GetString().ShouldBe("unreadable_response");
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

    /// <summary>Answers every request with a <c>200</c> and a body the SDKs cannot read.</summary>
    private sealed class AnswerServer(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public static async Task<AnswerServer> StartAsync(string bodyKind)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var app = builder.Build();
            app.Run(async context =>
            {
                context.Response.StatusCode = 200;

                switch (bodyKind)
                {
                    case "empty":
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength = 0;
                        break;
                    case "truncated":
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(TruncatedJson, Encoding.UTF8, context.RequestAborted);
                        break;
                    case "html":
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync("<html><body>The service is down for maintenance.</body></html>", Encoding.UTF8, context.RequestAborted);
                        break;
                    case "sse-typed":
                        context.Response.ContentType = "text/event-stream";
                        await context.Response.WriteAsync("event: message_start\ndata: {this is not json}\n\n", Encoding.UTF8, context.RequestAborted);
                        break;
                    default:
                        context.Response.ContentType = "text/event-stream";
                        await context.Response.WriteAsync("data: {this is not json}\n\n", Encoding.UTF8, context.RequestAborted);
                        break;
                }
            });

            await app.StartAsync().ConfigureAwait(false);

            return new AnswerServer(app, new Uri($"{app.Urls.First()}/"));
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
