using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

// Same rationale as ToolGovernanceEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Response caching (Phase 81, F-45): a real IChatClient pipeline built
/// through <c>ModelProviderRegistry</c>, not <see cref="TraconResponseCachingChatClient"/>
/// constructed directly - this also proves the ring's POSITION (81.1): a hit
/// skips the real model call but a cached <c>FunctionCallContent</c> still runs.
/// </summary>
public sealed class ResponseCachePipelineTests
{
    private const string AgentName = "cached-agent";

    private static async Task<HttpResponseMessage> PostQueuedAsync(TraconTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }

    /// <summary>Polls a run until it reaches the expected status. See ApprovalEndpointTests.cs for the 30s rationale.</summary>
    private static async Task<string> WaitForStatusAsync(TraconTestHost host, Guid runId, string expected, string? tenant = null)
    {
        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (tenant is not null)
            {
                request.Headers.Add("X-Tracon-Tenant", tenant);
            }

            using var poll = await host.Client.SendAsync(request);
            status = (await TraconTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                return status!;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            $"Run {runId} did not reach status '{expected}' within 30 seconds; last seen status: '{status}'.");
    }

    [Fact]
    public async Task Second_identical_run_is_a_cache_hit_and_the_tool_still_runs()
    {
        var callCount = 0;

        var provider = new FakeModelProvider("cached-model")
            .CallsTool("get_time")
            .RespondsWith("The time is now.")
            .CallsTool("get_time")
            .RespondsWith("The time is now.");

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                // The returned text is FIXED on purpose: a tool result that
                // changes between calls (a live timestamp, for example) would
                // change the SECOND turn's message list and therefore its
                // cache key too - callCount only counts invocations, it is
                // never part of what the tool returns.
                .AddTool((Func<string>)(() => { callCount++; return "10:00 AM"; }), name: "get_time")
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding
                    {
                        Provider = "cached-model",
                        Model = "cached-1",
                        ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) },
                    },
                    ToolNames = ["get_time"],
                }),
            configureServices: static services =>
            {
                services.AddDistributedMemoryCache();
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var first = await PostQueuedAsync(host, new AgentRunRequest { Message = "what time is it?", SessionId = "s-cache-1" });
        var firstRunId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("runId").GetGuid();
        (await WaitForStatusAsync(host, firstRunId, "Completed")).ShouldBe("Completed");

        var requestsAfterFirstRun = provider.Requests.Count;
        requestsAfterFirstRun.ShouldBe(2); // turn 1 (asks -> tool call), turn 2 (with the tool result -> final text)
        callCount.ShouldBe(1);

        using var second = await PostQueuedAsync(host, new AgentRunRequest { Message = "what time is it?", SessionId = "s-cache-2" });
        var secondRunId = (await TraconTestHost.ReadJsonAsync(second)).GetProperty("runId").GetGuid();
        (await WaitForStatusAsync(host, secondRunId, "Completed")).ShouldBe("Completed");

        // Both turns hit the cache: no NEW real model call.
        provider.Requests.Count.ShouldBe(requestsAfterFirstRun);

        // 🚨 81.1's central claim: a cache hit still lets the loop run the
        // FunctionCallContent it carries. The tool body ran a SECOND time
        // even though the model was never asked again.
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task Second_identical_run_shows_null_usage_through_the_run_record()
    {
        // 🚨 Regression at the EXACT boundary the real defect was found at
        // (audit finding, phase 81 closure): ResponseCacheUsageTests already
        // locks the behavior at the TraconResponseCachingChatClient level,
        // but the bug this guards was only OBSERVED through GET /api/runs/{id}
        // against a real OpenAI call - repo history (phases 6/12/15/16/18/20/21/28)
        // shows this class of defect passes client-level tests and only
        // surfaces at the recorded-run boundary.
        var provider = new FakeModelProvider("cached-model")
            .RespondsWith("4", inputTokens: 100, outputTokens: 20)
            .RespondsWith("4", inputTokens: 100, outputTokens: 20);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding
                    {
                        Provider = "cached-model",
                        Model = "cached-1",
                        ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) },
                    },
                }),
            configureServices: static services =>
            {
                services.AddDistributedMemoryCache();
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var first = await PostQueuedAsync(host, new AgentRunRequest { Message = "what is 2+2?", SessionId = "s-usage-1" });
        var firstRunId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("runId").GetGuid();
        (await WaitForStatusAsync(host, firstRunId, "Completed")).ShouldBe("Completed");

        using var firstRecord = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{firstRunId}", UriKind.Relative));
        var firstUsage = (await TraconTestHost.ReadJsonAsync(firstRecord)).GetProperty("usage");
        firstUsage.ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
        firstUsage.GetProperty("totalTokens").GetInt64().ShouldBe(120);

        using var second = await PostQueuedAsync(host, new AgentRunRequest { Message = "what is 2+2?", SessionId = "s-usage-2" });
        var secondRunId = (await TraconTestHost.ReadJsonAsync(second)).GetProperty("runId").GetGuid();
        (await WaitForStatusAsync(host, secondRunId, "Completed")).ShouldBe("Completed");

        provider.Requests.Count.ShouldBe(1); // the second run never reached the model

        using var secondRecord = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{secondRunId}", UriKind.Relative));
        var secondUsage = (await TraconTestHost.ReadJsonAsync(secondRecord)).GetProperty("usage");

        // K-557: null ("not measured"), not a real number - the hit reported
        // NOTHING, it did not report zero tokens spent.
        secondUsage.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task A_cache_store_failure_does_not_fail_the_run()
    {
        var provider = new FakeModelProvider("cached-model").RespondsWith("answer");

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding
                    {
                        Provider = "cached-model",
                        Model = "cached-1",
                        ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) },
                    },
                }),
            configureServices: static services =>
            {
                // A store that fails every call, registered through DI like a
                // real one - not constructed directly against the client.
                services.AddSingleton<IDistributedCache, ThrowingDistributedCache>();
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "hi", SessionId = "s-store-failure" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // A read failure is a miss, a write failure is dropped: the model call
        // itself still succeeds and the run completes normally.
        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");
    }

    [Fact]
    public async Task Disabled_response_cache_calls_the_model_every_time()
    {
        var provider = new FakeModelProvider("uncached-model")
            .RespondsWith("answer one")
            .RespondsWith("answer two");

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "uncached-model", Model = "uncached-1" },
                }),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var first = await PostQueuedAsync(host, new AgentRunRequest { Message = "hi", SessionId = "s-nocache-1" });
        var firstRunId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("runId").GetGuid();
        (await WaitForStatusAsync(host, firstRunId, "Completed")).ShouldBe("Completed");

        using var second = await PostQueuedAsync(host, new AgentRunRequest { Message = "hi", SessionId = "s-nocache-2" });
        var secondRunId = (await TraconTestHost.ReadJsonAsync(second)).GetProperty("runId").GetGuid();
        (await WaitForStatusAsync(host, secondRunId, "Completed")).ShouldBe("Completed");

        // K1: with ResponseCache left null, today's behavior is unchanged -
        // every identical request still reaches the model.
        provider.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Enabling_response_cache_without_a_registered_store_fails_validation_naming_the_missing_registration()
    {
        // 🚨 Plan deviation (recorded at closure): the plan predicted
        // "compilation_error". AgentDefinitionValidator.CheckModelAsync
        // already calls CreateChatClientAsync directly - the SAME path
        // Unrecognized_provider_setting_rejects_running_with_a_clear_message
        // (MultiProviderTests) exercises - and catches TraconException
        // as "invalid_setting"/"model.providerSettings" BEFORE CheckStructureAsync
        // (the "compilation_error" source) ever runs; HasError(messages)
        // short-circuits it. The new fail-fast check throws the SAME
        // exception type, so it is caught the SAME way - consistent with
        // every other ModelBinding construction failure, not a new code path.

        // No AddDistributedMemoryCache() in configureServices: IDistributedCache is NOT registered.
        await using var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(new FakeModelProvider("cached-model").RespondsWith("hi")));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new
            {
                name = "would-be-cached",
                instructions = "hi",
                model = new
                {
                    provider = "cached-model",
                    model = "cached-1",
                    responseCache = new { enabled = true },
                },
            });

        var body = await TraconTestHost.ReadJsonAsync(response);
        var messages = body.GetProperty("messages").EnumerateArray().ToList();

        var error = messages.ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe("invalid_setting");
        error.GetProperty("path").GetString().ShouldBe("model.providerSettings");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("IDistributedCache");
    }

    [Fact]
    public async Task Response_cache_never_leaks_across_tenants()
    {
        var provider = new FakeModelProvider("cached-model")
            .RespondsWith("tenant answer")
            .RespondsWith("tenant answer");

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });

                builder
                    .AddModelProvider(provider)
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding
                        {
                            Provider = "cached-model",
                            Model = "cached-1",
                            ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) },
                        },
                    });
            },
            configureServices: static services =>
            {
                services.AddDistributedMemoryCache();
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        async Task<Guid> RunAsAsync(string tenant, string sessionId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
            {
                Content = JsonContent.Create(new AgentRunRequest { Message = "hi", SessionId = sessionId }),
            };
            request.Headers.Add("Prefer", "respond-async");
            request.Headers.Add("X-Tracon-Tenant", tenant);

            using var response = await host.Client.SendAsync(request);
            return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();
        }

        var tenantARun1 = await RunAsAsync("tenant-a", "s-tenant-a-1");
        (await WaitForStatusAsync(host, tenantARun1, "Completed", tenant: "tenant-a")).ShouldBe("Completed");
        provider.Requests.Count.ShouldBe(1);

        // Same tenant, same prompt: a hit, no new real call.
        var tenantARun2 = await RunAsAsync("tenant-a", "s-tenant-a-2");
        (await WaitForStatusAsync(host, tenantARun2, "Completed", tenant: "tenant-a")).ShouldBe("Completed");
        provider.Requests.Count.ShouldBe(1);

        // Different tenant, same prompt: MUST NOT reuse tenant-a's entry.
        var tenantBRun1 = await RunAsAsync("tenant-b", "s-tenant-b-1");
        (await WaitForStatusAsync(host, tenantBRun1, "Completed", tenant: "tenant-b")).ShouldBe("Completed");
        provider.Requests.Count.ShouldBe(2);
    }
}
