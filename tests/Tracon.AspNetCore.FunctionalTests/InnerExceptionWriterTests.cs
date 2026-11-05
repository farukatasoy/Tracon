using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A writer that walks the inner exceptions of a provider failure, frame by frame, reaches no key: neither the key a
/// provider was set up with nor the key of a tenant binding.
/// </summary>
/// <remarks>
/// <para>
/// An error tracker or a structured exception formatter does not stop at the outermost message. It sends the type, the
/// message, the stack trace and the data of every exception it can reach through the inner exception and through the
/// branches of an aggregate. The failing provider here throws an aggregate (as an SDK's own retry loop does) whose
/// inner exception quotes the key it was called with, so the key sits in a frame no outer message shows.
/// </para>
/// <para>
/// Both calls are made against one host: a tenant with a binding (the call is made with that tenant's key) and a tenant
/// without one (the call is made with the key the provider was set up with, which the provider reports). The log probe
/// writes each record the way such a writer does.
/// </para>
/// </remarks>
public sealed class InnerExceptionWriterTests
{
    private const string Provider = "writer-provider";
    private const string AgentName = "writer-agent";
    private const string TenantWithBinding = "tenant-a";
    private const string TenantWithoutBinding = "tenant-b";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string KeyName = "Tracon:ProviderKeys:tenant-a:WriterProvider";
    private const string TenantKey = "canary-writer-tenant-key-7e1d90b4";
    private const string SetupKey = "canary-writer-setup-key-2c58a3f6";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_writer_that_walks_the_inner_exceptions_reaches_neither_the_setup_key_nor_a_tenant_key(bool streamed)
    {
        var structured = new StructuredLogProbe();

        await using var host = await StartAsync(structured, new WritingProvider(reportSetupKey: true));

        var withBinding = await RunAsync(host, TenantWithBinding, streamed);
        var withoutBinding = await RunAsync(host, TenantWithoutBinding, streamed);

        // Both calls reached the provider and failed there (an event stream answers 200 and carries the error in its
        // events), and the writer walked a graph with a view in it.
        if (!streamed)
        {
            withBinding.Status.ShouldBe(HttpStatusCode.BadGateway, withBinding.Body);
            withoutBinding.Status.ShouldBe(HttpStatusCode.BadGateway, withoutBinding.Body);
        }

        structured.Text.ShouldContain("frame.type=Tracon.MaskedFailureView", Case.Sensitive);
        structured.Text.ShouldContain("[redacted]", Case.Sensitive);

        foreach (var written in new[] { structured.Text, host.Logs.AllText, withBinding.Body, withoutBinding.Body })
        {
            written.ShouldNotContain(TenantKey, Case.Sensitive);
            written.ShouldNotContain(SetupKey, Case.Sensitive);
        }
    }

    [Fact]
    public async Task A_provider_that_does_not_report_its_setup_key_leaves_it_in_the_frame_a_writer_reaches()
    {
        // The documented limit of the member: a value Tracon was not told of is not looked for. The tenant's key is
        // still replaced, because that mask comes from the credential the call was resolved with.
        var structured = new StructuredLogProbe();

        await using var host = await StartAsync(structured, new WritingProvider(reportSetupKey: false));

        _ = await RunAsync(host, TenantWithBinding, streamed: false);
        _ = await RunAsync(host, TenantWithoutBinding, streamed: false);

        structured.Text.ShouldNotContain(TenantKey, Case.Sensitive);
        structured.Text.ShouldContain(SetupKey, Case.Sensitive);
    }

    private static async Task<TraconTestHost> StartAsync(StructuredLogProbe structured, WritingProvider provider)
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    })
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer.",
                        Model = new ModelBinding { Provider = Provider, Model = "writer-1" },
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureServices: services =>
            {
                services.AddLogging(logging => logging.AddProvider(structured));
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection([new(KeyName, TenantKey)])
                    .Build());
                services.Configure<TraconOptions>(static options => options.CircuitBreaker.FailureThreshold = 1000);
            });

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = TenantWithBinding,
                ProviderName = Provider,
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        return host;
    }

    private static async Task<(HttpStatusCode Status, string Body)> RunAsync(TraconTestHost host, string tenant, bool streamed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add(TenantHeader, tenant);

        if (!streamed)
        {
            // The header selects the single JSON answer instead of the event stream.
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // What an SDK's own retry loop throws: an aggregate whose inner exception carries the provider's answer.
    private static AggregateException Refused(string key)
        => new(
            "Retry failed after 4 tries.",
            new HttpRequestException($"HTTP 401 Incorrect API key provided: {key}.", inner: null, HttpStatusCode.Unauthorized));

    private sealed class WritingProvider(bool reportSetupKey) : ITenantCredentialModelProvider
    {
        public string Name => Provider;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "writer-1" }];

        public IReadOnlyList<string> SetupApiKeys => reportSetupKey ? [SetupKey] : [];

        public IChatClient CreateChatClient(ModelBinding binding) => new RefusingChatClient(Refused(SetupKey));

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);

            return new RefusingChatClient(Refused(credential.ApiKey));
        }
    }

    private sealed class RefusingChatClient(Exception failure) : IChatClient
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
}
