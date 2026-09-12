using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 35 — <c>tracon.run.cost</c> is verified end to end through a real
/// DI registration and a real HTTP request.
/// </summary>
/// <remarks>
/// The unit tests in <c>Tracon.Core.UnitTests</c> wire up
/// <see cref="RunRecordingAgent"/> by hand; this CAN MISS a wiring break in
/// <c>AddTracon()</c>'s real registration chain (metric, provider, run
/// recording wrapper) (Phase 28 lesson: "a probe program does not prove the
/// real pipeline"). This test builds that chain FOR REAL with <c>TestHost</c>
/// and, because <see cref="MeterListener"/> runs in the SAME process, can see
/// the measurement <see cref="TraconMetrics"/> produces.
/// </remarks>
public sealed class RunCostMetricEndToEndTests
{
    [Fact]
    public async Task Run_cost_is_published_through_the_real_DI_chain()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new MeterInstanceCollector(meterFactory.Meter);

        var provider = new PricedEchoModelProvider();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = "priced",
                DisplayName = "Priced Agent",
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "priced-fake", Model = "priced-model" },
                Origin = AgentDefinitionOrigin.Code,
            });
        },
            configureServices: services => services.AddSingleton<IMeterFactory>(meterFactory));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/priced/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // CompleteAsync (and therefore RecordCost) may not have run yet
        // unless the SSE stream is fully consumed.
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var measurement = collector.Snapshot(TraconDiagnostics.RunCostCounterName).ShouldHaveSingleItem();

        // $2/M input * 1,000,000 + $4/M output * 500,000 = 4.0
        measurement.Value.ShouldBe(4.0);
        measurement.Tags[TraconDiagnostics.Tags.AgentName].ShouldBe("priced");
        measurement.Tags[TraconDiagnostics.Tags.ModelId].ShouldBe("priced-model");
    }

    /// <summary>A fixed-price fake provider that never reaches the network.</summary>
    private sealed class PricedEchoModelProvider : IModelProvider, IDisposable
    {
        private readonly PricedChatClient _client = new();

        public string Name => "priced-fake";

        public IReadOnlyList<ModelDescriptor> Models { get; } =
        [
            new ModelDescriptor
            {
                Name = "priced-model",
                ContextWindowTokens = 8_192,
                MaxOutputTokens = 1_024,
                InputCostPerMillionTokens = 2m,
                OutputCostPerMillionTokens = 4m,
            },
        ];

        public IChatClient CreateChatClient(ModelBinding binding) => _client;

        public void Dispose() => _client.Dispose();

        private sealed class PricedChatClient : IChatClient
        {
            private static readonly UsageDetails Usage = new()
            {
                InputTokenCount = 1_000_000,
                OutputTokenCount = 500_000,
                TotalTokenCount = 1_500_000,
            };

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")) { Usage = Usage });

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "tamam")
                {
                    Contents = [new UsageContent(Usage)],
                };

                await Task.CompletedTask;
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resources to release.
            }
        }
    }

}
