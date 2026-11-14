using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The ring a pipeline shares with the others writes the spans and the metrics that a telemetry client built for that
/// pipeline alone wrote.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario runs the same call twice against the same pipeline: once through the framework's
/// <see cref="OpenTelemetryChatClient"/> built directly over it, which is how a pipeline used to get its ring, and once
/// through the shared ring. The span (name, kind, status, tags, events) and the metrics (instrument, tags, token counts)
/// must be the same. The scenarios are the ones a change of key could lose: the default model of a pipeline when the call
/// names none (the shared ring takes it from the call, not from its key), a model the call names, a provider with no
/// endpoint, a pipeline that says nothing about itself, and options that carry tools, stop sequences and additional
/// properties.
/// </para>
/// <para>
/// The test is also the guard of the framework's version: if the telemetry client starts to read the default model, the
/// provider or the endpoint of the pipeline anywhere else than its constructor and the options of the call, a scenario
/// here fails before a dashboard does. The instruments belong to the framework's client and cannot be bound by instance,
/// so the capture keeps the measurements that carry a marker only this test knows, in the provider, the host or the
/// response model.
/// </para>
/// </remarks>
public sealed class ChatTelemetryEquivalenceTests
{
    private static readonly IReadOnlyList<ChatMessage> Messages = [new ChatMessage(ChatRole.User, "hello")];

    private static readonly Dictionary<string, Scenario> Scenarios = new(StringComparer.Ordinal)
    {
        ["default-model-and-endpoint"] = new(
            static marker => new ChatClientMetadata($"eq-{marker}", new Uri($"https://{marker}.example.test:8443/v1/"), "default-model"),
            static () => null),
        ["model-named-by-the-call"] = new(
            static marker => new ChatClientMetadata($"eq-{marker}", new Uri($"https://{marker}.example.test:8443/v1/"), "default-model"),
            static () => new ChatOptions { ModelId = "option-model", Temperature = 0.5f }),
        ["provider-without-endpoint-or-model"] = new(
            static marker => new ChatClientMetadata($"eq-{marker}", providerUri: null, defaultModelId: null),
            static () => new ChatOptions { MaxOutputTokens = 10 }),
        ["pipeline-that-says-nothing"] = new(static _ => null, static () => null),
        ["endpoint-on-its-default-port"] = new(
            static marker => new ChatClientMetadata($"eq-{marker}", new Uri($"http://{marker}.example.test/"), "default-model"),
            static () => new ChatOptions()),
        ["tools-stop-sequences-and-additional-properties"] = new(
            static marker => new ChatClientMetadata($"eq-{marker}", new Uri($"https://{marker}.example.test/v1/"), "default-model"),
            static () => new ChatOptions
            {
                Tools = [AIFunctionFactory.Create(static () => "x", "probe_tool", "A probe tool.")],
                StopSequences = ["stop"],
                TopP = 0.9f,
                Seed = 7,
                ConversationId = "conversation-1",
                ResponseFormat = ChatResponseFormat.Json,
                AdditionalProperties = new AdditionalPropertiesDictionary { ["key"] = "value" },
            }),
    };

    /// <summary>How the call ends.</summary>
    public enum CallMode
    {
        /// <summary>One response.</summary>
        Response,

        /// <summary>A stream of updates read to the end.</summary>
        Stream,

        /// <summary>The pipeline fails.</summary>
        Error,
    }

