using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A parameterized agent on the run paths that carry no parameter values.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-330: only <c>POST /api/agents/{name}/run</c> and eval bound values. The
/// queued run, batch, replay, the OpenAI-compatible endpoints, MCP and A2A
/// compiled the definition as written, and the model received the literal
/// <c>{{customer}}</c>; defaults were not applied either. A definition
/// registered in code was worse off on <c>/run</c> itself: the gate read only
/// the store, so its own parameters came back as unknown.
/// </para>
/// <para>
/// Every case goes through the real host: the binding happens in the compiler
/// behind the catalog, and the refusal in each endpoint in front of it.
/// </para>
/// </remarks>
public sealed class AgentParameterPathTests
{
    private const string Required = "billing-agent";
    private const string DefaultsOnly = "greeting-agent";
    private const string ModelName = "param-model";

    [Fact]
    public async Task A_queued_run_of_an_agent_with_a_required_parameter_is_refused_with_the_missing_name()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: false);

        using var response = await PostQueuedAsync(host, Required);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MissingAsync(response)).ShouldContain("customer", StringComparer.Ordinal);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_queued_run_binds_the_default_of_an_optional_parameter()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: true);

        using var accepted = await PostQueuedAsync(host, DefaultsOnly);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await WaitUntil.TrueAsync(() => provider.Requests.Count > 0, "the queued run to reach the model");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello friend, how can I help?");
    }

    [Fact]
    public async Task The_OpenAI_compatible_endpoint_refuses_an_agent_with_a_required_parameter()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: false);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/v1/chat/completions",
            new { model = Required, messages = new[] { new { role = "user", content = "hi" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("customer", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_replay_of_an_agent_with_a_required_parameter_repeats_the_values_its_run_recorded()
    {
        // F-294 reopened K-890's replay refusal: a run records the values it was
        // bound with, so a replay no longer has to refuse for want of them. The
        // refusal that remains - a run that recorded NOTHING - is pinned in
        // RunReplayParameterTests.
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: false);

        using var run = await PostBufferedAsync(
            host,
            Required,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            });

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(run)).GetProperty("runId").GetGuid();
        var calls = provider.Requests.Count;

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{runId}/replay", new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(calls + 1);
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
    }

    [Fact]
    public async Task A_replay_of_an_agent_whose_parameters_all_have_defaults_repeats_the_value_it_was_given()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: false);

        using var run = await PostBufferedAsync(
            host,
            DefaultsOnly,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            });

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(run)).GetProperty("runId").GetGuid();

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{runId}/replay", new { toolMode = "NoTools" });

        // The default ('friend') would have changed the source run's
        // instructions; the recorded value ('Acme') does not.
        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
    }

    [Fact]
    public async Task An_eval_of_a_parameterized_definition_registered_in_code_binds_each_cases_values()
    {
        // The eval job read the definition store only, like the /run gate did:
        // a case's values were dropped for a definition registered in code.
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: true, inCode: true);

        using (var suite = await host.Client.PutAsJsonAsync(
                   "/tracon/api/evals/param-suite",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = Required,
                       Checks = System.Text.Json.JsonDocument.Parse("[{\"kind\":\"nonEmpty\",\"minLength\":1}]").RootElement,
                   }))
        {
            suite.EnsureSuccessStatusCode();
        }

        using (var cases = await host.Client.PutAsJsonAsync(
                   "/tracon/api/evals/param-suite/cases",
                   new object[] { new { query = "hi", parameters = new { customer = "Acme" } } }))
        {
            cases.EnsureSuccessStatusCode();
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/evals/param-suite/run", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}");

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the eval run to finish");

        var result = detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.ToString());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
    }

    [Fact]
    public async Task A_parameterized_definition_registered_in_code_binds_its_values_on_run()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: false, inCode: true);

        using var bound = await PostBufferedAsync(
            host,
            Required,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            });

        bound.StatusCode.ShouldBe(HttpStatusCode.OK, await bound.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");

        using var missing = await PostBufferedAsync(host, Required, new AgentRunRequest { Message = "hi" });

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MissingAsync(missing)).ShouldContain("customer", StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_agent_called_without_values_fails_before_the_model_and_says_why()
    {
        // The paths with no endpoint to refuse in front of them — a workflow
        // step, an agent called as a tool, voice — meet the guard the compiler
        // puts on the agent: the run fails before any provider call.
        var provider = Provider();
        await using var host = await StartAsync(provider, queueWorker: false);

        var agent = await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(Required, culture: null, CancellationToken.None);

        var exception = await Should.ThrowAsync<TraconException>(async () => await agent!.RunAsync("hi"));

        exception.Message.ShouldContain("'customer'", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
    }

    private static AgentDefinition Definition(string name, bool required) => new()
    {
        Name = name,
        Instructions = "Hello {{customer}}, how can I help?",
        Parameters =
        [
            required
                ? new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }
                : new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" },
        ],
        Model = new ModelBinding { Provider = "param-provider", Model = ModelName },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider("param-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static async Task<TraconTestHost> StartAsync(FakeModelProvider provider, bool queueWorker, bool inCode = false)
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);

                if (inCode)
                {
                    builder.AddAgent(Definition(Required, required: true));
                }
            },
            configureServices: services => services.UseScheduling(options =>
            {
                options.RunWorker = queueWorker;
                options.PollInterval = TimeSpan.FromMilliseconds(20);
            }));

        if (!inCode)
        {
            foreach (var definition in new[] { Definition(Required, required: true), Definition(DefaultsOnly, required: false) })
            {
                using var created = await host.Client.PostAsJsonAsync(
                    "/tracon/api/agents",
                    new AgentDefinitionRequest
                    {
                        Name = definition.Name,
                        Instructions = definition.Instructions,
                        Parameters = definition.Parameters,
                        Model = definition.Model,
                    });

                created.EnsureSuccessStatusCode();
            }
        }

        return host;
    }

    private static async Task<HttpResponseMessage> PostQueuedAsync(TraconTestHost host, string agentName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };

        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, string agentName, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agentName}/run")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }

    private static async Task<List<string?>> MissingAsync(HttpResponseMessage response)
        => [.. (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("missingParameters")
            .EnumerateArray()
            .Select(static element => element.GetString())];
}
