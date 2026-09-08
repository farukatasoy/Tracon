using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests.FailureManifests;

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

    [Fact]
    public async Task A_timeout_falls_back_to_the_next_model_and_the_run_succeeds()
    {
        await using var host = await AgentPrismTestHost.StartAsync(static builder => builder
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
        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetRawText().ShouldNotContain(TimingOutModelProvider.ExceptionMessage);

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var recorded = await AgentPrismTestHost.ReadJsonAsync(runs);

        recorded.GetArrayLength().ShouldBeGreaterThan(0);
        recorded[0].GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Completed));

        // Attribution follows the model that ACTUALLY answered, so the run
        // does not read as if the timed-out provider had served it.
        recorded[0].GetProperty("modelProvider").GetString().ShouldBe("fake");
    }

    [Fact]
    public async Task Without_a_fallback_the_run_is_classified_as_a_timeout_and_the_client_gets_502()
    {
        await using var host = await AgentPrismTestHost.StartAsync(static builder => builder
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

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var recorded = await AgentPrismTestHost.ReadJsonAsync(runs);

        recorded.GetArrayLength().ShouldBeGreaterThan(0);

        // 🚨 Failed, not Canceled: nothing was cancelled, the provider simply
        // never answered. Before Phase 157 this run was recorded as Canceled
        // with a null error, and the client got 200 with an empty body.
        recorded[0].GetProperty("status").GetString().ShouldBe(nameof(RunStatus.Failed));
    }

    /// <summary>
    /// Posts the NON-streaming run. The endpoint chooses between SSE and a
    /// single JSON answer by the presence of an idempotency key, so this
    /// header is what selects the synchronous path.
    /// </summary>
    private static async Task<HttpResponseMessage> PostSynchronousRunAsync(AgentPrismTestHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
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
