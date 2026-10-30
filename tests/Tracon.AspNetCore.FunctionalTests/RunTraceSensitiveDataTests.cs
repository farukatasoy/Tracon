using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// With <c>RecordSensitiveData</c> off, no content key reaches the span store, whichever ring wrote it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The source layer is the consumer's: the rings read <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
/// and Tracon does not set <c>EnableSensitiveData</c>. A consumer who turned the variable on for their own exporter
/// found the system instructions and the tool arguments and result in <c>GET /api/runs/{id}/trace</c> although
/// <c>RecordSensitiveData</c> was off, because the store filter matched the words <c>message</c>, <c>prompt</c>
/// and <c>completion</c> and those three keys contain none of them.
/// </para>
/// <para>
/// The test drives the real rings with <c>EnableSensitiveData = true</c> (see <see cref="SensitiveContentProbe"/>)
/// and reads the stored trace over HTTP. It proves the filter against what the libraries really write, so a key a
/// library upgrade adds fails here and not in a consumer's database.
/// </para>
/// </remarks>
public sealed class RunTraceSensitiveDataTests
{
    private const string ProviderName = "sensitive-data-model";
    private const string AgentName = "probe-agent";

    [Fact]
    public async Task No_content_reaches_the_store_while_sensitive_data_is_off()
    {
        var trace = await RunAndReadTraceAsync(recordSensitiveData: false);

        // 🚨 The probe's own spans must be in the trace, or an empty search below proves nothing: the host writes
        // spans of its own (`chat probe-1`), and a probe whose spans never reached the store would pass the search.
        // The rings the probe builds have no provider metadata, so their span names carry no model.
        trace.Spans.ShouldContain(static span => string.Equals(span.Name, "chat", StringComparison.Ordinal), "the chat ring's span");
        trace.Spans.ShouldContain(static span => string.Equals(span.Name, "embeddings", StringComparison.Ordinal), "the embedding ring's span");
        trace.Spans.Count(static span => string.Equals(span.Name, "generate_content", StringComparison.Ordinal))
            .ShouldBe(3, "the image, text-to-speech and speech-to-text rings' spans");
        trace.Spans.ShouldContain(
            static span => string.Equals(span.Name, "executor.process probe-step", StringComparison.Ordinal),
            "the workflow executor's span");

        var stored = JsonSerializer.Serialize(trace);

        foreach (var sentinel in SensitiveContentProbe.All)
        {
            stored.ShouldNotContain(sentinel, Case.Sensitive, $"the store kept '{sentinel}' while RecordSensitiveData was off");
        }
    }

    [Fact]
    public async Task The_token_counts_survive_the_filter()
    {
        var trace = await RunAndReadTraceAsync(recordSensitiveData: false);

        // 🚨 A filter on the words "input" and "output" would drop these two, and the cost view reads them. The probe's
        // chat ring reports 11 input and 7 output tokens.
        var chat = trace.Spans.First(static span => string.Equals(span.Name, "chat", StringComparison.Ordinal));

        chat.Attributes["gen_ai.usage.input_tokens"].ShouldBe("11");
        chat.Attributes["gen_ai.usage.output_tokens"].ShouldBe("7");
    }

    [Fact]
    public async Task The_content_is_stored_while_sensitive_data_is_on()
    {
        var trace = await RunAndReadTraceAsync(recordSensitiveData: true);

        var stored = JsonSerializer.Serialize(trace);

        // The control: the same host and the same rings, and the filter lets every sentinel the rings really write
        // through. Without it, the empty search above could mean that the sentinels never reached a span.
        foreach (var sentinel in SensitiveContentProbe.Recorded)
        {
            stored.ShouldContain(sentinel, Case.Sensitive, $"'{sentinel}' is missing while RecordSensitiveData is on");
        }
    }

    [Fact]
    public async Task A_custom_additional_property_a_ring_copies_to_a_tag_is_stored_under_the_callers_own_name()
    {
        // The one channel the filter cannot close, pinned so that the claim stays true as written: while its content
        // switch is on, a ring copies the entries of a request's AdditionalProperties to tags under the names the
        // caller chose, and the store cannot know those names. The XML of RecordSensitiveData and the observability
        // guide say so. A store that kept only known keys would flip this test on purpose.
        var trace = await RunAndReadTraceAsync(recordSensitiveData: false);

        trace.Spans
            .Any(static span => span.Attributes.TryGetValue(SensitiveContentProbe.AdditionalPropertyKey, out var value)
                && string.Equals(value, SensitiveContentProbe.AdditionalPropertyValue, StringComparison.Ordinal))
            .ShouldBeTrue("the custom key the caller chose is not one the store can know");
    }

    private static async Task<RunTrace> RunAndReadTraceAsync(bool recordSensitiveData)
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(new FakeModelProvider(ProviderName)
                    .CallsTool("sensitive_probe")
                    .EchoesLastToolResult())
                .AddTool(AIFunctionFactory.Create(
                    static async (CancellationToken cancellationToken) =>
                    {
                        await SensitiveContentProbe.EmitAsync(cancellationToken);

                        return "probe done";
                    },
                    "sensitive_probe",
                    "Writes the sensitive spans."))
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Run the probe.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "probe-1" },
                    ToolNames = ["sensitive_probe"],
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Observability.SuccessSampleRatio = 1;
                options.Observability.RecordSensitiveData = recordSensitiveData;
                options.Observability.MaxSpansPerRun = 1000;
            }));

        return (await RunTraceReader.RunAsync(host, AgentName)).Trace;
    }
}
