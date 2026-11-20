using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant-aware <see cref="IAgentDefinitionSource"/>: one tenant's definitions never reach another tenant, and the
/// compiled agent the catalog caches for a definition is never served across tenants.
/// </summary>
/// <remarks>
/// The source decides which definitions a tenant holds; Tracon adds the ambient tenant to the key of the
/// compiled-agent cache. Two tenants with an agent of the same name and different instructions, run in turn so the
/// cache is warm for the first before the second asks, would see each other's instructions if either half failed.
/// </remarks>
public sealed class DefinitionSourceTenantTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string ProviderName = "tenant-provider";
    private const string ModelName = "tenant-model";

    [Fact]
    public async Task A_tenants_definition_is_not_listed_and_cannot_be_run_by_another_tenant()
    {
        var provider = Provider();
        var (host, source) = await StartAsync(provider);

        await using (host)
        {
            source.Add(Definition("tenant-a-only", "Only tenant A."), "tenant-a");

            var listedForA = await ListAsync(host, "tenant-a");
            var listedForB = await ListAsync(host, "tenant-b");

            listedForA.ShouldContain("tenant-a-only", StringComparer.Ordinal);
            listedForB.ShouldNotContain("tenant-a-only", StringComparer.Ordinal);

            using var refused = await RunAsync(host, "tenant-b", "tenant-a-only");

            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            provider.Requests.ShouldBeEmpty();

            using var allowed = await RunAsync(host, "tenant-a", "tenant-a-only");

            allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Two_tenants_with_one_agent_name_never_share_a_compiled_agent_through_the_cache()
    {
        var provider = Provider();
        var (host, source) = await StartAsync(provider);

        await using (host)
        {
            source.Add(Definition("shared-agent", "Tenant A speaks."), "tenant-a");
            source.Add(Definition("shared-agent", "Tenant B speaks."), "tenant-b");

            // A twice so the cache is warm for A before B asks; then A again after B.
            foreach (var tenant in new[] { "tenant-a", "tenant-a", "tenant-b", "tenant-a" })
            {
                using var response = await RunAsync(host, tenant, "shared-agent");

                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            }

            provider.Requests.Select(static request => request.Options!.Instructions).ShouldBe(
                ["Tenant A speaks.", "Tenant A speaks.", "Tenant B speaks.", "Tenant A speaks."]);
        }
    }

    [Fact]
    public async Task Each_tenants_own_values_bind_into_its_own_schema()
    {
        var provider = Provider();
        var (host, source) = await StartAsync(provider);

        await using (host)
        {
            // Tenant A's agent declares 'topic'; tenant B's declares 'subject'. Each tenant's request is checked
            // against its own definition, not against the other's.
            source.Add(
                Definition("param-agent", "A on {{topic}}.") with { Parameters = [Required("topic")] },
                "tenant-a");
            source.Add(
                Definition("param-agent", "B on {{subject}}.") with { Parameters = [Required("subject")] },
                "tenant-b");

            using var a = await RunAsync(host, "tenant-a", "param-agent", ("topic", "tides"));
            using var b = await RunAsync(host, "tenant-b", "param-agent", ("subject", "moons"));
            using var crossed = await RunAsync(host, "tenant-b", "param-agent", ("topic", "tides"));

            a.StatusCode.ShouldBe(HttpStatusCode.OK, await a.Content.ReadAsStringAsync());
            b.StatusCode.ShouldBe(HttpStatusCode.OK, await b.Content.ReadAsStringAsync());
            crossed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            provider.Requests.Select(static request => request.Options!.Instructions).ShouldBe(["A on tides.", "B on moons."]);
        }
    }

    [Fact]
    public async Task A_tenant_cannot_save_a_definition_under_a_name_the_source_holds()
    {
        // The source wins a name clash with a stored definition, so a row saved under the same name would be kept and
        // never run. The management API says no, and names the source that holds the name.
        var provider = Provider();
        var (host, source) = await StartAsync(provider);

        await using (host)
        {
            source.Add(Definition("host-owned", "From the source."), "tenant-a");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents")
            {
                Content = JsonContent.Create(new AgentDefinitionRequest
                {
                    Name = "host-owned",
                    Instructions = "Mine.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                }),
            };

            request.Headers.Add(TenantHeader, "tenant-a");

            using var response = await host.Client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());

            var problem = await TraconTestHost.ReadJsonAsync(response);

            problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(source.Name);

            using var run = await RunAsync(host, "tenant-a", "host-owned");

            run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
            provider.Requests.Select(static captured => captured.Options!.Instructions).ShouldBe(["From the source."]);
        }
    }

    private static AgentParameter Required(string name) => new() { Name = name, Kind = AgentParameterKind.Text, Required = true };

    private static AgentDefinition Definition(string name, string instructions) => new()
    {
        Name = name,
        Instructions = instructions,
        Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static async Task<(TraconTestHost Host, InMemoryDefinitionSource Source)> StartAsync(FakeModelProvider provider)
    {
        ITenantContext? tenantContext = null;
        var source = new InMemoryDefinitionSource(tenant: () => tenantContext?.TenantId ?? "default");

        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.AddAgentDefinitionSource(source);
            });

        tenantContext = host.Services.GetRequiredService<ITenantContext>();

        return (host, source);
    }

    private static async Task<List<string?>> ListAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return [.. (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().Select(static agent => agent.GetProperty("name").GetString())];
    }

    private static async Task<HttpResponseMessage> RunAsync(
        TraconTestHost host,
        string tenant,
        string agent,
        params (string Name, string Value)[] values)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agent}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                Message = "go",
                Parameters = values.Length == 0
                    ? null
                    : values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal),
            }),
        };

        request.Headers.Add(TenantHeader, tenant);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }
}
