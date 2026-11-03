using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What Tracon writes about a provider failure carries the provider's text and not the key the call was made with, for
/// the paths the canary run does not drive: a fallback chain that ends in failure, a text that quotes a value that is
/// not the call's own, and an exception whose text cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// The failing provider here is a scripted <see cref="ITenantCredentialModelProvider"/>: it builds a client with the
/// tenant's resolved key and the client throws an exception that quotes that key, so the key that was really used is the
/// key that has to be gone. A chain that ends in failure is the case of a public exception (the provider-unavailable
/// error) that carries the first failure as its inner exception, written by every logger above the model call.
/// </para>
/// <para>
/// The circuit threshold is raised: an open circuit answers with its own sentence, and a call that never reached the
/// provider quotes nothing.
/// </para>
/// </remarks>
public sealed class TenantProviderFailureTextTests
{
    private const string PrimaryProvider = "quote-primary";
    private const string FallbackProvider = "quote-fallback";
    private const string AgentName = "quote-agent";
    private const string Tenant = "tenant-a";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string PrimaryKeyName = "Tracon:ProviderKeys:tenant-a:QuotePrimary";
    private const string FallbackKeyName = "Tracon:ProviderKeys:tenant-a:QuoteFallback";
    private const string PrimaryKey = "canary-primary-link-3c8e51a7d4";
    private const string FallbackKey = "canary-fallback-link-9b24d06fe1";

    // A value no tenant of this host holds: a text that quotes it quotes something that is not the call's own.
    private const string NotTheCallsOwn = "canary-not-the-calls-own-61f0aa83bc";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_fallback_chain_that_ends_in_failure_writes_neither_link_key(bool streamed)
    {
        await using var host = await StartAsync(
            Binding(withFallback: true),
            new QuotingProvider(PrimaryProvider, key => Unavailable($"The primary refused {key}.")),
            new QuotingProvider(FallbackProvider, key => Unavailable($"The fallback refused {key}.")));

        var response = await RunAsync(host, streamed);

        // The chain tried both links and every logger above the model call wrote the failure.
        host.Logs.AllText.ShouldContain("All providers in the fallback chain failed", Case.Sensitive);
        host.Logs.AllText.ShouldContain("The primary refused [redacted].", Case.Sensitive);
        host.Logs.AllText.ShouldNotContain(PrimaryKey, Case.Sensitive);
        host.Logs.AllText.ShouldNotContain(FallbackKey, Case.Sensitive);
        response.ShouldNotContain(PrimaryKey, Case.Sensitive);
        response.ShouldNotContain(FallbackKey, Case.Sensitive);
    }

    [Fact]
    public async Task A_value_that_is_not_the_calls_own_is_left_as_it_is()
    {
        await using var host = await StartAsync(
            Binding(withFallback: false),
            new QuotingProvider(PrimaryProvider, key => Unavailable($"The provider refused {key} and also {NotTheCallsOwn}.")),
            fallback: null);

        await RunAsync(host, streamed: false);

        // The mask holds the value of this call and nothing else: no call's text changes because of another tenant's key.
        host.Logs.AllText.ShouldContain("The provider refused [redacted] and also " + NotTheCallsOwn, Case.Sensitive);
        host.Logs.AllText.ShouldNotContain(PrimaryKey, Case.Sensitive);
    }

    [Fact]
    public async Task A_failure_whose_text_cannot_be_written_ends_the_run_with_the_normal_error_and_shows_nothing_raw()
    {
        await using var host = await StartAsync(
            Binding(withFallback: false),
            new QuotingProvider(PrimaryProvider, key => new TextlessUpstreamException($"HTTP 401 The provider refused {key}.")),
            fallback: null);

        var (status, response) = await RunWithStatusAsync(host, streamed: false);

        // The run ends the way every provider failure ends: 502 and a body that names no provider text.
        status.ShouldBe(HttpStatusCode.BadGateway, response);
        response.ShouldNotContain(PrimaryKey, Case.Sensitive);
        host.Logs.AllText.ShouldNotContain(PrimaryKey, Case.Sensitive);
        host.Logs.AllText.ShouldContain("withheld because it could not be masked", Case.Sensitive);
    }

    private static HttpRequestException Unavailable(string message)
        => new(message, inner: null, HttpStatusCode.ServiceUnavailable);

    private static ModelBinding Binding(bool withFallback)
        => new()
        {
            Provider = PrimaryProvider,
            Model = "primary-1",
            Fallbacks = withFallback ? [new ModelFallback { Provider = FallbackProvider, Model = "fallback-1" }] : [],
        };

    private static async Task<TraconTestHost> StartAsync(ModelBinding binding, QuotingProvider primary, QuotingProvider? fallback)
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(primary)
                    .UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    })
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer.",
                        Model = binding,
                        Origin = AgentDefinitionOrigin.Code,
                    });

                if (fallback is not null)
                {
                    builder.AddModelProvider(fallback);
                }
            },
            configureServices: static services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection([new(PrimaryKeyName, PrimaryKey), new(FallbackKeyName, FallbackKey)])
                    .Build());

                services.Configure<TraconOptions>(static options => options.CircuitBreaker.FailureThreshold = 1000);
            });

        var store = host.Services.GetRequiredService<ITenantProviderBindingStore>();

        foreach (var (provider, keyName) in (ValueTuple<string, string>[])[(PrimaryProvider, PrimaryKeyName), (FallbackProvider, FallbackKeyName)])
        {
            await store.UpsertAsync(
                new TenantProviderBinding
                {
                    TenantId = Tenant,
                    ProviderName = provider,
                    ApiKeyConfigurationName = keyName,
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                TestContext.Current.CancellationToken);
        }

        return host;
    }

    private static async Task<string> RunAsync(TraconTestHost host, bool streamed)
        => (await RunWithStatusAsync(host, streamed)).Body;

    private static async Task<(HttpStatusCode Status, string Body)> RunWithStatusAsync(TraconTestHost host, bool streamed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add(TenantHeader, Tenant);

        if (!streamed)
        {
            // The header selects the single JSON answer instead of the event stream.
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A provider whose client throws what <paramref name="failure"/> builds from the key the client was made with.</summary>
    private sealed class QuotingProvider(string name, Func<string, Exception> failure) : ITenantCredentialModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "primary-1" }, new ModelDescriptor { Name = "fallback-1" }];

        public IChatClient CreateChatClient(ModelBinding binding)
            => throw new InvalidOperationException("The tenant binding gives this provider a key, so the key-less overload is not used.");

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);

            return new QuotingChatClient(failure(credential.ApiKey));
        }
    }

    private sealed class QuotingChatClient(Exception failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw failure;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw failure;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>An upstream exception that reads its message and cannot write its full text.</summary>
    private sealed class TextlessUpstreamException(string message) : Exception(message)
    {
        public override string ToString() => throw new InvalidOperationException("the text cannot be written");
    }
}
