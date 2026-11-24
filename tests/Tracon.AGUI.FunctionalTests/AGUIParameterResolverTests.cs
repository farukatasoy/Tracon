using System.Net;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The host's own code decides the parameter values of an AG-UI run (<see cref="TraconAGUIOptions.ResolveParameters"/>), and a
/// tool of the run reads them with their source.
/// </summary>
/// <remarks>
/// <para>
/// A value that came from a front end is text the user chose; a value the host's code produced on the server is the host's.
/// The two claims here are that only what the resolver returns reaches the run, and that the tool is told which kind it holds.
/// Both cross the HTTP boundary, the run recorder and a tool call, so they are functional tests.
/// </para>
/// </remarks>
public sealed class AGUIParameterResolverTests
{
    private const string Agent = "chat-agent";
    private const string OtherAgent = "plain-agent";
    private const string ModelName = "resolver-model";
    private const string Provider = "resolver-provider";
    private const string Probe = "probe_context";

    // Text only the front end knows: nothing Tracon writes about a failure may contain it.
    private const string Canary = "CANARY-FORWARDED-4410-zq";

    [Fact]
    public async Task Only_what_the_resolver_returns_reaches_the_run_and_the_tool_reads_it_as_the_hosts()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, static (_, _) => Resolved(("customer", "Acme-from-host")));

        var response = await host.RunAsync(Agent, Forwarding("t-host", """{"parameters":{"customer":"Wrong-from-browser","tone":"loud"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();

        // The browser's tone is not the run's tone: the resolver did not return it, so the schema's default stands.
        probe.Parameters.ShouldNotBeNull().ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme-from-host", ["tone"] = "plain" },
            ignoreOrder: true);
        probe.Source.ShouldBe(ParameterValueSource.Host);

        var recorded = await host.Services.GetRequiredService<IRunInputStore>()
            .GetAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, response.TraconRunId, TestContext.Current.CancellationToken);

        recorded.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme-from-host");
    }

    [Fact]
    public async Task The_resolver_is_given_the_agent_the_thread_the_caller_and_the_untrusted_values()
    {
        AGUIParameterRequest? seen = null;
        var probe = new ToolProbe();
        await using var host = await StartAsync(
            probe,
            (request, _) =>
            {
                seen = request;

                return Resolved(("customer", "Acme"));
            });

        var response = await host.RunAsync(Agent, Forwarding("t-input", """{"parameters":{"customer":"From-browser"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        var request = seen.ShouldNotBeNull();

        request.AgentName.ShouldBe(Agent);
        request.ThreadId.ShouldBe("t-input");
        request.User.ShouldNotBeNull();
        request.ForwardedParameters.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "From-browser" });
    }

    [Fact]
    public async Task A_resolver_that_returns_nothing_runs_the_agent_on_its_defaults_and_no_front_end_value_comes_in_by_itself()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(
            probe,
            static (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(null),
            definition: Definition(customerRequired: false));

        var response = await host.RunAsync(Agent, Forwarding("t-none", """{"parameters":{"customer":"From-browser"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        probe.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "friend");
        probe.Source.ShouldBe(ParameterValueSource.Host);
    }

    [Fact]
    public async Task A_required_value_the_resolver_does_not_supply_is_a_400_even_when_the_front_end_sent_it()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, static (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(null));

        var response = await host.RunAsync(Agent, Forwarding("t-required", """{"parameters":{"customer":"From-browser"}}"""));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("missingParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["customer"]);
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_resolver_tells_agents_apart_by_name()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(
            probe,
            static (request, _) => string.Equals(request.AgentName, Agent, StringComparison.Ordinal)
                ? Resolved(("customer", "Acme"))
                : ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(null),
            configureTracon: static builder => builder.AddAgent(new AgentDefinition
            {
                Name = OtherAgent,
                Instructions = "Plain.",
                Model = new ModelBinding { Provider = Provider, Model = ModelName },
                Origin = AgentDefinitionOrigin.Code,
            }));

        var accepted = await host.RunAsync(Agent, Forwarding("t-a", """{"parameters":{"customer":"x"}}"""));
        var other = await host.RunAsync(OtherAgent, Forwarding("t-b", """{"parameters":{"customer":"x"}}"""));

        accepted.Status.ShouldBe(HttpStatusCode.OK, accepted.Body);
        other.Status.ShouldBe(HttpStatusCode.OK, other.Body);
    }

    [Fact]
    public async Task A_value_the_resolver_returns_for_a_name_the_schema_does_not_declare_is_a_400()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, static (_, _) => Resolved(("customer", "Acme"), ("custmer", "typo")));

        var response = await host.RunAsync(Agent, AGUIInputs.User("t-undeclared", "hi"));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("unknownParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["custmer"]);
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_resolver_that_throws_ends_the_request_with_500_and_no_run_starts()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(
            probe,
            static (_, _) => throw new InvalidOperationException("The lookup behind the resolver is down."));

        var response = await host.RunAsync(Agent, Forwarding("t-throws", JsonSerializer.Serialize(new { parameters = new { customer = Canary } })));

        response.Status.ShouldBe(HttpStatusCode.InternalServerError, response.Body);
        response.Problem.GetProperty("errorType").GetString().ShouldBe("parameter_resolution_failed");

        // Neither the front end's text nor the exception's reaches the answer; the log has the exception.
        response.Body.ShouldNotContain(Canary, Case.Sensitive);
        response.Body.ShouldNotContain("lookup behind the resolver", Case.Sensitive);

        string.Join('\n', host.Logs.Lines).ShouldNotContain(Canary, Case.Sensitive);
        string.Join('\n', host.Logs.Lines).ShouldContain("parameter resolver failed", Case.Insensitive);

        probe.Calls.ShouldBe(0);
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_resolver_that_returns_a_null_value_fails_closed_like_one_that_throws()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(
            probe,
            static (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = null! }));

        var response = await host.RunAsync(Agent, AGUIInputs.User("t-null", "hi"));

        response.Status.ShouldBe(HttpStatusCode.InternalServerError, response.Body);
        response.Problem.GetProperty("errorType").GetString().ShouldBe("parameter_resolution_failed");
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_dictionary_the_resolver_changes_while_it_is_read_fails_closed_like_a_throw()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, static (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(new BrokenDictionary()));

        var response = await host.RunAsync(Agent, Forwarding("t-broken", """{"parameters":{"customer":"x"}}"""));

        response.Status.ShouldBe(HttpStatusCode.InternalServerError, response.Body);
        response.Problem.GetProperty("errorType").GetString().ShouldBe("parameter_resolution_failed");
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_agent_nothing_carries_is_a_404_and_the_resolver_is_never_asked_about_it()
    {
        var asked = 0;
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, (_, _) =>
        {
            Interlocked.Increment(ref asked);

            throw new InvalidOperationException("The resolver knows no such agent.");
        });

        var response = await host.RunAsync("no-such-agent", AGUIInputs.User("t-unknown-agent", "hi"));

        response.Status.ShouldBe(HttpStatusCode.NotFound, response.Body);
        asked.ShouldBe(0);
    }

    [Fact]
    public async Task The_resolver_is_asked_once_per_request_with_the_requests_own_token()
    {
        var asked = 0;
        var tokenCanBeCanceled = false;
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, (_, token) =>
        {
            Interlocked.Increment(ref asked);
            tokenCanBeCanceled = token.CanBeCanceled;

            return Resolved(("customer", "Acme"));
        });

        var response = await host.RunAsync(Agent, AGUIInputs.User("t-once", "hi"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        asked.ShouldBe(1, "one request asks once, however many model turns it takes");
        tokenCanBeCanceled.ShouldBeTrue("the token is the request's, which a disconnecting client cancels");
    }

    [Fact]
    public async Task A_value_the_resolver_returns_over_the_maximum_length_is_a_400_that_names_it()
    {
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, static (_, _) => Resolved(("customer", new string('a', 4097))));

        var response = await host.RunAsync(Agent, AGUIInputs.User("t-long", "hi"));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Problem.GetProperty("tooLongParameters").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["customer"]);
        probe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_resolver_reads_the_tenant_of_the_request_from_the_tenant_context_itself()
    {
        IServiceProvider? services = null;
        string? tenantSeen = null;
        var probe = new ToolProbe();
        await using var host = await StartAsync(
            probe,
            (_, _) =>
            {
                tenantSeen = services!.GetRequiredService<ITenantContext>().TenantId;

                return Resolved(("customer", "Acme"));
            },
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        services = host.Services;

        var response = await host.RunAsync(
            Agent,
            AGUIInputs.User("t-tenant", "hi"),
            configure: static request => request.Headers.Add("X-Tracon-Tenant", "acme"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        tenantSeen.ShouldBe("acme", "the resolver is not handed the tenant; it reads the one of the request");
    }

    [Fact]
    public async Task A_front_end_value_of_the_wrong_shape_is_a_400_before_the_resolver_is_asked()
    {
        var asked = 0;
        var probe = new ToolProbe();
        await using var host = await StartAsync(probe, (_, _) =>
        {
            Interlocked.Increment(ref asked);

            return Resolved(("customer", "Acme"));
        });

        var response = await host.RunAsync(Agent, Forwarding("t-shape", """{"parameters":{"customer":42}}"""));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        asked.ShouldBe(0);
    }

    [Fact]
    public async Task A_request_a_gate_turns_away_never_reaches_the_resolver()
    {
        var asked = 0;
        var probe = new ToolProbe();
        await using var host = await AGUITestHost.StartAsync(
            builder => Configure(builder, probe, Definition()),
            configureServices: static services => services.AddSingleton<IRunAuthorizationHandler>(new DenyAllHandler()),
            configureAGUI: options => options.ResolveParameters = (_, _) =>
            {
                Interlocked.Increment(ref asked);

                return Resolved(("customer", "Acme"));
            });

        var response = await host.RunAsync(Agent, Forwarding("t-denied", """{"parameters":{"customer":"x"}}"""));

        response.Status.ShouldBe(HttpStatusCode.Forbidden, response.Body);
        asked.ShouldBe(0, "host code is not run for a caller the run gate refused");
    }

    [Fact]
    public async Task Without_a_resolver_a_value_the_front_end_forwards_is_read_as_the_clients()
    {
        var probe = new ToolProbe();
        await using var host = await AGUITestHost.StartAsync(
            builder => Configure(builder, probe, Definition()),
            configureAGUI: static options => options.AcceptForwardedParameters = true);

        var response = await host.RunAsync(Agent, Forwarding("t-client", """{"parameters":{"customer":"From-browser"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        probe.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "From-browser");
        probe.Source.ShouldBe(ParameterValueSource.Client);
    }

    [Fact]
    public async Task A_resolver_replaces_the_flag_when_both_are_set()
    {
        var probe = new ToolProbe();
        await using var host = await AGUITestHost.StartAsync(
            builder => Configure(builder, probe, Definition()),
            configureAGUI: static options =>
            {
                options.AcceptForwardedParameters = true;
                options.ResolveParameters = static (_, _) => Resolved(("customer", "Acme-from-host"));
            });

        var response = await host.RunAsync(Agent, Forwarding("t-both", """{"parameters":{"customer":"From-browser"}}"""));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        probe.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme-from-host");
        probe.Source.ShouldBe(ParameterValueSource.Host);
    }

    private static ValueTask<IReadOnlyDictionary<string, string>?> Resolved(params (string Name, string Value)[] values)
        => ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal));

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
            ToolNames = [Probe],
            Model = new ModelBinding { Provider = Provider, Model = ModelName },
            Origin = AgentDefinitionOrigin.Code,
        };

    private static void Configure(ITraconBuilder builder, ToolProbe probe, AgentDefinition definition)
        => builder
            .AddModelProvider(new FakeModelProvider(Provider)
                .WithModel(new ModelDescriptor { Name = ModelName })
                .CallsTool(Probe, new { })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string>)(() =>
                {
                    probe.Read();

                    return "read";
                }),
                name: Probe,
                description: "Reads the run's parameters the way a tool of the host would.")
            .AddAgent(definition);

    private static Task<AGUITestHost> StartAsync(
        ToolProbe probe,
        Func<AGUIParameterRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, string>?>> resolver,
        AgentDefinition? definition = null,
        Action<ITraconBuilder>? configureTracon = null)
        => AGUITestHost.StartAsync(
            builder =>
            {
                Configure(builder, probe, definition ?? Definition());
                configureTracon?.Invoke(builder);
            },
            configureAGUI: options => options.ResolveParameters = resolver);

    private static RunAgentInput Forwarding(string thread, string forwardedProps)
    {
        var input = AGUIInputs.User(thread, "hi");

        input.ForwardedProperties = JsonDocument.Parse(forwardedProps).RootElement.Clone();

        return input;
    }

    /// <summary>What a tool of the run sees in its scope. One per test, so parallel tests do not share it.</summary>
    private sealed class ToolProbe
    {
        public IReadOnlyDictionary<string, string>? Parameters { get; private set; }

        public ParameterValueSource Source { get; private set; }

        public int Calls { get; private set; }

        public void Read()
        {
            var scope = TraconRunContext.Current;

            Parameters = scope?.Parameters;
            Source = scope?.ParameterSource ?? ParameterValueSource.Client;
            Calls++;
        }
    }

    /// <summary>A resolver's dictionary that fails while it is read, as one changed by another thread would.</summary>
    private sealed class BrokenDictionary : IReadOnlyDictionary<string, string>
    {
        public int Count => 1;

        public IEnumerable<string> Keys => throw new InvalidOperationException("The collection was modified.");

        public IEnumerable<string> Values => throw new InvalidOperationException("The collection was modified.");

        public string this[string key] => throw new InvalidOperationException("The collection was modified.");

        public bool ContainsKey(string key) => throw new InvalidOperationException("The collection was modified.");

        public bool TryGetValue(string key, out string value) => throw new InvalidOperationException("The collection was modified.");

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => throw new InvalidOperationException("The collection was modified.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DenyAllHandler : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Deny("not for you"));

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Deny("not for you"));
    }
}
