using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The run parameters of an agent, supplied by the front end in <c>forwardedProps.parameters</c> when the host allows it.
/// </summary>
/// <remarks>
/// <para>
/// A value becomes part of the agent's instructions, so the host opts in, the schema of the agent decides which names exist,
/// and the values are checked, recorded and replayed exactly like the ones of <c>POST /api/agents/{name}/run</c>. These are
/// functional tests because every claim here crosses the HTTP boundary, the turn preparation's check, the run recorder and
/// the replay.
/// </para>
/// </remarks>
public sealed class AGUIParameterTests
{
    private const string Agent = "billing-agent";
    private const string ModelName = "param-model";
    private const string Provider = "param-provider";

    // A value only the caller knows: whatever Tracon writes about a refusal or a failure must not contain it.
    private const string Canary = "CANARY-VALUE-7731-xq";

    // A name the schema does not declare; unlike a value, a refusal is allowed to say it.
    private const string MisspelledName = "custmer-91ab";

    [Fact]
    public async Task A_required_parameter_the_front_end_forwards_fills_the_instructions()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var response = await host.RunAsync(Agent, WithParameters("t-required", "r1", """{"parameters":{"customer":"Acme"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();
        model.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a plain tone.");
    }

    [Fact]
    public async Task Other_properties_of_forwardedProps_are_the_front_ends_own_and_change_nothing()
    {
        // A front end forwards what its own runtime needs (CopilotKit adds its own keys); only `parameters` is read.
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var response = await host.RunAsync(
            Agent,
            WithParameters("t-other", "r1", """{"parameters":{"customer":"Acme"},"copilotkit":{"threadMetadata":{"a":1}},"customer":"Wrong"}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        model.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a plain tone.");
    }

    [Fact]
    public async Task Each_turn_of_one_thread_takes_the_values_it_sends()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var first = await host.RunAsync(Agent, WithParameters("t-turns", "r1", """{"parameters":{"customer":"Acme"}}"""));
        var second = await host.RunAsync(Agent, WithParameters("t-turns", "r2", """{"parameters":{"customer":"Globex","tone":"formal"}}"""));

        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);

