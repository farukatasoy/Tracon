using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 266: with <c>RecordSensitiveData</c> off, no content key reaches the span store, whichever ring wrote it.
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

        // The probe's spans are in the trace, so an empty search proves the filter and not a missing span.
        trace.Spans.ShouldContain(span => span.Name.Contains("chat", StringComparison.Ordinal));

        var stored = JsonSerializer.Serialize(trace);

        foreach (var sentinel in SensitiveContentProbe.All)
        {
            stored.ShouldNotContain(sentinel, Case.Sensitive, $"the store kept '{sentinel}' while RecordSensitiveData was off");
        }
    }

    [Fact]
    public async Task The_cost_attributes_survive_the_filter()
    {
        var trace = await RunAndReadTraceAsync(recordSensitiveData: false);

        // 🚨 A filter on the words "input" and "output" would drop these, and the cost view reads them.
        trace.Spans
            .Where(static span => span.Name.StartsWith("chat", StringComparison.Ordinal))
            .ShouldContain(static span => span.Attributes.ContainsKey("gen_ai.operation.name"));
    }

    [Fact]
    public async Task The_content_is_stored_while_sensitive_data_is_on()
    {
        var trace = await RunAndReadTraceAsync(recordSensitiveData: true);

        var stored = JsonSerializer.Serialize(trace);

        // The control: the same host and the same rings, and the filter lets the content through. Without it the
        // test above could pass because the sentinels never reached the span.
        stored.ShouldContain(SensitiveContentProbe.SystemInstructions);
        stored.ShouldContain(SensitiveContentProbe.ToolArguments);
        stored.ShouldContain(SensitiveContentProbe.ToolResult);
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

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "go" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var runId = JsonDocument.Parse(frames.Single(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();

        using var traceResponse = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative),
            TestContext.Current.CancellationToken);

        traceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await traceResponse.Content.ReadFromJsonAsync<RunTrace>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }
}
