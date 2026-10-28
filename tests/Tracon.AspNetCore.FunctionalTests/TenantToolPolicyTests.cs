using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant's tool policy decides which tool a run of that tenant may call: a gate in front of
/// <see cref="IToolAuthorizationHandler"/>, read at call time, through a real host and a real run.
/// </summary>
/// <remarks>
/// <para>
/// Each question crosses a boundary a unit test cannot see: the tenant of the run comes from the request, the tool
/// chain is composed from the container, and the policy is read from a store that a second request writes. The rule
/// itself (section 261.1 of the plan) is one function and has its own unit tests; what this class proves is that
/// the real chain asks it, with the real tenant, on every call.
/// </para>
/// <para>
/// Every scenario runs one turn that calls ONE tool and reads the tool's record back. A denial is not an error:
/// the call is recorded as successful with <see cref="ToolInvocationRecord.AuthorizationDenied"/> set, and the
/// body of the tool does not run.
/// </para>
/// </remarks>
public sealed class TenantToolPolicyTests
{
    private const string Provider = "policy-provider";
    private const string Model = "policy-model";
    private const string AgentName = "orders-agent";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string Lookup = "lookup_order";
    private const string Refund = "refund_order";
    private const string Refused = "This tool is not enabled for this tenant.";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    // --- The behavior of a host that never saves a policy ---

    [Fact]
    public async Task A_tenant_without_a_policy_keeps_todays_behavior()
    {
        var bodies = new ToolBodies();
        await using var host = await StartAsync(Script(Refund), bodies);

        var call = await RunAsync(host, tenant: "acme", session: "no-policy");

        bodies.Count(Refund).ShouldBe(1);
        call.AuthorizationDenied.ShouldBeFalse();
        call.Succeeded.ShouldBeTrue();
    }

    // --- The decision table: the name, the permission, and nothing else ---

    [Theory]
    // The name is listed.
    [InlineData(Lookup, new[] { Lookup }, new string[0], true)]
    // The permission is granted.
    [InlineData(Refund, new[] { Lookup }, new[] { "orders.refund" }, true)]
    // The name is listed and the permission is not granted: either is enough.
    [InlineData(Refund, new[] { Refund }, new string[0], true)]
    // A tool with no permission cannot pass on a permission.
    [InlineData(Lookup, new[] { Refund }, new[] { "orders.refund" }, false)]
    // The name is not listed and the permission is not granted.
    [InlineData(Refund, new[] { Lookup }, new[] { "orders.read" }, false)]
    // Two empty lists are a policy that allows nothing.
    [InlineData(Lookup, new string[0], new string[0], false)]
    [InlineData(Refund, new string[0], new string[0], false)]
    public async Task A_call_passes_on_the_name_or_the_permission_and_on_nothing_else(
        string tool,
        string[] allowedTools,
        string[] grantedPermissions,
        bool runs)
    {
        var bodies = new ToolBodies();
        await using var host = await StartAsync(Script(tool), bodies);

        await host.Services.GetRequiredService<ITenantToolPolicyStore>()
            .UpsertAsync("acme", allowedTools, grantedPermissions, TestContext.Current.CancellationToken);

        var call = await RunAsync(host, tenant: "acme", session: "table");

        bodies.Count(tool).ShouldBe(runs ? 1 : 0);
        call.AuthorizationDenied.ShouldBe(!runs);

        if (!runs)
        {
            // The denial is the tool's ordinary result: the model reads it and the run goes on.
            call.Succeeded.ShouldBeTrue();
            call.Result.ShouldNotBeNull().ShouldContain(Refused, Case.Sensitive);
        }
    }

    // --- Two gates ---

    [Fact]
    public async Task Both_gates_must_allow_a_call_the_policy_allows_and_the_handler_refuses_does_not_run()
    {
        var bodies = new ToolBodies();
        var handler = new RecordingHandler { Deny = true };

        await using var host = await StartAsync(
            Script(Refund),
            bodies,
            services => services.AddSingleton<IToolAuthorizationHandler>(handler));

        await host.Services.GetRequiredService<ITenantToolPolicyStore>()
            .UpsertAsync("acme", [Refund], [], TestContext.Current.CancellationToken);

        var call = await RunAsync(host, tenant: "acme", session: "both-gates");

        handler.Calls.ShouldBe(1, "the policy allowed the call, so the handler was asked");
        bodies.Count(Refund).ShouldBe(0);
        call.AuthorizationDenied.ShouldBeTrue();
        call.Result.ShouldNotBeNull().ShouldContain("handler said no", Case.Sensitive);
    }

