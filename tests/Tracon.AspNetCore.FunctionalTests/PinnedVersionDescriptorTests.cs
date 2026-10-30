using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run pinned to an earlier definition version is validated, priced and
/// recorded as THAT version.
/// </summary>
/// <remarks>
/// <para>
/// The catalog compiled the pinned version but described it with the
/// current version's descriptor, and the decorators read the response format,
/// the model and the version off the descriptor. An eval of v1 — plain text —
/// was validated against v2's JSON schema and failed; its case runs were
/// recorded as v2, on v2's model. The eval row named v2's model too.
/// </para>
/// <para>
/// Functional on purpose: the defect lives between the catalog, the
/// decorator pipeline, the eval job and the run store.
/// </para>
/// </remarks>
public sealed class PinnedVersionDescriptorTests
{
    private const string AgentName = "explainer-agent";

    [Fact]
    public async Task An_eval_of_a_pinned_version_validates_and_records_that_version()
    {
        var provider = new FakeModelProvider("pinned")
            .WithModel(new ModelDescriptor { Name = "model-v1" })
            .WithModel(new ModelDescriptor { Name = "model-v2", SupportsStructuredOutput = true })
            .EchoesUserMessage();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.UseScheduling(options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: static services =>
            {
                services.Configure<TraconStructuredResponseOptions>(options => options.Enabled = true);
            });

        // v1 answers in plain text on model-v1; v2 switched to a JSON schema on model-v2.
        await SaveAsync(host, HttpMethod.Post, "/tracon/api/agents", Request(model: "model-v1", format: null));
        await SaveAsync(host, HttpMethod.Put, $"/tracon/api/agents/{AgentName}", Request(
            model: "model-v2",
            format: new AgentResponseFormat
            {
                Kind = AgentResponseFormatKind.JsonSchema,
                Schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
                SchemaName = "answer",
            }));

        using (var suite = await host.Client.PutAsJsonAsync(
                   "/tracon/api/evals/pinned-suite",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = AgentName,
                       Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
                   }))
        {
            suite.EnsureSuccessStatusCode();
        }

        using (var cases = await host.Client.PutAsJsonAsync(
                   "/tracon/api/evals/pinned-suite/cases",
                   new object[] { new { query = "plain answer please" } }))
        {
            cases.EnsureSuccessStatusCode();
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/evals/pinned-suite/run", new { agentVersion = 1 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}");

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the pinned eval run to finish");

        var evalRun = detail.GetProperty("run");
        var result = detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.ToString());
        evalRun.GetProperty("agentVersion").GetInt32().ShouldBe(1);
        evalRun.GetProperty("modelId").GetString().ShouldBe("model-v1");

        var caseRun = await host.Services.GetRequiredService<IRunStore>()
            .GetRunAsync(result.GetProperty("runId").GetGuid());

        caseRun.ShouldNotBeNull();
        caseRun.AgentVersion.ShouldBe(1);
        caseRun.ModelId.ShouldBe("model-v1");
        caseRun.Status.ShouldBe(RunStatus.Completed);
    }

    private static AgentDefinitionRequest Request(string model, AgentResponseFormat? format) => new()
    {
        Name = AgentName,
        Instructions = "Answer the question.",
        Model = new ModelBinding { Provider = "pinned", Model = model, ResponseFormat = format },
    };

    private static async Task SaveAsync(TraconTestHost host, HttpMethod method, string uri, AgentDefinitionRequest body)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await response.Content.ReadAsStringAsync());
    }
}
