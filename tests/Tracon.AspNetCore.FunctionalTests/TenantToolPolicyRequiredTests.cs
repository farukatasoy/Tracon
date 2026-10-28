using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>Tracon:Tools:RequireTenantPolicy</c> turns "a tenant with no tool policy runs every tool" into "a tenant with
/// no tool policy runs none".
/// </summary>
/// <remarks>
/// <para>
/// The switch is read from <c>IOptions</c> when the host starts, as the production profile sees it; the key's
/// binding from configuration is covered by <c>TraconOptionsBindingCoverageTests</c> through the real container,
/// which is where the egress switch's binding is covered too.
/// </para>
/// <para>
/// A request that carries no tenant header runs as the default tenant. With the switch on the default tenant needs a
/// policy row like any other.
/// </para>
/// </remarks>
public sealed class TenantToolPolicyRequiredTests
{
    private const string Provider = "required-provider";
    private const string Model = "required-model";
    private const string AgentName = "required-agent";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string Tool = "refund_order";
    private const string Setting = "Tracon:Tools:RequireTenantPolicy";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Theory]
    [InlineData(null)]
    [InlineData("acme")]
    public async Task With_the_switch_on_a_tenant_without_a_policy_runs_no_tool_and_the_text_names_the_switch(string? tenant)
    {
        var counter = new ToolCounter();
        await using var host = await StartAsync(counter, requirePolicy: true, runs: 1);

        var call = await RunAsync(host, tenant, "required-1");

        // The default tenant (no header) is a tenant too.
        counter.Count.ShouldBe(0);
        call.AuthorizationDenied.ShouldBeTrue();
        call.Result.ShouldNotBeNull().ShouldContain(Setting, Case.Sensitive);
        call.Result.ShouldNotContain("acme", Case.Insensitive, "the text carries no tenant id");
    }

    [Fact]
    public async Task With_the_switch_off_the_same_tenant_runs_the_tool()
    {
        // The control: without it the test above also passes when the tool can never run.
        var counter = new ToolCounter();
        await using var host = await StartAsync(counter, requirePolicy: false, runs: 1);

        var call = await RunAsync(host, tenant: "acme", "required-off");

        counter.Count.ShouldBe(1);
        call.AuthorizationDenied.ShouldBeFalse();
    }

    [Fact]
    public async Task A_policy_for_one_tenant_does_not_open_the_default_tenant()
    {
        var counter = new ToolCounter();
        await using var host = await StartAsync(counter, requirePolicy: true, runs: 2);

        await host.Services.GetRequiredService<ITenantToolPolicyStore>()
            .UpsertAsync("acme", [Tool], [], TestContext.Current.CancellationToken);

        var acme = await RunAsync(host, tenant: "acme", "required-acme");
        var fallback = await RunAsync(host, tenant: null, "required-default");

        acme.AuthorizationDenied.ShouldBeFalse();
        fallback.AuthorizationDenied.ShouldBeTrue("the default tenant has no row of its own");
        counter.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Saving_a_policy_is_not_blocked_by_the_switch_and_lets_the_tenant_run()
    {
        var counter = new ToolCounter();
        await using var host = await StartAsync(counter, requirePolicy: true, runs: 2);

        (await RunAsync(host, tenant: "acme", "required-before")).AuthorizationDenied.ShouldBeTrue();

        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/tenants/acme/tools", UriKind.Relative),
                   new { allowedTools = new[] { Tool } },
                   TestContext.Current.CancellationToken))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        (await RunAsync(host, tenant: "acme", "required-after")).AuthorizationDenied.ShouldBeFalse();
        counter.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_policy_returns_the_tenant_to_running_no_tool()
    {
        var counter = new ToolCounter();
        await using var host = await StartAsync(counter, requirePolicy: true, runs: 1);
        var store = host.Services.GetRequiredService<ITenantToolPolicyStore>();

        await store.UpsertAsync("acme", [Tool], [], TestContext.Current.CancellationToken);
        await store.DeleteAsync("acme", TestContext.Current.CancellationToken);

        (await RunAsync(host, tenant: "acme", "required-deleted")).AuthorizationDenied.ShouldBeTrue();
        counter.Count.ShouldBe(0);
    }

    [Fact]
    public async Task The_policy_response_reports_the_switch()
    {
        var counter = new ToolCounter();
        await using var on = await StartAsync(counter, requirePolicy: true, runs: 0);
        await using var off = await StartAsync(counter, requirePolicy: false, runs: 0);

        var onBody = await ReadPolicyAsync(on);
        var offBody = await ReadPolicyAsync(off);

        onBody.GetProperty("requirePolicy").GetBoolean().ShouldBeTrue();
        offBody.GetProperty("requirePolicy").GetBoolean().ShouldBeFalse();
    }

    private static async Task<System.Text.Json.JsonElement> ReadPolicyAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/tenants/acme/tools", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static Task<TraconTestHost> StartAsync(ToolCounter counter, bool requirePolicy, int runs)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(new FakeModelProvider(Provider)
                    .WithModel(new ModelDescriptor { Name = Model })
                    .ForModel(Model, cfg =>
                    {
                        for (var run = 0; run < runs; run++)
                        {
                            cfg.CallsTools((Tool, null)).RespondsWith("Done");
                        }
                    }));
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.AddTool(
                    (Func<string>)(() =>
                    {
                        counter.Hit();

                        return "refunded";
                    }),
                    name: Tool,
                    description: "Refunds an order.");
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Work on orders.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    ToolNames = [Tool],
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services => services.Configure<TraconOptions>(
                options => options.Tools.RequireTenantPolicy = requirePolicy));

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

    private sealed class ToolCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hit() => Interlocked.Increment(ref _count);
    }
}
