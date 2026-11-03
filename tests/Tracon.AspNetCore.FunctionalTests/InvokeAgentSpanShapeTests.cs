using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The shape of a run's stored trace is a contract of its own: a dashboard that groups by span name, a query on
/// <c>gen_ai.agent.name</c> and the waterfall view all read it. A change to how the telemetry agent is built or
/// shared must leave every span's name, kind, parent, status and attribute keys exactly where they were.
/// </summary>
/// <remarks>
/// <para>
/// Each scenario writes its trace as text and compares it with a file under <c>Golden/</c>. The files were produced by
/// the code before the telemetry agent was shared, and a change that moves a span fails here before it reaches a
/// dashboard. Values are kept only for the attributes that are fixed by the scenario; ids, timings and counters that
/// vary from run to run keep their key and lose their value. Set <c>TRACON_UPDATE_GOLDEN=1</c> to rewrite the files
/// after a change that is meant.
/// </para>
/// <para>
/// The scenarios cover the paths the telemetry agent sits on: a streamed run, a run answered in one response, a run that
/// calls a tool and a run that stops at an approval.
/// </para>
/// </remarks>
public sealed class InvokeAgentSpanShapeTests
{
    private const string ProviderName = "shape-provider";
    private const string ModelName = "shape-model";
    private const string AgentName = "shape-agent";
    private const string UpdateVariable = "TRACON_UPDATE_GOLDEN";

    /// <summary>Attributes whose value the scenario fixes; every other attribute keeps only its key.</summary>
    private static readonly HashSet<string> FixedValues = new(StringComparer.Ordinal)
    {
        "gen_ai.operation.name",
        "gen_ai.agent.name",
        "gen_ai.agent.id",
        "gen_ai.provider.name",
        "gen_ai.tool.name",
    };

    [Fact]
    public async Task A_streamed_run_keeps_the_shape_of_its_spans()
    {
        await using var host = await StartAsync(withTool: false, toolNeedsApproval: false);

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName, "hello");

        Verify("streamed-run", trace);
    }

    [Fact]
    public async Task A_run_answered_in_one_response_keeps_the_shape_of_its_spans()
    {
        await using var host = await StartAsync(withTool: false, toolNeedsApproval: false);

        var trace = await RunInOneResponseAsync(host, sessionId: null);

        Verify("single-response-run", trace);
    }

    [Fact]
    public async Task A_run_that_calls_a_tool_keeps_the_shape_of_its_spans()
    {
        await using var host = await StartAsync(withTool: true, toolNeedsApproval: false);

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName, "look it up");

        Verify("tool-run", trace);
    }

    [Fact]
    public async Task A_run_that_stops_at_an_approval_keeps_the_shape_of_its_spans()
    {
        await using var host = await StartAsync(withTool: true, toolNeedsApproval: true);

        var trace = await RunInOneResponseAsync(host, sessionId: "shape-session");

        Verify("approval-run", trace);
    }

    private static async Task<RunTrace> RunInOneResponseAsync(TraconTestHost host, string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = sessionId }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await TraconTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());

        return await RunTraceReader.ReadAsync(host, body.GetProperty("runId").GetGuid());
    }

    private static Task<TraconTestHost> StartAsync(bool withTool, bool toolNeedsApproval)
    {
        var provider = withTool
            ? new FakeModelProvider(ProviderName).ForModel(ModelName, static cfg => cfg.CallsTool("lookup", new { key = "k1" }).RespondsWith("found it"))
            : new FakeModelProvider(ProviderName).WithModel(new ModelDescriptor { Name = ModelName }).RespondsWith("ok");

        return TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);

                if (withTool)
                {
                    builder.AddTool(
                        (Func<string, string>)(static key => $"value of {key}"),
                        name: "lookup",
                        description: "Looks a value up.",
                        configure: options => options.RequiresApproval = toolNeedsApproval);
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Shape probe.",
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    ToolNames = withTool ? ["lookup"] : [],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: static services => services.Configure<TraconOptions>(static options => options.Observability.SuccessSampleRatio = 1));
    }

    private static void Verify(string scenario, RunTrace trace, [CallerFilePath] string sourceFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(sourceFile)!, "Golden", $"invoke-agent-{scenario}.txt");
        var actual = Render(trace);

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        File.Exists(path).ShouldBeTrue($"no golden file {path}; set {UpdateVariable}=1 to write it");

        actual.ShouldBe(File.ReadAllText(path), $"the span shape of '{scenario}' changed; actual:\n{actual}");
    }

    private static string Render(RunTrace trace)
    {
        var byId = trace.Spans.ToDictionary(static span => span.Id);
        var text = new StringBuilder();

        foreach (var span in trace.Spans.OrderBy(static span => span.StartedAt).ThenBy(static span => span.Name, StringComparer.Ordinal))
        {
            var parent = span.ParentId is { } parentId && byId.TryGetValue(parentId, out var found) ? found.Name : "-";

            text.Append(span.Name).Append(" | kind=").Append(span.Kind).Append(" | parent=").Append(parent)
                .Append(" | status=").Append(span.Status).Append('\n');

            foreach (var (key, value) in span.Attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append("    ").Append(key);

                if (FixedValues.Contains(key))
                {
                    text.Append('=').Append(value);
                }

                text.Append('\n');
            }
        }

        return text.ToString();
    }
}
