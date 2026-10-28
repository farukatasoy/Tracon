using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An eval case must put in front of the agent the input the production run
/// had (F-343).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Two halves of one defect. A case's <c>context</c> never reached the agent:
/// the case ran as a bare query and the context went to the evaluator only. And
/// promoting a run read its query from the <c>RunStarted</c> event, which is cut
/// at <c>MaxPayloadLength</c> (8 KB by default), and dropped the documents and
/// the parameter values the run carried - all of which <c>run_inputs</c> holds.
/// </para>
/// <para>
/// Every case goes through the real host: the run, the promotion endpoint, the
/// eval job on the queue worker and the model provider that records what it was
/// sent.
/// </para>
/// </remarks>
public sealed class EvalCaseInputFidelityTests
{
    private const string AgentName = "fidelity-agent";
    private const string ParameterizedAgentName = "billing-agent";
    private const string ModelName = "fidelity-model";
    private const string Suite = "fidelity-suite";

    /// <summary>Longer than the default <c>MaxPayloadLength</c> (8 * 1024).</summary>
    private static readonly string LongQuery = string.Concat(Enumerable.Repeat("Summarize the attached policy for a new hire. ", 230));

    [Fact]
    public async Task A_case_context_reaches_the_model_as_a_document_ahead_of_the_query()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveSuiteAsync(host, AgentName);