    [Fact]
    public async Task A_call_the_policy_refuses_is_never_shown_to_the_consumers_handler()
    {
        var bodies = new ToolBodies();
        var handler = new RecordingHandler();

        await using var host = await StartAsync(
            Script(Refund),
            bodies,
            services => services.AddSingleton<IToolAuthorizationHandler>(handler));

        await host.Services.GetRequiredService<ITenantToolPolicyStore>()
            .UpsertAsync("acme", [Lookup], [], TestContext.Current.CancellationToken);

        var call = await RunAsync(host, tenant: "acme", session: "policy-first");

        handler.Calls.ShouldBe(0, "the consumer's code was called for a call that cannot run");
        bodies.Count(Refund).ShouldBe(0);
        call.AuthorizationDenied.ShouldBeTrue();
    }

    // --- Tenants ---

    [Fact]
    public async Task Each_tenant_is_held_to_its_own_policy()
    {
        var bodies = new ToolBodies();
        await using var host = await StartAsync(Script(Refund, runs: 3), bodies);
        var store = host.Services.GetRequiredService<ITenantToolPolicyStore>();

        await store.UpsertAsync("acme", [Refund], [], TestContext.Current.CancellationToken);
        await store.UpsertAsync("globex", [Lookup], [], TestContext.Current.CancellationToken);

        var acme = await RunAsync(host, tenant: "acme", session: "tenants-a");
        var globex = await RunAsync(host, tenant: "globex", session: "tenants-b");
        var initech = await RunAsync(host, tenant: "initech", session: "tenants-c");

        acme.AuthorizationDenied.ShouldBeFalse();
        globex.AuthorizationDenied.ShouldBeTrue("globex's policy does not list the tool");
        initech.AuthorizationDenied.ShouldBeFalse("initech has no policy and the switch is off");
        bodies.Count(Refund).ShouldBe(2, "acme and initech ran the tool; globex did not");
    }

    [Theory]
    [InlineData("Acme", "acme")]
    [InlineData("ACME", "Acme")]
    public async Task A_policy_saved_under_another_letter_case_still_holds_the_tenant(string savedAs, string headerAs)
    {
        // With the switch off a missed row is a tenant with no policy, and that tenant runs everything: a policy that
        // an administrator saved as "Acme" and the runtime looked up as "acme" would silently stop restricting.
        var bodies = new ToolBodies();
        await using var host = await StartAsync(Script(Refund), bodies);

        await host.Services.GetRequiredService<ITenantToolPolicyStore>()
            .UpsertAsync(savedAs, [Lookup], [], TestContext.Current.CancellationToken);

        var call = await RunAsync(host, tenant: headerAs, session: "case");

        call.AuthorizationDenied.ShouldBeTrue();
        bodies.Count(Refund).ShouldBe(0);
    }

    // --- Freshness ---

    [Fact]
    public async Task A_saved_policy_applies_to_the_next_call_of_an_agent_that_is_already_compiled()
    {
        // The agent compiles on the first run and is cached; the gate is not part of what it compiles to, so a
        // policy saved after that must still bind the next call. A policy that waited for a recompile would be a
        // tenant running a tool its administrator had already taken away.
        var bodies = new ToolBodies();
        await using var host = await StartAsync(Script(Refund, runs: 4), bodies);
        var store = host.Services.GetRequiredService<ITenantToolPolicyStore>();

        (await RunAsync(host, tenant: "acme", session: "fresh-1")).AuthorizationDenied.ShouldBeFalse();

        await store.UpsertAsync("acme", [], [], TestContext.Current.CancellationToken);
        (await RunAsync(host, tenant: "acme", session: "fresh-2")).AuthorizationDenied.ShouldBeTrue();

        await store.UpsertAsync("acme", [Refund], [], TestContext.Current.CancellationToken);
        (await RunAsync(host, tenant: "acme", session: "fresh-3")).AuthorizationDenied.ShouldBeFalse();

        await store.DeleteAsync("acme", TestContext.Current.CancellationToken);
        (await RunAsync(host, tenant: "acme", session: "fresh-4")).AuthorizationDenied.ShouldBeFalse();

        bodies.Count(Refund).ShouldBe(3);
    }

