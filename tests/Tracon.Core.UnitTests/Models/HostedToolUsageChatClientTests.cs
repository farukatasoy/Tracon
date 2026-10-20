using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// F-603 — the ring that reads the hosted tool calls a model response carries and reports their cost to
/// the run (<see cref="HostedToolUsageChatClient"/>).
/// </summary>
/// <remarks>
/// The ring is the only reader of a hosted call: no function invocation and no tool wrapper ever sees it.
/// Everything below runs the real ring against a scripted client, inside a run scope, and reads what the
/// run would read: its non-token total, the tool measurement the record carries, the ledger of registered
/// names and the budget.
/// </remarks>
public sealed class HostedToolUsageChatClientTests
{
    private const string Provider = "openai-responses";

    private static ChatOptions Options(params AITool[] tools) => new() { Tools = [.. tools] };

    private static StaticOptionsMonitor<TraconOptions> Prices(decimal? webSearch = 0.01m, decimal? codeInterpreter = 0.03m, decimal? fileSearch = 0.0025m)
    {
        var options = new TraconOptions();
        var kinds = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase);

        if (webSearch is not null)
        {
            kinds["WebSearch"] = new HostedToolPriceOverride { PerCall = webSearch };
        }

        if (codeInterpreter is not null)
        {
            kinds["CodeInterpreter"] = new HostedToolPriceOverride { PerCall = codeInterpreter };
        }

        if (fileSearch is not null)
        {
            kinds["FileSearch"] = new HostedToolPriceOverride { PerCall = fileSearch };
        }

        options.Pricing.Currency = "USD";
        options.Pricing.HostedTools[Provider] = kinds;

