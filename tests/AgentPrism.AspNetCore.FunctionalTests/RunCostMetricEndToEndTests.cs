using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 35 — <c>agentprism.run.cost</c> is verified end to end through a real
/// DI registration and a real HTTP request.
/// </summary>
/// <remarks>
/// The unit tests in <c>AgentPrism.Core.UnitTests</c> wire up
/// <see cref="RunRecordingAgent"/> by hand; this CAN MISS a wiring break in
/// <c>AddAgentPrism()</c>'s real registration chain (metric, provider, run
/// recording wrapper) (Phase 28 lesson: "a probe program does not prove the
/// real pipeline"). This test builds that chain FOR REAL with <c>TestHost</c>
/// and, because <see cref="MeterListener"/> runs in the SAME process, can see
/// the measurement <see cref="AgentPrismMetrics"/> produces.
/// </remarks>
public sealed class RunCostMetricEndToEndTests
{
    [Fact]
    public async Task Run_cost_is_published_through_the_real_DI_chain()
    {
        using var collector = new MetricCollector(AgentPrismDiagnostics.MeterName);

        var provider = new PricedEchoModelProvider();

        await using var host = await AgentPrismTestHost.StartAsync(builder =>
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
        });

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/priced/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // CompleteAsync (and therefore RecordCost) may not have run yet
        // unless the SSE stream is fully consumed.
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var measurement = collector.DoubleMeasurements(AgentPrismDiagnostics.RunCostCounterName).ShouldHaveSingleItem();

        // $2/M input * 1,000,000 + $4/M output * 500,000 = 4.0
        measurement.Value.ShouldBe(4.0);
        measurement.Tags[AgentPrismDiagnostics.Tags.AgentName].ShouldBe("priced");
        measurement.Tags[AgentPrismDiagnostics.Tags.ModelId].ShouldBe("priced-model");
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

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null) => _client;

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

    /// <summary>A simple listener that collects the measurements of a given <c>Meter</c>.</summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _doubles = [];
        private readonly Lock _gate = new();

        public MetricCollector(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _doubles.Add((instrument.Name, value, ToDictionary(tags)));
                }
            });

            _listener.Start();
        }

        public List<(double Value, Dictionary<string, object?> Tags)> DoubleMeasurements(string name)
        {
            lock (_gate)
            {
                return [.. _doubles
                    .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                    .Select(m => (m.Value, m.Tags))];
            }
        }

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                result[tag.Key] = tag.Value;
            }

            return result;
        }
    }
}
