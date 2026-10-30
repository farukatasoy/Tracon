using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Verifies which failures the circuit breaker counts against a provider.
/// </summary>
/// <remarks>
/// <para>
/// The circuit of the setup-time credential is shared by every tenant. A failure
/// that says nothing about the provider's health must not count: a tenant could
/// otherwise open the circuit for all the others with its own bad requests. The
/// provider is unhealthy when it cannot be reached, times out, answers 5xx, 408 or
/// 429, or rejects the shared credential (401/403, which an earlier defect depends on).
/// Every other permanent 4xx belongs to the request, not to the provider.
/// </para>
/// <para>
/// Every test goes through <see cref="ModelProviderRegistry"/>: the rule depends on
/// where a failure comes from, and only the real pipeline shows that.
/// </para>
/// </remarks>
public sealed class CircuitBreakerFailureClassificationTests
{
    private const string Provider = "fake";

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(422)]
    public async Task A_permanent_client_error_from_the_provider_does_not_count(int status)
    {
        var raw = new ThrowingChatClient(() => new FakeProviderSdkException($"HTTP {status} (invalid_request_error)"));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));
        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeFalse($"HTTP {status} rejects the request, not the provider.");
        raw.CallCount.ShouldBe(2, "an open circuit would have stopped the second call before the provider.");
    }

    [Fact]
    public async Task A_permanent_client_error_carried_only_by_the_status_code_does_not_count()
    {
        // The message .NET writes for EnsureSuccessStatusCode has no "HTTP nnn" text.
        var raw = new ThrowingChatClient(() => new HttpRequestException(
            "Response status code does not indicate success: 400 (Bad Request).",
            inner: null,
            HttpStatusCode.BadRequest));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task An_authentication_failure_still_opens_the_circuit(int status)
    {
        var raw = new ThrowingChatClient(() => new FakeProviderSdkException($"HTTP {status} (invalid_api_key)"));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeTrue("a rejected shared credential fails every caller in the same way.");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(408)]
    [InlineData(429)]
    public async Task A_transient_status_still_opens_the_circuit(int status)
    {
        var raw = new ThrowingChatClient(() => new FakeProviderSdkException($"HTTP {status} (server_error)"));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_connection_failure_without_a_status_still_opens_the_circuit()
    {
        var raw = new ThrowingChatClient(() => new HttpRequestException(
            HttpRequestError.ConnectionError,
            "Connection refused (localhost:1)"));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeTrue("A connection failure opens the circuit.");
    }

    [Fact]
    public async Task A_provider_timeout_still_opens_the_circuit()
    {
        // HttpClient reports its own deadline as a cancellation that nobody requested.
        var raw = new ThrowingChatClient(() => new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
            new TimeoutException()));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task An_exhausted_run_budget_does_not_count()
    {
        // Same class: the budget ring sits inside the circuit, the
        // request never reaches the network, and the decision belongs to one
        // tenant's run tree (the same reasoning as the content block).
        var raw = new ThrowingChatClient(() => new InvalidOperationException("The provider must not be called."));
        var (breaker, client) = Pipeline(raw);

        var budget = new AgentRunBudget { MaxTotalTokens = 10 };
        budget.RecordUsage(11);

        var runId = Guid.NewGuid();
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = runId, RootRunId = runId, Budget = budget });

        try
        {
            await Should.ThrowAsync<TraconRunBudgetExceededException>(
                () => client.GetResponseAsync([User()], cancellationToken: Token));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        raw.CallCount.ShouldBe(0);
        breaker.IsOpen(Provider, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_attachment_does_not_count()
    {
        // The attachment ring sits inside the circuit: a history that names a
        // deleted attachment fails before the provider is called.
        var raw = new ThrowingChatClient(() => new InvalidOperationException("The provider must not be called."));
        var (breaker, client) = Pipeline(
            raw,
            attachmentStore: new InMemoryAttachmentStore(),
            tenantContext: new FixedTenantContext("tenant-a"));

        var message = new ChatMessage(
            ChatRole.User,
            [new UriContent(AttachmentUriReference.Create("/tracon", Guid.NewGuid()), "image/png")]);

        await Should.ThrowAsync<TraconException>(() => client.GetResponseAsync([message], cancellationToken: Token));

        raw.CallCount.ShouldBe(0);
        breaker.IsOpen(Provider, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_tool_failure_that_the_tool_loop_rethrows_does_not_count()
    {
        // FunctionInvokingChatClient rethrows a tool's exception after
        // MaximumConsecutiveErrorsPerRequest (3) failed iterations. The model
        // answered every call; only the tool failed.
        var calls = 0;
        var raw = new FakeChatClient(_ => new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent($"call-{Interlocked.Increment(ref calls)}", "broken_tool")])));
        var (breaker, client) = Pipeline(raw);

        var tool = AIFunctionFactory.Create(
            new Func<string>(() => throw new InvalidOperationException("The tool's backend is down.")),
            "broken_tool");

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.GetResponseAsync([User()], new ChatOptions { Tools = [tool] }, Token));

        raw.CallCount.ShouldBe(4);
        breaker.IsOpen(Provider, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(401, true)]
    [InlineData(503, true)]
    public async Task The_streaming_path_applies_the_same_rule(int status, bool opens)
    {
        var raw = new ThrowingChatClient(() => new FakeProviderSdkException($"HTTP {status} (error)"));
        var (breaker, client) = Pipeline(raw);

        await Should.ThrowAsync<Exception>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([User()], cancellationToken: Token))
            {
            }
        });

        breaker.IsOpen(Provider, out _).ShouldBe(opens);
    }

    [Fact]
    public async Task A_permanent_client_error_on_the_half_open_trial_does_not_reopen_the_circuit()
    {
        // The failure is not counted, so it is neither the trial's success nor
        // its failure: the circuit stays half-open (the same as a content block).
        var time = new ManualTimeProvider();
        var status = 503;
        var raw = new ThrowingChatClient(() => new FakeProviderSdkException($"HTTP {status} (error)"));
        var (breaker, client) = Pipeline(raw, time: time);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));
        breaker.IsOpen(Provider, out _).ShouldBeTrue();

        time.Advance(TimeSpan.FromSeconds(31));
        status = 400;

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync([User()], cancellationToken: Token));

        breaker.IsOpen(Provider, out _).ShouldBeFalse();
        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ChatMessage User() => new(ChatRole.User, "hello");

    private static (ModelProviderCircuitBreaker Breaker, IChatClient Client) Pipeline(
        IChatClient raw,
        IAttachmentStore? attachmentStore = null,
        ITenantContext? tenantContext = null,
        TimeProvider? time = null)
    {
        var breaker = new ModelProviderCircuitBreaker(
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
            {
                CircuitBreaker = new TraconCircuitBreakerOptions
                {
                    Enabled = true,
                    FailureThreshold = 1,
                    BreakDuration = TimeSpan.FromSeconds(30),
                },
            }),
            time);

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(raw)],
            breaker,
            attachmentStore: attachmentStore,
            tenantContext: tenantContext);

        return (breaker, registry.CreateChatClient(TestData.Binding()));
    }

    /// <summary>
    /// Stands in for a provider SDK exception (OpenAI's <c>ClientResultException</c>):
    /// it derives from <see cref="Exception"/> and carries the status as "HTTP nnn" text.
    /// </summary>
    private sealed class FakeProviderSdkException(string message) : Exception(message);

    private sealed class ThrowingChatClient(Func<Exception> failure) : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw failure();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Yield();
            throw failure();
#pragma warning disable CS0162 // The iterator needs a yield statement to compile as one.
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resource to release.
        }
    }
}