        return new StaticOptionsMonitor<TraconOptions>(options);
    }

    private static AgentRunScope Scope(AgentRunBudget? budget = null)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            Budget = budget,
            ToolUsage = new ToolUsageAccumulator(),
            HostedCalls = new HostedCallLedger(),
            NonTokenCost = new NonTokenCostAccumulator("USD", budget),
        };

    private static HostedToolUsageChatClient Ring(
        IChatClient inner,
        IOptionsMonitor<TraconOptions>? options = null,
        Microsoft.Extensions.Logging.ILogger? logger = null)
        => new(inner, Provider, options ?? Prices(), logger);

    private static ChatResponse Response(params AIContent[] contents)
        => new(new ChatMessage(ChatRole.Assistant, [.. contents, new TextContent("done")]));

    // What the Responses adapter returns for a file search item (measured): a base call with no raw item, and
    // a base result that carries the provider's item.
    private static AIContent[] FileSearch(string callId)
        => [new ToolCallContent(callId), new ToolResultContent(callId) { RawRepresentation = new FileSearchCallResponseItem() }];

    private static async Task<T> InScopeAsync<T>(AgentRunScope scope, Func<Task<T>> body)
    {
        TraconRunContext.SetCurrent(scope);

        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task A_priced_web_search_call_adds_its_price_to_the_runs_total_and_carries_the_measurement()
    {
        var scope = Scope();
        var tool = RegisteredTool("web", HostedToolKind.WebSearch);
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(tool), TestContext.Current.CancellationToken));

        var total = scope.NonTokenCost!.Seal().ShouldNotBeNull();
        total.Amount.ShouldBe(0.01m);
        total.UnsummedItemCount.ShouldBe(0);
        total.IsEstimated.ShouldBeFalse();

        var usage = scope.ToolUsage!.Take("ws_1").ShouldNotBeNull();
        usage.Unit.ShouldBe(ToolUsageUnits.Calls);
        usage.Quantity.ShouldBe(1m);
        usage.Cost.ShouldBe(0.01m);
        usage.Provider.ShouldBe(Provider);
        usage.IsEstimated.ShouldBeFalse();

        scope.HostedCalls!.TryGetName("ws_1", out var name).ShouldBeTrue();
        name.ShouldBe("web");
    }

    [Fact]
    public async Task A_code_interpreter_call_is_an_estimate_and_a_file_search_call_is_found_by_its_raw_item()
    {
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response([new CodeInterpreterToolCallContent("ci_1"), .. FileSearch("fs_1")])));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        var total = scope.NonTokenCost!.Seal().ShouldNotBeNull();
        total.Amount.ShouldBe(0.0325m);
        total.IsEstimated.ShouldBeTrue("a code interpreter is billed by container, not by call");
    }

    [Fact]
    public async Task A_tool_call_that_is_not_a_hosted_tool_adds_nothing()
    {
        var scope = Scope();
        var other = new ToolCallContent("x_1") { RawRepresentation = new object() };
        using var ring = Ring(new FakeChatClient(_ => Response(other, new McpServerToolCallContent("m_1", "tool", "server"), new TextContent("hi"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldBeNull();
    }

    [Fact]
    public async Task A_base_call_is_a_file_search_only_when_its_result_in_the_same_message_is_the_providers_file_search_item()
    {
        // The adapter also returns a base call for a computer call, with its own raw item; and a lone base call
        // with no result beside it says nothing about what ran.
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response(
            new ToolCallContent("lone_1"),
            new ToolCallContent("computer_1") { RawRepresentation = new object() },
            new ToolResultContent("computer_1") { RawRepresentation = new object() },
            new ToolCallContent("fs_other"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldBeNull();
    }

    [Fact]
    public async Task A_file_search_pair_in_one_streamed_update_is_found()
    {
        // The adapter streams a file search call and its result in ONE update (decompiled and parsed over the wire).
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [.. FileSearch("fs_1")]),
            new ChatResponseUpdate(ChatRole.Assistant, "done"),
        ]));

        TraconRunContext.SetCurrent(scope);

        try
        {
            await foreach (var _ in ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken))
            {
                // Drain.
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.0025m);
    }

    [Fact]
    public async Task The_result_of_a_call_adds_nothing()
    {
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"), new WebSearchToolResultContent("ws_1"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
    }

    [Fact]
    public async Task A_kind_with_no_price_is_listed_and_counted_and_never_priced_at_zero()
    {
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))), Prices(webSearch: null));
        var tool = RegisteredTool("web", HostedToolKind.WebSearch);

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(tool), TestContext.Current.CancellationToken));

        var total = scope.NonTokenCost!.Seal().ShouldNotBeNull();
        total.Amount.ShouldBeNull();
        total.UnsummedItemCount.ShouldBe(1);

        var usage = scope.ToolUsage!.Take("ws_1").ShouldNotBeNull();
        usage.Cost.ShouldBeNull();
        usage.Quantity.ShouldBe(1m);
    }

    [Fact]
    public async Task A_price_missing_is_stated_once_per_ring()
    {
        var logger = new CountingLogger();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent($"ws_{Guid.NewGuid():N}"))), Prices(webSearch: null), logger);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            await InScopeAsync(Scope(), () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));
        }

        logger.Warnings.ShouldBe(1);
    }

    [Fact]
    public async Task A_call_of_a_tool_nobody_registered_counts_toward_the_total_and_has_no_record()
    {
        // The harness adds its own web search: it is billed, and it is not a registered tool's.
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(new HostedWebSearchTool()), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
        scope.HostedCalls!.TryGetName("ws_1", out _).ShouldBeFalse();
        scope.ToolUsage!.Take("ws_1").ShouldBeNull("there is no invocation record to attach it to");
    }

    [Fact]
    public async Task One_call_seen_twice_in_a_response_is_one_call()
    {
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"), new WebSearchToolCallContent("ws_1"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
    }

    [Fact]
    public async Task Two_searches_in_a_response_are_two_calls()
    {
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"), new WebSearchToolCallContent("ws_2"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.02m);
    }

    [Fact]
    public async Task A_streamed_call_is_reported_when_it_arrives_not_when_the_stream_ends()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 100m };
        var scope = Scope(budget);
        var gate = new TaskCompletionSource();
        using var ring = Ring(new GatedStreamClient(new WebSearchToolCallContent("ws_1"), gate.Task));

        TraconRunContext.SetCurrent(scope);

        try
        {
            await using var enumerator = ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(RegisteredTool("web", HostedToolKind.WebSearch)), TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            // The stream is still open: the tree's budget has to hear about the amount now, or it cannot
            // refuse the next turn of a streaming run.
            budget.ConsumedCost.ShouldBe(0.01m);
            scope.ToolUsage!.Take("ws_1").ShouldNotBeNull().Cost.ShouldBe(0.01m);

            gate.SetResult();

            while (await enumerator.MoveNextAsync())
            {
                // Drain.
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task The_amount_reaches_the_budget_so_the_next_turn_can_be_refused()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 0.05m };
        using var ring = Ring(new FakeChatClient(_ => Response(new CodeInterpreterToolCallContent("ci_1"))));

        budget.IsExhausted.ShouldBeFalse();

        await InScopeAsync(Scope(budget), () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));
        await InScopeAsync(Scope(budget), () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        budget.ConsumedCost.ShouldBe(0.06m);
        budget.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public async Task Outside_a_run_nothing_is_reported_and_nothing_throws()
    {
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))));

        TraconRunContext.SetCurrent(null);

        var response = await ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken);

        response.Text.ShouldBe("done");
    }

    [Fact]
    public async Task A_run_whose_scope_keeps_no_accumulators_is_not_disturbed()
    {
        var scope = new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid() };
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))));

        var response = await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        response.Text.ShouldBe("done");
    }

    [Fact]
    public async Task The_price_is_read_when_the_call_arrives_not_when_the_ring_is_built()
    {
        var options = new TraconOptions();
        options.Pricing.HostedTools[Provider] = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["WebSearch"] = new HostedToolPriceOverride { PerCall = 0.01m },
        };

        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent($"ws_{Guid.NewGuid():N}"))), new StaticOptionsMonitor<TraconOptions>(options));
        var first = Scope();
        var second = Scope();

        await InScopeAsync(first, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));
        options.Pricing.HostedTools[Provider]["WebSearch"].PerCall = 0.02m;
        await InScopeAsync(second, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        first.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
        second.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.02m);
    }

    [Fact]
    public async Task A_provider_that_bills_more_searches_than_the_response_shows_adds_the_difference()
    {
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => WithBilledSearches(3, new WebSearchToolCallContent("ws_1"))));

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.03m);
    }

    [Fact]
    public async Task A_streamed_response_that_the_provider_bills_less_for_never_takes_an_amount_back()
    {
        // A streamed response states its count after its calls: the call was reported when it arrived, and a counter
        // never takes an amount back. The difference is logged.
        var scope = Scope();
        var logger = new CountingLogger();
        var usage = new UsageContent(new UsageDetails { AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["WebSearchRequests"] = 0 } });
        using var ring = Ring(
            new FakeChatClient(streamingUpdates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new WebSearchToolCallContent("ws_1")]),
                new ChatResponseUpdate(ChatRole.Assistant, [usage]),
            ]),
            logger: logger);

        TraconRunContext.SetCurrent(scope);

        try
        {
            await foreach (var _ in ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken))
            {
                // Drain.
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
        logger.Warnings.ShouldBe(1, "the difference is logged");
    }

    [Fact]
    public async Task A_provider_count_that_matches_the_response_changes_nothing_and_logs_nothing()
    {
        var scope = Scope();
        var logger = new CountingLogger();
        using var ring = Ring(new FakeChatClient(_ => WithBilledSearches(1, new WebSearchToolCallContent("ws_1"))), logger: logger);

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
        logger.Warnings.ShouldBe(0);
    }

    [Fact]
    public async Task A_streamed_response_reads_the_provider_count_from_its_usage()
    {
        var scope = Scope();
        var usage = new UsageContent(new UsageDetails { AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["WebSearchRequests"] = 2 } });
        using var ring = Ring(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new WebSearchToolCallContent("ws_1")]),
            new ChatResponseUpdate(ChatRole.Assistant, [usage]),
        ]));

        TraconRunContext.SetCurrent(scope);

        try
        {
            await foreach (var _ in ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken))
            {
                // Drain.
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.02m);
    }

    [Fact]
    public async Task A_cache_hit_in_another_run_is_not_billed_because_the_ring_sits_inside_the_cache()
    {
        // The structural claim (F-603): a hit replays a response, and the run that replays it has not
        // searched. Above the cache the second run would be billed for the first run's search.
        var cache = new FakeDistributedCache();
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<TraconOptions>>(Prices());
        var inner = new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1")));
        var registry = new ModelProviderRegistry([new FakeModelProvider(inner, name: Provider)], distributedCache: cache, services: services.BuildServiceProvider());
        var binding = new ModelBinding
        {
            Provider = Provider,
            Model = "fake-model",
            ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(5) },
        };
        var client = registry.CreateChatClient(binding);
        var options = new ChatOptions { ModelId = "fake-model" };
        var first = Scope();
        var second = Scope();

        await InScopeAsync(first, () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], options, TestContext.Current.CancellationToken));
        await InScopeAsync(second, () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], options, TestContext.Current.CancellationToken));

        inner.CallCount.ShouldBe(1, "the second call was a cache hit");
        first.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
        second.NonTokenCost!.Seal().ShouldBeNull();
    }

    [Fact]
    public async Task The_pipeline_the_registry_builds_reports_a_hosted_call_to_the_run()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<TraconOptions>>(Prices());
        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))), name: Provider)],
            services: services.BuildServiceProvider());
        var client = registry.CreateChatClient(new ModelBinding { Provider = Provider, Model = "fake-model" });
        var scope = Scope();

        await InScopeAsync(scope, () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], new ChatOptions(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
    }

    [Fact]
    public async Task A_buffered_response_prices_only_the_searches_the_provider_billed()
    {
        // Two searches in the response and one billed: the second failed. It is listed at no cost, and the provider's count
        // is the bill.
        var scope = Scope();
        using var ring = Ring(new FakeChatClient(_ => WithBilledSearches(1, new WebSearchToolCallContent("ws_1"), new WebSearchToolCallContent("ws_2"))));
        var tool = RegisteredTool("web", HostedToolKind.WebSearch);

        await InScopeAsync(scope, () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(tool), TestContext.Current.CancellationToken));

        scope.ToolUsage!.Take("ws_1").ShouldNotBeNull().Cost.ShouldBe(0.01m);

        var second = scope.ToolUsage.Take("ws_2").ShouldNotBeNull();
        second.Cost.ShouldBe(0m);
        second.Quantity.ShouldBe(1m);

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
    }

    [Fact]
    public async Task A_price_the_options_cannot_read_never_reaches_the_model_call()
    {
        var logger = new CountingLogger();
        using var ring = Ring(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))), new ThrowingOptionsMonitor(), logger);

        var response = await InScopeAsync(Scope(), () => ring.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), TestContext.Current.CancellationToken));

        response.Text.ShouldBe("done");
        logger.Warnings.ShouldBe(1, "the failure is logged and the run goes on");
    }

    [Fact]
    public async Task A_cancelled_stream_keeps_the_calls_it_already_reported()
    {
        // The provider runs the tool inside the model call: cancelling stops the next turn, not a search that has
        // happened and is billed.
        var scope = Scope();
        var gate = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();
        using var ring = Ring(new GatedStreamClient(new WebSearchToolCallContent("ws_1"), gate.Task));

        TraconRunContext.SetCurrent(scope);

        try
        {
            await Should.ThrowAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in ring.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], Options(), cancellation.Token))
                {
                    await cancellation.CancelAsync();
                    gate.SetCanceled(cancellation.Token);
                }
            });
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.01m);
    }

    [Fact]
    public async Task A_fallback_links_call_is_priced_with_the_links_provider_not_the_primarys()
    {
        var options = new TraconOptions();
        options.Pricing.HostedTools["backup"] = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["WebSearch"] = new HostedToolPriceOverride { PerCall = 0.07m },
        };
        options.Pricing.HostedTools["primary"] = new Dictionary<string, HostedToolPriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["WebSearch"] = new HostedToolPriceOverride { PerCall = 0.01m },
        };

        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<TraconOptions>>(new StaticOptionsMonitor<TraconOptions>(options));
        var primary = new FakeModelProvider(
            new FakeChatClient(_ => throw new TraconProviderUnavailableException("circuit open") { ProviderName = "primary" }),
            name: "primary");
        var backup = new FakeModelProvider(new FakeChatClient(_ => Response(new WebSearchToolCallContent("ws_1"))), name: "backup");
        var registry = new ModelProviderRegistry([primary, backup], services: services.BuildServiceProvider());
        var client = registry.CreateChatClient(new ModelBinding
        {
            Provider = "primary",
            Model = "fake-model",
            Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }],
        });
        var scope = Scope();

        await InScopeAsync(scope, () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], new ChatOptions(), TestContext.Current.CancellationToken));

        scope.NonTokenCost!.Seal().ShouldNotBeNull().Amount.ShouldBe(0.07m);
    }

    private static AITool RegisteredTool(string name, HostedToolKind kind)
    {
        var registration = new HostedToolRegistration(name, kind, _ => kind switch
        {
            HostedToolKind.WebSearch => new HostedWebSearchTool(),
            HostedToolKind.CodeInterpreter => new HostedCodeInterpreterTool(),
            _ => new HostedFileSearchTool(),
        }, null);

        return HostedToolRegistry.Create(registration, new HostedToolContext { TenantId = "t", Provider = Provider, Services = new NoServicesProvider() }, "agent");
    }

    private static ChatResponse WithBilledSearches(long billed, params AIContent[] contents)
    {
        var response = Response(contents);
        response.Usage = new UsageDetails { AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["WebSearchRequests"] = billed } };

        return response;
    }

    // The type the Responses adapter keeps as the raw item of a file search call; the ring matches it by name.
    private sealed class FileSearchCallResponseItem;

    private sealed class NoServicesProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class ThrowingOptionsMonitor : IOptionsMonitor<TraconOptions>
    {
        public TraconOptions CurrentValue => throw new InvalidOperationException("The pricing source is not readable.");

        public TraconOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TraconOptions, string?> listener) => null;
    }

    private sealed class CountingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }

    /// <summary>Streams one content, then waits for the test before it ends the stream.</summary>
    private sealed class GatedStreamClient(AIContent content, Task gate) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [content]);

            await gate.ConfigureAwait(false);

            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
