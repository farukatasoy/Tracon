using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        CircuitBreakingChatClient.CountsAgainstProvider(new ForeignProviderInvocationException(failure))
            .ShouldBe(status is 401 or 403, $"only an auth failure counts against the shared credential: {described}");
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