        using (var cases = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}/cases",
                   new object[] { new { query = "What is the refund window?", context = "Refunds within 30 days." } }))
        {
            cases.EnsureSuccessStatusCode();
        }

        await RunEvalAsync(host);

        var messages = provider.Requests[^1].Messages;

        messages.Count.ShouldBe(2);
        messages[0].Text.ShouldStartWith("-----BEGIN TRACON DOCUMENT-----");
        messages[0].Text.ShouldContain("Refunds within 30 days.");
        messages[1].Text.ShouldBe("What is the refund window?");
    }

    [Fact]
    public async Task A_long_query_with_documents_is_promoted_whole_with_the_documents_as_context()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        LongQuery.Length.ShouldBeGreaterThan(8 * 1024);

        var runId = await RunAsync(host, AgentName, new AgentRunRequest
        {
            Message = LongQuery,
            Documents =
            [
                new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." },
                new AgentRunDocument { Name = "faq.md", Content = "Shipping takes 2 days." },
            ],
        });

        await SaveSuiteAsync(host, AgentName);

        var promoted = await PromoteAsync(host, runId);

        promoted.GetProperty("query").GetString().ShouldBe(LongQuery);

        var context = promoted.GetProperty("context").GetString();
        context.ShouldNotBeNull();
        context.ShouldContain("Name: policy.md", Case.Sensitive);
        context.ShouldContain("Refunds within 30 days.", Case.Sensitive);
        context.ShouldContain("Name: faq.md", Case.Sensitive);
        context.ShouldContain("Shipping takes 2 days.", Case.Sensitive);
    }

    [Fact]
    public async Task A_promoted_run_reaches_the_model_in_its_eval_with_the_messages_it_had_in_production()
    {
        // The whole point of promotion: the eval repeats the production input.
        // Compared on what the model provider was actually sent, not on what
        // the case row says.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var runId = await RunAsync(host, AgentName, new AgentRunRequest
        {
            Message = LongQuery,
            Documents =
            [
                new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." },
                new AgentRunDocument { Name = "faq.md", Content = "Shipping takes 2 days." },
            ],
        });

        var production = provider.Requests[^1];

        await SaveSuiteAsync(host, AgentName);
        await PromoteAsync(host, runId);
        await RunEvalAsync(host);

        var evaluation = provider.Requests[^1];

        evaluation.Messages.Select(static message => (message.Role, message.Text))
            .ShouldBe(production.Messages.Select(static message => (message.Role, message.Text)));
        evaluation.Options!.Instructions.ShouldBe(production.Options!.Instructions);
    }

    [Fact]
    public async Task A_parameterized_run_is_promoted_with_the_values_it_was_bound_with()
    {
        // A promoted case of a parameterized agent carried no values: its eval
        // failed outright on the missing required value, or - for an optional
        // one - ran on the default instead of what production was told.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var runId = await RunAsync(host, ParameterizedAgentName, new AgentRunRequest
        {
            Message = "hi",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
        });

        await SaveSuiteAsync(host, ParameterizedAgentName);

        var promoted = await PromoteAsync(host, runId);

        promoted.GetProperty("parameters").GetProperty("customer").GetString().ShouldBe("Acme");

        var result = await RunEvalAsync(host);

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.ToString());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
    }

    [Fact]
    public async Task An_eval_case_run_records_the_parameter_values_it_was_bound_with()
    {
        // Every other run path records the values it bound (run_inputs.parameters),
        // so a replay repeats them; the eval case run recorded none, and its
        // replay refused for want of them.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SaveSuiteAsync(host, ParameterizedAgentName);

        using (var cases = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}/cases",
                   new object[] { new { query = "hi", parameters = new { customer = "Acme" } } }))
        {
            cases.EnsureSuccessStatusCode();
        }

        var result = await RunEvalAsync(host);
        var caseRunId = result.GetProperty("runId").GetGuid();

        var input = await host.Services.GetRequiredService<IRunInputStore>().GetAsync("default", caseRunId);

        input.ShouldNotBeNull();
        input.Parameters.ShouldNotBeNull();
        input.Parameters["customer"].ShouldBe("Acme");
    }

    [Fact]
    public async Task Without_a_recorded_input_a_long_query_is_promoted_cut_and_marked()
    {
        // With input recording off the RunStarted event is the only source. Its
        // text is cut at MaxPayloadLength and ends in the writer's marker; the
        // case keeps the marker, so the cut is visible wherever the case is read.
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: false);

        var runId = await RunAsync(host, AgentName, new AgentRunRequest { Message = LongQuery });

        await SaveSuiteAsync(host, AgentName);

        var query = (await PromoteAsync(host, runId)).GetProperty("query").GetString()!;

        query.ShouldBe(string.Concat(LongQuery.AsSpan(0, 8 * 1024), "…[truncated]"));
    }

    [Fact]
    public async Task Without_a_recorded_input_a_run_that_carried_a_document_is_refused()
    {
        // The DocumentAttached event says a document was there; its content was
        // never recorded. A case without it would not repeat the run, so the
        // promotion refuses instead of dropping it silently.
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: false);

        var runId = await RunAsync(host, AgentName, new AgentRunRequest
        {
            Message = "Summarize the attached policy.",
            Documents = [new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." }],
        });

        await SaveSuiteAsync(host, AgentName);

        using var response = await host.Client.PostAsync($"/tracon/api/evals/{Suite}/cases/from-run/{runId}", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain("document", Case.Insensitive);

        using var cases = await host.Client.GetAsync($"/tracon/api/evals/{Suite}/cases");
        (await TraconTestHost.ReadJsonAsync(cases)).GetArrayLength().ShouldBe(0);
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("fidelity-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, bool recordRunInput = true)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                .AddModelProvider(provider)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer from the attached documents.",
                    Model = new ModelBinding { Provider = "fidelity-provider", Model = ModelName },
                    Origin = AgentDefinitionOrigin.Code,
                })
                .AddAgent(new AgentDefinition
                {
                    Name = ParameterizedAgentName,
                    Instructions = "Hello {{customer}}, how can I help?",
                    Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }],
                    Model = new ModelBinding { Provider = "fidelity-provider", Model = ModelName },
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                });
            },
            configureServices: services =>
            {

                services.Configure<TraconOptions>(options => options.RunRecording.RecordRunInput = recordRunInput);
            });

    private static async Task<Guid> RunAsync(TraconTestHost host, string agentName, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agentName}/run")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();
    }

    private static async Task SaveSuiteAsync(TraconTestHost host, string agentName)
    {
        using var response = await host.Client.PutAsJsonAsync(
            $"/tracon/api/evals/{Suite}",
            new EvalSuiteSaveRequest
            {
                AgentName = agentName,
                Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
            });

        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> PromoteAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.PostAsync($"/tracon/api/evals/{Suite}/cases/from-run/{runId}", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await TraconTestHost.ReadJsonAsync(response);
    }

    /// <summary>Runs the suite on the queue worker and returns its single case result.</summary>
    private static async Task<JsonElement> RunEvalAsync(TraconTestHost host)
    {
        using var triggered = await host.Client.PostAsJsonAsync($"/tracon/api/evals/{Suite}/run", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}");

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the eval run to finish");

        return detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();
    }
}
