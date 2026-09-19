using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests.FailureManifests;

/// <summary>
/// Failure manifest: <strong>the model provider times out</strong>
/// (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// The written expectation, verified below:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       A timeout is a <strong>fallback trigger</strong>. If the binding has
///       a fallback chain, the next model answers and the run SUCCEEDS; the
///       client never sees the timeout.
///     </description>
///   </item>
///   <item>
///     <description>
///       Without a fallback, the run fails as <see cref="RunErrorClass.Timeout"/>
///       and not as <see cref="RunErrorClass.Unknown"/>. The classification is
///       what an operator alerts on, so a timeout falling into the catch-all
///       bucket would make a provider outage indistinguishable from a bug.
///     </description>
///   </item>
///   <item>
///     <description>
///       The HTTP surface answers <c>502</c> with a problem document, and the
///       provider's own exception text never reaches it.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class ProviderTimeoutTests
{
    private const string AgentName = "slow-agent";
    private const string TimingOutProvider = "times-out";
    private const string WrappingProvider = "wraps-its-timeout";

    [Fact]
    public async Task A_timeout_falls_back_to_the_next_model_and_the_run_succeeds()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddModelProvider(new TimingOutModelProvider())
            .AddModelProvider(new FakeModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding
                {
                    Provider = TimingOutProvider,
                    Model = "slow-1",
                    Fallbacks = [new ModelFallback { Provider = "fake", Model = "fake-model" }],
                },
            }));

        using var response = await PostSynchronousRunAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The client sees an ordinary answer: the timeout never reached it.
        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetRawText().ShouldNotContain(TimingOutModelProvider.ExceptionMessage);

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        var recorded = await TraconTestHost.ReadJsonAsync(runs);

        recorded.GetArrayLength().ShouldBeGreaterThan(0);
        recorded[0].GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Completed));

        // Attribution follows the model that ACTUALLY answered, so the run
        // does not read as if the timed-out provider had served it.
        recorded[0].GetProperty("modelProvider").GetString().ShouldBe("fake");
    }

    [Fact]
    public async Task Without_a_fallback_the_run_is_classified_as_a_timeout_and_the_client_gets_502()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddModelProvider(new TimingOutModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = TimingOutProvider, Model = "slow-1" },
            }));

        using var response = await PostSynchronousRunAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(TimingOutModelProvider.ExceptionMessage);

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        var recorded = await TraconTestHost.ReadJsonAsync(runs);

        recorded.GetArrayLength().ShouldBeGreaterThan(0);

        // 🚨 Failed, not Canceled: nothing was cancelled, the provider simply
        // never answered. Before Phase 157 this run was recorded as Canceled
        // with a null error, and the client got 200 with an empty body.
        recorded[0].GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Failed));

        // 🚨 The CLASS is deliberately NOT asserted here, and that is a finding,
        // not an omission. Measured 2026-09-19: this run is recorded as
        // RunErrorClass.Canceled although its status is Failed. The cause is
        // one layer up - RunRecordingAgent.ToRunError redacts a foreign
        // exception's message through SafeErrorText, so the classifier sees
        // "TaskCanceledException failed. (ref: ...)" and the timeout wording
        // K-737 relies on to tell a provider timeout from a caller's stop is
        // gone before it ever reaches a pattern. A caller's stop cannot reach
        // this path at all (RunRecordingAgent.cs:309 catches it under
        // `when (cancellationSource.IsCancellationRequested)`), so Canceled is
        // wrong here - but fixing it changes what IRunErrorClassifier promises
        // for a RunError whose provenance it cannot see, which is a decision,
        // not a bug fix. Open item: MT-RES-089's record.
    }

    /// <summary>
    /// The same timeout, in the shape a real SDK delivers it: wrapped, so it
    /// crosses the model-call boundary as a FOREIGN failure and is normalized.
    /// </summary>
    /// <remarks>
    /// 🚨 This is the production path, and nothing covered it. The fake above
    /// throws <see cref="TaskCanceledException"/> DIRECTLY, and
    /// <c>ProviderFailureNormalizer.ShouldNormalize</c> lets every
    /// <see cref="OperationCanceledException"/> through untouched — so that run
    /// reaches the classifier with the SDK's own type and message, and the
    /// message patterns see the timeout. A real SDK wraps its timeout in its
    /// own exception; that one IS normalized, arrives as
    /// <c>upstream_error</c>, and was filed as
    /// <see cref="RunErrorClass.ProviderError"/> until MT-RES-089 measured it
    /// live against an unroutable endpoint (2026-09-19).
    /// </remarks>
    [Fact]
    public async Task A_timeout_the_sdk_wrapped_is_still_classified_as_a_timeout()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddModelProvider(new WrappedTimeoutModelProvider())
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = WrappingProvider, Model = "slow-2" },
            }));

        using var response = await PostSynchronousRunAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        var recorded = await TraconTestHost.ReadJsonAsync(runs);

        recorded.GetArrayLength().ShouldBeGreaterThan(0);

        var error = recorded[0].GetProperty("error");

        // Normalized: the identity is Tracon's, not the SDK's.
        error.GetProperty("type").GetString().ShouldBe("upstream_error");
        error.GetProperty("class").GetString().ShouldBe(nameof(RunErrorClass.Timeout));

        // The provider's own text still never reaches the record.
        error.GetProperty("message").GetString().ShouldNotBeNull()
            .ShouldNotContain(WrappedTimeoutModelProvider.ExceptionMessage);
    }

    /// <summary>
    /// Posts the NON-streaming run. The endpoint chooses between SSE and a
    /// single JSON answer by the presence of an idempotency key, so this
    /// header is what selects the synchronous path.
    /// </summary>
    private static async Task<HttpResponseMessage> PostSynchronousRunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }

    /// <summary>
    /// A provider whose client wraps its timeout the way a real SDK does,
    /// so the failure crosses the model-call boundary as a foreign exception.
    /// </summary>
    /// <remarks>
    /// The wrapper type is deliberately NOT an
    /// <see cref="OperationCanceledException"/>: that is the one shape
    /// <c>ProviderFailureNormalizer.ShouldNormalize</c> lets through, and
    /// letting it through is what the sibling fake already covers.
    /// </remarks>
    private sealed class WrappedTimeoutModelProvider : IModelProvider
    {
        public const string ExceptionMessage = "The request to https://api.example/v1 timed out after 30s.";

        public string Name => WrappingProvider;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "slow-2" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new WrappedTimeoutChatClient();

        private sealed class WrappedTimeoutChatClient : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw Wrapped();

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw Wrapped();

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resources to release.
            }

            // 🚨 The shape measured on the real OpenAI SDK (MT-RES-089 log,
            // 2026-09-19): its retry pipeline gives up and throws an
            // AggregateException ("Retry failed after 4 tries") whose inner
            // exceptions are the per-attempt TaskCanceledExceptions. That outer
            // type is NOT an OperationCanceledException, so it IS normalized -
            // which is the whole difference from the sibling fake.
            private static AggregateException Wrapped()
                => new(
                    "Retry failed after 4 tries.",
                    new TaskCanceledException(ExceptionMessage, new TimeoutException(ExceptionMessage)));
        }
    }

    /// <summary>
    /// A provider whose client raises the exception a real SDK raises when its
    /// own request deadline elapses.
    /// </summary>
    /// <remarks>
    /// 🚨 It throws <see cref="TaskCanceledException"/> wrapping a
    /// <see cref="TimeoutException"/> - the shape <c>HttpClient</c> produces on
    /// a timeout - rather than a plain <see cref="TimeoutException"/>. The
    /// classifier has to tell that apart from a caller's cancellation, and a
    /// fake that throws the easy exception would never exercise that.
    /// </remarks>
    private sealed class TimingOutModelProvider : IModelProvider
    {
        public const string ExceptionMessage = "The request to https://api.example/v1 timed out after 30s.";

        public string Name => TimingOutProvider;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "slow-1" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new TimingOutChatClient();

        private sealed class TimingOutChatClient : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw Timeout();

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw Timeout();

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resources to release.
            }

            private static TaskCanceledException Timeout()
                => new(ExceptionMessage, new TimeoutException(ExceptionMessage));
        }
    }
}