    // --- Failure ---

    [Fact]
    public async Task A_store_that_cannot_be_read_denies_the_call_and_logs_an_error()
    {
        var bodies = new ToolBodies();

        await using var host = await StartAsync(
            Script(Refund),
            bodies,
            // Registered before AddTracon(): the consumer's store wins over the built-in one.
            services => services.AddSingleton<ITenantToolPolicyStore, UnreadablePolicyStore>());

        var call = await RunAsync(host, tenant: "acme", session: "store-down");

        // With the switch off, a missing policy means "run everything": a gate that read a failure as "no policy"
        // would open exactly when the store is down.
        bodies.Count(Refund).ShouldBe(0);
        call.AuthorizationDenied.ShouldBeTrue();
        call.Result.ShouldNotBeNull().ShouldContain("The authorization check failed.", Case.Sensitive);

        host.Logs.Entries.ShouldContain(
            entry => entry.StartsWith("Error ", StringComparison.Ordinal)
                     && entry.Contains("tenant tool policy could not be read", StringComparison.Ordinal)
                     && entry.Contains(Refund, StringComparison.Ordinal));
    }

    // --- Hosts and runs ---

    /// <summary>
    /// A model that calls one tool and then answers, for each of <paramref name="runs"/> runs: the fake keeps ONE queue
    /// for the host, so a run after the first needs its own pair of steps.
    /// </summary>
    private static FakeModelProvider Script(string tool, int runs = 1)
        => new FakeModelProvider(Provider)
            .WithModel(new ModelDescriptor { Name = Model })
            .ForModel(Model, cfg =>
            {
                for (var run = 0; run < runs; run++)
                {
                    cfg.CallsTools((tool, null)).RespondsWith("Done");
                }
            });

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        ToolBodies bodies,
        Action<IServiceCollection>? configureServices = null,
        Action<TraconOptions>? configureOptions = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                bodies.Register(builder, Lookup, permission: null);
                bodies.Register(builder, Refund, permission: "orders.refund");
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Work on orders.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    ToolNames = [Lookup, Refund],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                if (configureOptions is not null)
                {
                    services.Configure(configureOptions);
                }

                configureServices?.Invoke(services);
            });

    /// <summary>Runs the agent as a tenant and reads the one tool call the run made.</summary>
    private static async Task<ToolInvocationRecord> RunAsync(TraconTestHost host, string? tenant, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "go", SessionId = session }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var canonical = AmbientTenantScope.Normalize(tenant ?? "default");

        using var scope = AmbientTenantScope.Begin(canonical);
        var runs = host.Services.GetRequiredService<IRunStore>();

        var run = (await runs.QueryRunsAsync(
                new RunQuery { TenantId = canonical, SessionId = session },
                TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem($"runs of {canonical}/{session}");

        return (await runs.ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem($"tool calls of {canonical}/{session}");
    }

    /// <summary>The tool bodies of one test, and how often each ran.</summary>
    private sealed class ToolBodies
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int Count(string tool)
        {
            lock (_counts)
            {
                return _counts.GetValueOrDefault(tool);
            }
        }

        public void Register(ITraconBuilder builder, string tool, string? permission)
            => builder.AddTool(
                (Func<string>)(() =>
                {
                    lock (_counts)
                    {
                        _counts[tool] = _counts.GetValueOrDefault(tool) + 1;
                    }

                    return $"{tool}-done";
                }),
                name: tool,
                description: $"Runs {tool}.",
                configure: options => options.RequiredPermission = permission);
    }

    private sealed class RecordingHandler : IToolAuthorizationHandler
    {
        private int _calls;

        public bool Deny { get; init; }

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(Deny ? ToolAuthorizationResult.Deny("handler said no") : ToolAuthorizationResult.Allow());
        }
    }

    private sealed class UnreadablePolicyStore : ITenantToolPolicyStore
    {
        public ValueTask<TenantToolPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the policy store is down");

        public ValueTask<TenantToolPolicy> UpsertAsync(
            string tenantId, IReadOnlyList<string> allowedTools, IReadOnlyList<string> grantedPermissions, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the policy store is down");

        public ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the policy store is down");
    }
}