    public static TheoryData<string, CallMode> Matrix()
    {
        var data = new TheoryData<string, CallMode>();

        foreach (var scenario in Scenarios.Keys)
        {
            foreach (var mode in Enum.GetValues<CallMode>())
            {
                data.Add(scenario, mode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task The_shared_ring_writes_the_span_and_the_metrics_a_client_of_its_own_wrote(string scenarioName, CallMode mode)
    {
        var scenario = Scenarios[scenarioName];
        var marker = Guid.NewGuid().ToString("N");
        var failure = mode is CallMode.Error ? new InvalidOperationException("model down") : null;
        var pipeline = new ScriptedPipeline("answer", scenario.Metadata(marker), failure: failure, responseModel: marker);

        using var directProbe = new ChatSpanProbe();
        using var sharedProbe = new ChatSpanProbe();
        using var metrics = new MetricCapture(marker);

        await RunAsync(new OpenTelemetryChatClient(pipeline, logger: null, directProbe.Source), scenario.Options(), mode);
        var directMetrics = metrics.Take();

        using var lanes = sharedProbe.NewLanes();
        await RunAsync(lanes.Wrap(pipeline), scenario.Options(), mode);
        var sharedMetrics = metrics.Take();

        var directSpan = Describe(directProbe.Spans().ShouldHaveSingleItem());
        var sharedSpan = Describe(sharedProbe.Spans().ShouldHaveSingleItem());

        sharedSpan.ShouldBe(directSpan);
        sharedMetrics.ShouldBe(directMetrics);

        if (mode is not CallMode.Error || !string.Equals(scenarioName, "pipeline-that-says-nothing", StringComparison.Ordinal))
        {
            directMetrics.ShouldNotBeEmpty("the capture saw nothing, so the comparison above proved nothing");
        }
    }

    [Fact]
    public async Task The_default_model_of_the_pipeline_names_the_span_and_the_call_is_not_changed_by_it()
    {
        using var probe = new ChatSpanProbe();
        using var lanes = probe.NewLanes();
        var pipeline = new ScriptedPipeline("answer", new ChatClientMetadata("provider", new Uri("https://api.example.test/"), "default-model"));
        var client = lanes.Wrap(pipeline);

        await client.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        var span = probe.Spans().ShouldHaveSingleItem();
        span.DisplayName.ShouldBe("chat default-model");
        span.GetTagItem("gen_ai.request.model").ShouldBe("default-model");
        pipeline.LastOptions.ShouldBeNull("the pipeline underneath was not given the model the ring named the span with");
    }

    private static async Task RunAsync(IChatClient client, ChatOptions? options, CallMode mode)
    {
        try
        {
            if (mode is CallMode.Stream)
            {
                await foreach (var unused in client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken))
                {
                }
            }
            else
            {
                await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);
            }
        }
        catch (InvalidOperationException exception) when (mode is CallMode.Error && string.Equals(exception.Message, "model down", StringComparison.Ordinal))
        {
            // The scenario expects the failure; what it wrote on the span is compared.
        }
    }

    /// <summary>A span as text: everything it says about itself except the instants and durations.</summary>
    private static string Describe(Activity span)
    {
        var tags = span.TagObjects
            .Where(static tag => !string.Equals(tag.Key, "gen_ai.response.time_to_first_chunk", StringComparison.Ordinal))
            .OrderBy(static tag => tag.Key, StringComparer.Ordinal)
            .Select(static tag => $"{tag.Key}={tag.Value}");
        var events = span.Events.Select(static @event => @event.Name);

        return $"{span.DisplayName} | {span.Kind} | {span.Status} | {span.StatusDescription}\n"
            + string.Join('\n', tags) + "\n"
            + string.Join('\n', events);
    }

    private sealed record Scenario(Func<string, ChatClientMetadata?> Metadata, Func<ChatOptions?> Options);

    /// <summary>The measurements of the four instruments a chat client makes that carry the marker, as text.</summary>
    private sealed class MetricCapture : IDisposable
    {
        // The instruments belong to the framework's client, which makes a meter of its own, so they are selected by name.
        private readonly MetricCollector _collector = MetricCollector.ForInstruments(
            "gen_ai.client.operation.duration",
            "gen_ai.client.token.usage",
            "gen_ai.client.operation.time_to_first_chunk",
            "gen_ai.client.operation.time_per_output_chunk");

        private readonly string _marker;

        public MetricCapture(string marker) => _marker = marker;

        /// <summary>Returns what was captured since the last call, in a fixed order, and forgets it.</summary>
        public List<string> Take()
            => [.. _collector.Drain()
                .Where(measurement => measurement.Tags.Values.Any(tag => tag?.ToString() is { } text && text.Contains(_marker, StringComparison.Ordinal)))
                .Select(Render)
                .Order(StringComparer.Ordinal)];

        public void Dispose() => _collector.Dispose();

        // A token count is written as it is. A duration is written as a placeholder: it differs from one call to the next, and
        // the comparison is about which instruments carry which tags.
        private static string Render(RecordedMeasurement measurement)
        {
            var tags = measurement.Tags
                .Select(static tag => $"{tag.Key}={tag.Value?.ToString()}")
                .Order(StringComparer.Ordinal);

            var value = measurement.ValueType == typeof(int)
                ? ((int)measurement.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "time";

            return $"{measurement.Instrument} {value} [{string.Join(", ", tags)}]";
        }
    }
}
