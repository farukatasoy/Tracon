using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An installation that takes the tenant from a header and lists no allowed tenants lets the caller choose how
/// many tenants there are; the compiled agent cache holds no more than its limit however many ask.
/// </summary>
/// <remarks>
/// <para>
/// This is the path the limit exists for: HTTP, the tenant header, the tenant context, the catalog and the
/// compiled-agent cache in one run. The cache held one entry for every tenant that ever ran an agent and gave
/// nothing back, at about 9 KiB each in the soak measurement.
/// </para>
/// <para>
/// A run that follows its tenant's entry out of the cache still answers: the agent is compiled again.
/// </para>
/// <para>
/// The culture of a run is free text in the request body too. The cache keys on the instruction text the culture
/// selects, so the strings a caller invents open no slots, and one tenant's culture never shares a slot with
/// another tenant's.
/// </para>
/// </remarks>
public sealed class AgentCacheCardinalityTests
{
    private const string AgentName = "support";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const int Limit = 32;
    private const int Tenants = 300;
    private const int InventedCultures = 5_000;

    [Fact]
    public async Task The_cache_never_holds_more_than_its_limit_however_many_tenants_run_an_agent()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer.",
                    Model = new ModelBinding { Provider = "echo", Model = "echo-model" },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: static services =>
                services.Configure<TraconOptions>(static options => options.AgentCache.MaxEntries = Limit));

        var cache = host.Services.GetRequiredService<CompiledAgentCache>();

        for (var tenant = 0; tenant < Tenants; tenant++)
        {
            await RunAsync(host, $"tenant-{tenant:D4}");

            cache.Count.ShouldBeLessThanOrEqualTo(Limit, $"after tenant {tenant}");
        }

        // The limit was reached: the test would pass for the wrong reason if the cache had held nothing.
        cache.Count.ShouldBeGreaterThan(Limit / 2);

        // A tenant whose entry left the cache runs again and is answered.
        await RunAsync(host, "tenant-0000");
        cache.Count.ShouldBeLessThanOrEqualTo(Limit);
    }

    [Fact]
    public async Task A_run_cannot_open_cache_slots_by_inventing_cultures()
    {
        await using var host = await StartWithCulturesAsync();
        var cache = host.Services.GetRequiredService<CompiledAgentCache>();

        // Five thousand distinct strings: unmatched ones, ones that fall back to a parent, and an ordinary one.
        for (var index = 0; index < InventedCultures; index++)
        {
            var culture = (index % 3) switch
            {
                0 => $"x{index}",
                1 => $"tr-{index}",
                _ => $"EN-{index}-{new string('z', index % 50)}",
            };

            await RunAsync(host, "tenant-a", culture);
        }

        // The default text, "en" and "tr": one slot for each text the definition can give, and nothing for the
        // strings that were made up.
        cache.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Two_tenants_running_the_same_culture_do_not_share_a_cache_slot()
    {
        await using var host = await StartWithCulturesAsync();
        var cache = host.Services.GetRequiredService<CompiledAgentCache>();

        await RunAsync(host, "tenant-a", "tr");
        cache.Count.ShouldBe(1);

        // The same spelling, the same text: another tenant still compiles and holds its own agent.
        await RunAsync(host, "tenant-b", "tr");
        cache.Count.ShouldBe(2);

        // A different spelling of the culture the first tenant ran finds that tenant's slot.
        await RunAsync(host, "tenant-a", "TR-tr");
        cache.Count.ShouldBe(2);
    }

    private static async Task<TraconTestHost> StartWithCulturesAsync()
        => await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer.",
                    InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["en"] = "Answer in English.",
                        ["tr"] = "Turkce yanitla.",
                    },
                    Model = new ModelBinding { Provider = "echo", Model = "echo-model" },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

    private static async Task RunAsync(TraconTestHost host, string tenant, string? culture = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "Hello.", Culture = culture }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            _ = frame;
        }
    }
}