        model.Requests.Select(static request => request.Options!.Instructions)
            .ShouldBe(["Hello Acme, answer in a plain tone.", "Hello Globex, answer in a formal tone."]);
    }

    [Fact]
    public async Task A_run_without_the_required_parameter_is_a_400_that_names_it_and_the_model_is_not_called()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var absent = await host.RunAsync(Agent, AGUIInputs.User("t-missing", "hi"));
        var empty = await host.RunAsync(Agent, WithParameters("t-missing", "r2", """{"parameters":{}}"""));
        var onlyOptional = await host.RunAsync(Agent, WithParameters("t-missing", "r3", """{"parameters":{"tone":"formal"}}"""));

        foreach (var response in new[] { absent, empty, onlyOptional })
        {
            response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
            response.Problem.GetProperty("title").GetString().ShouldBe("Invalid run parameters");
            response.Problem.GetProperty("missingParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["customer"]);
        }

        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_the_schema_does_not_declare_is_a_400_and_never_dropped()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var response = await host.RunAsync(Agent, WithParameters("t-unknown", "r1", """{"parameters":{"customer":"Acme","custmer":"typo"}}"""));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("title").GetString().ShouldBe("Invalid run parameters");
        response.Problem.GetProperty("unknownParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["custmer"]);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_value_over_the_maximum_length_in_UTF8_bytes_is_a_400_and_the_maximum_itself_is_not()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);
        const int Maximum = 4096;

        var atLimit = await host.RunAsync(Agent, WithValue("t-length", "r1", new string('a', Maximum)));
        atLimit.Status.ShouldBe(HttpStatusCode.OK, "the limit is the longest value, not the first refused");

        var over = await host.RunAsync(Agent, WithValue("t-length", "r2", new string('a', Maximum + 1)));
        over.Status.ShouldBe(HttpStatusCode.BadRequest, over.Body);
        over.Problem.GetProperty("tooLongParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["customer"]);

        // Bytes, not characters: 2049 two-byte characters are 4098 bytes.
        var multiByte = await host.RunAsync(Agent, WithValue("t-length", "r3", new string('é', (Maximum / 2) + 1)));
        multiByte.Status.ShouldBe(HttpStatusCode.BadRequest, multiByte.Body);

        model.Requests.Count.ShouldBe(1, "only the value at the limit reached the model");
    }

    [Theory]
    [InlineData("""{"parameters":{"customer":42}}""")]
    [InlineData("""{"parameters":{"customer":true}}""")]
    [InlineData("""{"parameters":{"customer":null}}""")]
    [InlineData("""{"parameters":{"customer":{"name":"CANARY-VALUE-7731-xq"}}}""")]
    [InlineData("""{"parameters":{"customer":["CANARY-VALUE-7731-xq"]}}""")]
    [InlineData("""{"parameters":["CANARY-VALUE-7731-xq"]}""")]
    [InlineData("""{"parameters":"CANARY-VALUE-7731-xq"}""")]
    [InlineData("""{"parameters":7}""")]
    public async Task A_parameter_that_is_not_a_string_is_a_400_and_the_refusal_quotes_no_value(string forwardedProps)
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var response = await host.RunAsync(Agent, WithParameters("t-shape", "r1", forwardedProps));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("title").GetString().ShouldBe("Invalid run parameters");
        response.Body.ShouldNotContain(Canary, Case.Sensitive);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_or_empty_parameters_property_supplies_nothing()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true, definition: Definition(customerRequired: false));

        var nulled = await host.RunAsync(Agent, WithParameters("t-null", "r1", """{"parameters":null}"""));
        var empty = await host.RunAsync(Agent, WithParameters("t-empty", "r1", """{"parameters":{}}"""));
        var plain = await host.RunAsync(Agent, WithParameters("t-plain", "r1", "null"));

        foreach (var response in new[] { nulled, empty, plain })
        {
            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        }

        model.Requests.Select(static request => request.Options!.Instructions)
            .ShouldAllBe(static instructions => string.Equals(instructions, "Hello friend, answer in a plain tone.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_values_are_recorded_with_the_run_defaults_included()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true);

        var response = await host.RunAsync(Agent, WithParameters("t-recorded", "r1", """{"parameters":{"customer":"Acme"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        var recorded = await RecordedAsync(host, response.TraconRunId);

        recorded.ShouldNotBeNull();
        recorded.Parameters.ShouldNotBeNull();
        recorded.Parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ShouldBe(["customer=Acme", "tone=plain"]);
    }

    [Fact]
    public async Task A_replay_of_the_run_repeats_the_values_it_was_started_with()
    {
        // A replay without tools needs a stored definition to recompile, so the agent is saved through the management API.
        var model = Model();
        await using var host = await AGUITestHost.StartAsync(
            builder => builder.AddModelProvider(model),
            configureAGUI: static options => options.AcceptForwardedParameters = true);

        using var saved = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new
            {
                name = Agent,
                instructions = "Hello {{customer}}, answer in a {{tone}} tone.",
                parameters = new object[]
                {
                    new { name = "customer", kind = "Text", required = true },
                    new { name = "tone", kind = "Text", defaultValue = "plain" },
                },
                model = new { provider = Provider, model = ModelName },
            },
            TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();

        var response = await host.RunAsync(Agent, WithParameters("t-replay", "r1", """{"parameters":{"customer":"Acme","tone":"formal"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        var calls = model.Requests.Count;

        using var replay = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{response.TraconRunId}/replay", UriKind.Relative),
            new { toolMode = "NoTools" },
            TestContext.Current.CancellationToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        model.Requests.Count.ShouldBe(calls + 1);
        model.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a formal tone.");
    }

    [Fact]
    public async Task The_answer_to_an_approval_carries_the_parameters_like_every_other_request_of_the_thread()
    {
        var executions = new StrongBox<int>();
        var model = new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .CallsTool(TestAgents.CancelOrder, new { orderId = "ORD-7" })
            .EchoesLastToolResult();

        await using var host = await AGUITestHost.StartAsync(
            builder => builder
                .AddModelProvider(model)
                .AddTool(
                    (Func<string, string>)(orderId =>
                    {
                        Interlocked.Increment(ref executions.Value);

                        return $"{orderId} canceled.";
                    }),
                    name: TestAgents.CancelOrder,
                    description: "Cancels an order.",
                    configure: options => options.RequiresApproval = true)
                .AddAgent(Definition() with { ToolNames = [TestAgents.CancelOrder] }),
            configureAGUI: static options => options.AcceptForwardedParameters = true);

        var first = await host.RunAsync(Agent, WithParameters("t-approval", "r1", """{"parameters":{"customer":"Acme"}}""", "cancel the order"));
        var interrupt = first.Interrupts.ShouldHaveSingleItem();

        AGUIResume[] approve = [AGUIInputs.Resolved(interrupt.Id!, new { approved = true })];

        // The answer is a request like any other: the agent's required parameter has to be there again.
        var without = await host.RunAsync(Agent, AGUIInputs.With("t-approval", "r2", [], [], approve));
        without.Status.ShouldBe(HttpStatusCode.BadRequest, without.Body);
        without.Problem.GetProperty("missingParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["customer"]);
        executions.Value.ShouldBe(0, "a refused answer did not run the tool");

        var answer = AGUIInputs.With("t-approval", "r3", [], [], approve);
        answer.ForwardedProperties = JsonDocument.Parse("""{"parameters":{"customer":"Acme"}}""").RootElement.Clone();

        var with = await host.RunAsync(Agent, answer);
        with.Status.ShouldBe(HttpStatusCode.OK, with.Body);
        executions.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Without_the_option_forwardedProps_is_ignored_and_a_required_parameter_cannot_be_supplied()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: false);

        var response = await host.RunAsync(Agent, WithParameters("t-off", "r1", """{"parameters":{"customer":"Acme"}}"""));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("title").GetString().ShouldBe("Invalid run parameters");
        response.Problem.GetProperty("missingParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["customer"]);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_the_option_a_value_for_an_optional_parameter_is_ignored_and_a_wrong_shape_is_not_even_read()
    {
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: false, definition: Definition(customerRequired: false));

        var ignored = await host.RunAsync(Agent, WithParameters("t-off-optional", "r1", """{"parameters":{"customer":"Acme"}}"""));
        var shape = await host.RunAsync(Agent, WithParameters("t-off-shape", "r1", """{"parameters":{"customer":42}}"""));

        ignored.Status.ShouldBe(HttpStatusCode.OK, ignored.Body);
        shape.Status.ShouldBe(HttpStatusCode.OK, "the endpoint does not read forwardedProps when the host did not ask it to");

        model.Requests.Select(static request => request.Options!.Instructions)
            .ShouldAllBe(static instructions => string.Equals(instructions, "Hello friend, answer in a plain tone.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_endpoint_mapped_without_settings_does_not_accept_parameters()
    {
        // The overloads without settings are what existing hosts call; their behavior is the one before the option existed.
        var model = Model();
        await using var host = await AGUITestHost.StartAsync(builder => builder.AddModelProvider(model).AddAgent(Definition()));

        var response = await host.RunAsync(Agent, WithParameters("t-default", "r1", """{"parameters":{"customer":"Acme"}}"""));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_tenant_the_session_and_the_agent_are_never_read_from_forwardedProps()
    {
        var model = Model();
        await using var host = await StartAsync(
            model,
            acceptParameters: true,
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var input = WithParameters(
            "t-owner",
            "r1",
            """
            {
              "parameters": { "customer": "Acme" },
              "tenant": "tenant-b", "tenantId": "tenant-b",
              "sessionId": "hijacked-session", "threadId": "hijacked-thread",
              "agent": "other-agent", "agentName": "other-agent"
            }
            """);

        var response = await host.RunAsync(Agent, input, static request => request.Headers.Add("X-Tracon-Tenant", "tenant-a"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        var run = new Uri($"/tracon/api/runs/{response.TraconRunId}", UriKind.Relative);

        // The run belongs to the tenant the request resolved to, and to the thread and the agent of the request.
        using var own = await GetAsTenantAsync(host, run, "tenant-a");
        own.StatusCode.ShouldBe(HttpStatusCode.OK);

        var record = await own.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        record.GetProperty("agentName").GetString().ShouldBe(Agent);
        record.GetProperty("sessionId").GetString().ShouldBe("t-owner");

        // The tenant the front end named is not where the run went, and the sessions it named do not exist.
        using var named = await GetAsTenantAsync(host, run, "tenant-b");
        named.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var hijacked = await GetAsTenantAsync(host, new Uri("/tracon/api/sessions/hijacked-session", UriKind.Relative), "tenant-a");
        hijacked.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var other = await GetAsTenantAsync(host, new Uri("/tracon/api/sessions/hijacked-thread", UriKind.Relative), "tenant-a");
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var session = await GetAsTenantAsync(host, new Uri("/tracon/api/sessions/t-owner", UriKind.Relative), "tenant-a");
        session.StatusCode.ShouldBe(HttpStatusCode.OK, "the thread the request named is the session");
    }

    [Fact]
    public async Task What_Tracon_writes_about_a_parameter_names_it_and_never_quotes_its_value()
    {
        // The value reaches the model (that is what it is for) and the protected run input, and nothing Tracon writes on the
        // way: not the refusals, not the stream's own failure text, not its log lines, not the events of the run.
        var model = Model();
        await using var host = await StartAsync(model, acceptParameters: true, configureTracon: static builder => builder
            .AddModelProvider(new FailingModelProvider("failing-provider", "provider down"))
            .AddAgent(Definition() with { Name = "failing-billing", Model = new ModelBinding { Provider = "failing-provider", Model = "failing-1" } }));

        var seen = new StringBuilder();

        var accepted = await host.RunAsync(Agent, WithValue("t-canary", "r1", Canary));
        seen.AppendLine(accepted.Body);
        accepted.Status.ShouldBe(HttpStatusCode.OK, accepted.Body);
        model.Requests[^1].Options!.Instructions!.ShouldContain(Canary, Case.Sensitive, "the value is what the instructions are filled with");

        var misspelled = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme", [MisspelledName] = Canary };
        var unknown = await host.RunAsync(
            Agent,
            WithParameters("t-canary", "r2", JsonSerializer.Serialize(new { parameters = misspelled })));
        seen.AppendLine(unknown.Body);
        unknown.Status.ShouldBe(HttpStatusCode.BadRequest);
        unknown.Body.ShouldContain(MisspelledName, Case.Sensitive, "a refusal names the parameter");

        var tooLong = await host.RunAsync(Agent, WithValue("t-canary", "r3", Canary + new string('x', 4100)));
        seen.AppendLine(tooLong.Body);
        tooLong.Status.ShouldBe(HttpStatusCode.BadRequest);
        tooLong.Body.ShouldContain("customer", Case.Sensitive, "a refusal names the parameter");

        var wrongShape = await host.RunAsync(
            Agent,
            WithParameters("t-canary", "r4", JsonSerializer.Serialize(new { parameters = new { customer = new { inner = Canary } } })));
        seen.AppendLine(wrongShape.Body);
        wrongShape.Status.ShouldBe(HttpStatusCode.BadRequest);
        wrongShape.Body.ShouldContain("customer", Case.Sensitive, "a refusal names the parameter");

        // The model fails after the stream began: the front end gets a RUN_ERROR.
        var failed = await host.RunAsync("failing-billing", WithValue("t-canary-failed", "r5", Canary));
        seen.AppendLine(failed.Body);
        failed.Events.OfType<RunErrorEvent>().ShouldHaveSingleItem();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var recorded = await runs.QueryRunsAsync(new RunQuery { Take = 50, OnlyRootRuns = false });

        recorded.Count.ShouldBe(2, "the accepted run and the failed one are recorded; a refusal starts no run");

        foreach (var run in recorded)
        {
            seen.AppendLine(run.Error?.Message);

            await foreach (var runEvent in runs.ReadEventsAsync(run.Id, cancellationToken: TestContext.Current.CancellationToken))
            {
                seen.AppendLine(runEvent.Text);
                seen.AppendLine(runEvent.Payload);
            }
        }

        seen.ToString().ShouldNotContain(Canary, Case.Sensitive);
        string.Join('\n', host.Logs.Lines).ShouldNotContain(Canary, Case.Sensitive);
    }

    private static AgentDefinition Definition(bool customerRequired = true)
        => new()
        {
            Name = Agent,
            Instructions = "Hello {{customer}}, answer in a {{tone}} tone.",
            Parameters =
            [
                customerRequired
                    ? new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }
                    : new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" },
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
            ],
            Model = new ModelBinding { Provider = Provider, Model = ModelName },
            Origin = AgentDefinitionOrigin.Code,
        };

    private static FakeModelProvider Model()
        => new FakeModelProvider(Provider).WithModel(new ModelDescriptor { Name = ModelName }).EchoesUserMessage();

    private static Task<AGUITestHost> StartAsync(
        FakeModelProvider model,
        bool acceptParameters,
        AgentDefinition? definition = null,
        Action<ITraconBuilder>? configureTracon = null)
        => AGUITestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(model).AddAgent(definition ?? Definition());
                configureTracon?.Invoke(builder);
            },
            configureAGUI: options => options.AcceptForwardedParameters = acceptParameters);

    private static RunAgentInput WithParameters(string thread, string run, string forwardedProps, string text = "hi")
    {
        var input = AGUIInputs.User(thread, text, run);

        input.ForwardedProperties = JsonDocument.Parse(forwardedProps).RootElement.Clone();

        return input;
    }

    private static RunAgentInput WithValue(string thread, string run, string customer)
        => WithParameters(thread, run, JsonSerializer.Serialize(new { parameters = new { customer } }));

    private static async Task<HttpResponseMessage> GetAsTenantAsync(AGUITestHost host, Uri uri, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Tracon-Tenant", tenant);

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<RunInputRecord?> RecordedAsync(AGUITestHost host, Guid runId)
        => await host.Services.GetRequiredService<IRunInputStore>()
            .GetAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, runId, TestContext.Current.CancellationToken);
}
