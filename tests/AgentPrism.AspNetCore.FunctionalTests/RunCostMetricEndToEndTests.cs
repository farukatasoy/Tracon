using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Faz 35 — <c>agentprism.run.cost</c> gercek DI kaydi ve gercek HTTP istegi
/// uzerinden uctan uca dogrulanir.
/// </summary>
/// <remarks>
/// <c>AgentPrism.Core.UnitTests</c> tarafindaki birim testleri
/// <see cref="RunRecordingAgent"/>'i elle kurar; bu, <c>AddAgentPrism()</c>'in
/// gercek kayit zincirinde (metrik, saglayici, calistirma kaydi sarmalayicisi)
/// bir tel kopuklugunu KACIRABILIR (Faz 28 dersi: "bir prob programi gercek
/// boru hattini kanitlamaz"). Bu test o zinciri <c>TestHost</c> ile GERCEKTEN
/// kurar ve <see cref="MeterListener"/> AYNI surecte oldugu icin
/// <see cref="AgentPrismMetrics"/>'in urettigi olcumu gorebilir.
/// </remarks>
public sealed class RunCostMetricEndToEndTests
{
    [Fact]
    public async Task Gercek_DI_zincirinde_calistirma_maliyeti_yayilir()
    {
        using var collector = new MetricCollector(AgentPrismDiagnostics.MeterName);

        var provider = new PricedEchoModelProvider();

        await using var host = await AgentPrismTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = "priced",
                DisplayName = "Fiyatli Agent",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = "priced-fake", Model = "priced-model" },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/priced/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // SSE akisi tumuyle tuketilmeden CompleteAsync (ve dolayisiyla
        // RecordCost) calismis olmayabilir.
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var measurement = collector.DoubleMeasurements(AgentPrismDiagnostics.RunCostCounterName).ShouldHaveSingleItem();

        // 2$/M girdi * 1.000.000 + 4$/M cikti * 500.000 = 4.0
        measurement.Value.ShouldBe(4.0);
        measurement.Tags[AgentPrismDiagnostics.Tags.AgentName].ShouldBe("priced");
        measurement.Tags[AgentPrismDiagnostics.Tags.ModelId].ShouldBe("priced-model");
    }

    /// <summary>Sabit fiyatli, aga cikmayan sahte saglayici.</summary>
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
                // Sahte istemcinin serbest birakilacak kaynagi yok.
            }
        }
    }

    /// <summary>Belirli bir <c>Meter</c>'in olcumlerini toplayan basit dinleyici.</summary>
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
