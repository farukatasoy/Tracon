using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
// Same rationale as AgentExtensionRunTests.cs (K-269): the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 233 (F-627): conversation memory through a real host — what a user said in one session
/// reaches the model in another, for that user, that agent and that tenant only, and goes when its
/// session goes.
/// </summary>
/// <remarks>
/// Each question crosses the DI boundary (the vector store is the host's registration), the HTTP boundary
/// (the user and the tenant come from the request), the tenant boundary, the stream (the run's scope is
/// an <c>AsyncLocal</c> the provider reads), the queue (a worker runs the turn) or the store boundary; a
/// provider unit test answers none of them.
/// </remarks>
public sealed class ConversationMemoryTests
{
    private const string Collection = "functional_memory";
    private const string UserHeader = "X-Test-User";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string ProviderName = "memory-model";
    private const string Colour = "My favourite colour is teal.";
    private const string ColourQuestion = "What is my favourite colour?";

    [Fact]
    public async Task What_a_user_said_in_one_session_reaches_the_model_in_another_session_of_the_same_agent()
    {
        var (host, provider, _) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);
            await RunAsync(host, "ada", "session-2", ColourQuestion);

            ModelSaw(provider).ShouldContain("teal");
            ModelSaw(provider).ShouldContain("## Memories");
        }
    }

    [Fact]
    public async Task Another_user_does_not_get_it()
    {
        var (host, provider, _) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);
            await RunAsync(host, "grace", "session-2", ColourQuestion);

            ModelSaw(provider).ShouldNotContain("teal");
        }
    }

    [Fact]
    public async Task Another_agent_does_not_get_it()
    {
        var (host, provider, _) = await StartAsync(extraAgent: "other-agent");

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);
            await RunAsync(host, "ada", "session-2", ColourQuestion, agent: "other-agent");

            ModelSaw(provider).ShouldNotContain("teal");
        }
    }

    [Fact]
    public async Task Another_tenant_does_not_get_it_even_for_the_same_user_and_agent()
    {
        var (host, provider, _) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour, tenant: "tenant-a");
            await RunAsync(host, "ada", "session-2", ColourQuestion, tenant: "tenant-b");

            ModelSaw(provider).ShouldNotContain("teal");

            await RunAsync(host, "ada", "session-3", ColourQuestion, tenant: "tenant-a");

            ModelSaw(provider).ShouldContain("teal");
        }
    }

    [Fact]
    public async Task A_run_with_no_user_reads_and_writes_nothing()
    {
        var (host, provider, store) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, user: null, "session-1", Colour);
            await RunAsync(host, user: null, "session-2", ColourQuestion);

            store.Records(Collection).ShouldBeEmpty();
            ModelSaw(provider).ShouldNotContain("teal");

            // The same text from a known user is stored, so the empty store above is the rule and not an accident.
            await RunAsync(host, "ada", "session-3", Colour);
            store.Records(Collection).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task A_run_that_names_no_session_is_not_remembered()
    {
        // Measured (phase 233): a request that names no session opens none, and a record without a session
        // could not be erased with its session. The rule is "no session, no memory".
        var (host, _, store) = await StartAsync();

        await using (host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/memory-agent/run", UriKind.Relative))
            {
                Content = JsonContent.Create(new AgentRunRequest { Message = Colour }),
            };
            request.Headers.Add(UserHeader, "ada");

            using var response = await host.Client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            await response.Content.ReadAsStringAsync();

            store.Records(Collection).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Two_users_that_name_the_same_session_id_each_keep_their_own_memory()
    {
        // Session ownership is off by default, so nothing stops grace from naming ada's session.
        var (host, provider, store) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "shared-session", Colour);
            await RunAsync(host, "grace", "shared-session", "My favourite colour is plum.");

            store.Records(Collection).Select(static record => (string?)record["UserId"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .ShouldBe(["ada", "grace"]);

            await RunAsync(host, "grace", "session-9", ColourQuestion);

            ModelSaw(provider).ShouldContain("plum");
            ModelSaw(provider).ShouldNotContain("teal");
        }
    }

    [Fact]
    public async Task A_queued_run_remembers_for_the_user_who_queued_it()
    {
        // The worker, not the request, runs the turn: the user crosses the job payload.
        var (host, provider, store) = await StartAsync();

        await using (host)
        {
            await QueueAsync(host, "ada", "queued-session", Colour);

            var record = store.Records(Collection).First(static record => string.Equals((string?)record["Role"], "user", StringComparison.OrdinalIgnoreCase));
            record["UserId"].ShouldBe("ada");
            record["SessionId"].ShouldBe("queued-session");
            record["AgentId"].ShouldBe("memory-agent");

            await RunAsync(host, "ada", "session-2", ColourQuestion);

            ModelSaw(provider).ShouldContain("teal");
        }
    }

    [Fact]
    public async Task A_sub_agent_remembers_for_the_user_and_session_of_the_run_that_called_it()
    {
        var provider = new FakeModelProvider("routing")
            .ForModel("router-model", static cfg => cfg
                .CallsTool("background_agents_start_task", new { agentName = "helper", input = "My favourite colour is teal.", description = "remember" })
                .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
                .CallsTool("background_agents_get_task_results", new { taskId = 1 })
                .EchoesLastToolResult("Delegated: "))
            .ForModel("helper-model", static cfg => cfg.RespondsWith("Noted."));
        var store = new InMemoryVectorStore();

        await using var host = await StartHostAsync(
            store,
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(new AgentDefinition
                {
                    Name = "helper",
                    Instructions = "Help.",
                    Model = new ModelBinding { Provider = "routing", Model = "helper-model" },
                    Memory = new MemorySettings { EnableConversationMemory = true },
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.AddAgent(new AgentDefinition
                {
                    Name = "router",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = "routing", Model = "router-model" },
                    CallableAgentNames = ["helper"],
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        await RunAsync(host, "ada", "router-session", "start", agent: "router");

        var records = store.Records(Collection);
        records.ShouldNotBeEmpty("the sub-agent's turn was not remembered: the user did not reach it");
        records.Select(static record => (string?)record["UserId"]).Distinct(StringComparer.Ordinal).ShouldBe(["ada"]);
        records.Select(static record => (string?)record["AgentId"]).Distinct(StringComparer.Ordinal).ShouldBe(["helper"]);
        records.Select(static record => (string?)record["SessionId"]).Distinct(StringComparer.Ordinal).ShouldBe(["router-session"]);

        store.Dispose();
    }

    [Fact]
    public async Task A_workflow_agent_step_remembers_nothing_because_its_session_has_no_record_to_delete_it_with()
    {
        // Measured (phase 233, F-643): the step runs under the workflow's own session id, and no sessions row
        // carries it (F-332), so session delete, the sessions retention target and a session-keyed erasure could
        // never reach what the step stored. The audit found the records written; they are not written now.
        var provider = new FakeModelProvider(ProviderName).ForModel("memory-1", static cfg => cfg.EchoesUserMessage());
        var store = new InMemoryVectorStore();

        await using var host = await StartHostAsync(
            store,
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(MemoryAgent("memory-agent", new MemorySettings { EnableConversationMemory = true }));
                builder.UseWorkflows();
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "after",
                    static _ => (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));
            });

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/memory-flow",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Sequential,
                       Nodes =
                       [
                           new WorkflowNodeReference { Name = "memory-agent", Kind = WorkflowNodeKind.Agent },
                           new WorkflowNodeReference { Name = "after", Kind = WorkflowNodeKind.Function },
                       ],
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/workflows/memory-flow/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "My favourite colour is teal." }),
        };
        request.Headers.Add(UserHeader, "ada");

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _ = await response.Content.ReadAsStringAsync();

        // The step ran and the model saw the message; nothing was remembered.
        provider.Requests.ShouldNotBeEmpty();
        store.Records(Collection).ShouldBeEmpty();

        // The same agent, run directly with a session of its own, still remembers: it is the workflow, not the agent.
        await RunAsync(host, "ada", "direct-session", "My favourite colour is teal.");
        store.Records(Collection).ShouldNotBeEmpty();

        store.Dispose();
    }

    [Fact]
    public async Task An_erasure_of_the_data_subject_previews_and_deletes_the_memory_of_their_sessions_only()
    {
        var (host, _, store) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "ada-session", Colour);
            await RunAsync(host, "grace", "grace-session", "My favourite colour is plum.");
            var before = store.Records(Collection).Count;

            using (var preview = await host.Client.DeleteAsync(new Uri("/tracon/api/data-subjects/ada", UriKind.Relative)))
            {
                var body = await TraconTestHost.ReadJsonAsync(preview);
                body.GetProperty("rowsByTarget").GetProperty("conversation_memory").GetInt32().ShouldBeGreaterThan(0);
                store.Records(Collection).Count.ShouldBe(before);
            }

            using (var erase = await host.Client.DeleteAsync(new Uri("/tracon/api/data-subjects/ada?dryRun=false", UriKind.Relative)))
            {
                erase.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            var left = store.Records(Collection);
            left.ShouldNotBeEmpty();
            left.Select(static record => (string?)record["SessionId"]).Distinct(StringComparer.Ordinal).ShouldBe(["grace-session"]);
        }
    }

    [Fact]
    public async Task An_export_of_the_data_subject_lists_what_the_agent_remembers_of_them()
    {
        var (host, _, _) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "ada-session", Colour);

            using var export = await host.Client.GetAsync(new Uri("/tracon/api/data-subjects/ada/export", UriKind.Relative));

            export.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await export.Content.ReadAsStringAsync();
            body.ShouldContain("conversation_memory");
            body.ShouldContain("teal");
        }
    }

    [Fact]
    public async Task Deleting_a_session_deletes_the_memory_of_that_session_only()
    {
        var (host, _, store) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);
            await RunAsync(host, "ada", "session-2", "I live in Lisbon.");

            using var deleted = await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            store.Records(Collection).Select(static record => (string?)record["SessionId"]).Distinct(StringComparer.Ordinal)
                .ShouldBe(["session-2"]);
        }
    }

    [Fact]
    public async Task A_store_that_cannot_delete_fails_the_session_delete_and_keeps_the_session_so_a_repeat_finishes_it()
    {
        var (host, _, store) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);
            store.FailOn = static operation => string.Equals(operation, "delete", StringComparison.Ordinal) ? new InvalidOperationException("store is down") : null;

            // The host's exception pipeline would answer 500; the test server hands the exception to the caller.
            await Should.ThrowAsync<InvalidOperationException>(
                async () => await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative)));

            using (var stillThere = await host.Client.GetAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative)))
            {
                stillThere.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            store.FailOn = null;

            using var repeated = await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
            repeated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            store.Records(Collection).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task A_vector_store_that_is_down_costs_the_run_its_memory_and_not_its_answer()
    {
        var (host, provider, store) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);
            store.FailOn = static _ => new InvalidOperationException("store is down");

            var stream = await RunAsync(host, "ada", "session-2", ColourQuestion);

            stream.ShouldContain(ColourQuestion);
            ModelSaw(provider).ShouldNotContain("teal");
        }
    }

    [Fact]
    public async Task An_on_demand_agent_offers_the_search_tool_and_the_tool_reads_only_the_callers_memory()
    {
        // The scripted model calls the tool once, so the memory is seeded and one turn runs.
        var provider = new FakeModelProvider(ProviderName)
            .ForModel("memory-1", static cfg => cfg
                .CallsTool("search_conversation_memory", new { userQuestion = "favourite colour" })
                .EchoesLastToolResult("Found: "));
        var store = new InMemoryVectorStore();
        await SeedAsync(store, "tenant-a", "memory-agent", "ada", "ada-session", "My favourite colour is teal.");
        await SeedAsync(store, "tenant-a", "memory-agent", "grace", "grace-session", "My favourite colour is plum.");
        await SeedAsync(store, "tenant-b", "memory-agent", "ada", "ada-session", "My favourite colour is amber.");

        await using var host = await StartHostAsync(
            store,
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(MemoryAgent("memory-agent", new MemorySettings
                {
                    EnableConversationMemory = true,
                    ConversationMemorySearch = ConversationMemorySearch.OnDemand,
                }));
            });

        var stream = await RunAsync(host, "ada", "new-session", ColourQuestion, tenant: "tenant-a");

        provider.Requests[0].Options!.Tools!.Select(static tool => tool.Name).ShouldContain("search_conversation_memory", StringComparer.Ordinal);
        stream.ShouldContain("teal");
        stream.ShouldNotContain("plum");
        stream.ShouldNotContain("amber");

        store.Dispose();
    }

    [Fact]
    public async Task The_before_each_turn_agent_does_not_offer_a_search_tool()
    {
        var (host, provider, _) = await StartAsync();

        await using (host)
        {
            await RunAsync(host, "ada", "session-1", Colour);

            (provider.Requests[^1].Options?.Tools ?? []).Select(static tool => tool.Name).ShouldNotContain("search_conversation_memory", StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task An_agent_that_asks_for_memory_in_a_host_that_never_set_it_up_fails_its_run_naming_the_call_and_the_host_stays_up()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel("memory-1", static cfg => cfg.EchoesUserMessage());

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(MemoryAgent("memory-agent", new MemorySettings { EnableConversationMemory = true }));
                builder.AddAgent(MemoryAgent("plain-agent", memory: null));
            });

        using var failed = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/memory-agent/run", UriKind.Relative), new AgentRunRequest { Message = "hi" });
        var failure = await failed.Content.ReadAsStringAsync();

        failed.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        failure.ShouldContain("UseConversationMemory");

        using var healthy = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/plain-agent/run", UriKind.Relative), new AgentRunRequest { Message = "hi" });

        healthy.StatusCode.ShouldBe(HttpStatusCode.OK);
        await healthy.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task An_agent_that_asks_for_memory_in_a_host_with_no_vector_store_fails_its_run_naming_the_store()
    {
        var provider = new FakeModelProvider(ProviderName).ForModel("memory-1", static cfg => cfg.EchoesUserMessage());

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.UseConversationMemory(static options => options.VectorDimensions = 8);
                builder.AddAgent(MemoryAgent("memory-agent", new MemorySettings { EnableConversationMemory = true }));
            });

        using var failed = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/memory-agent/run", UriKind.Relative), new AgentRunRequest { Message = "hi" });
        var failure = await failed.Content.ReadAsStringAsync();

        failed.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        failure.ShouldContain("VectorStore");
    }

    [Fact]
    public async Task The_validation_endpoint_reports_a_search_limit_outside_its_range_with_the_path_of_the_field()
    {
        var (host, _, _) = await StartAsync();

        await using (host)
        {
            using var response = await host.Client.PostAsJsonAsync(
                new Uri("/tracon/api/agents/validate", UriKind.Relative),
                new AgentDefinitionRequest
                {
                    Name = "limited",
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "memory-1" },
                    Memory = new MemorySettings { EnableConversationMemory = true, ConversationMemoryMaxResults = 99 },
                });

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await TraconTestHost.ReadJsonAsync(response);
            body.GetProperty("valid").GetBoolean().ShouldBeFalse();
            body.GetProperty("messages").EnumerateArray()
                .ShouldContain(static message => string.Equals(message.GetProperty("path").GetString(), "memory.conversationMemoryMaxResults", StringComparison.Ordinal));
        }
    }

    private static async Task SeedAsync(InMemoryVectorStore store, string tenant, string agent, string user, string session, string content)
    {
        using var collection = store.GetDynamicCollection(Collection, ConversationMemorySchema.CreateDefinition(8));
        await collection.EnsureCollectionExistsAsync();
        await collection.UpsertAsync(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Key"] = Guid.NewGuid(),
            ["Role"] = "user",
            ["MessageId"] = null,
            ["AuthorName"] = null,
            ["ApplicationId"] = tenant,
            ["AgentId"] = agent,
            ["UserId"] = user,
            ["SessionId"] = session,
            ["Content"] = content,
            ["CreatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["ContentEmbedding"] = content,
        });
    }

    private static AgentDefinition MemoryAgent(string name, MemorySettings? memory)
        => new()
        {
            Name = name,
            Instructions = "Reply briefly.",
            Model = new ModelBinding { Provider = ProviderName, Model = "memory-1" },
            Memory = memory,
            Origin = AgentDefinitionOrigin.Code,
        };

    private static async Task<(TraconTestHost Host, FakeModelProvider Provider, InMemoryVectorStore Store)> StartAsync(string? extraAgent = null)
    {
        var provider = new FakeModelProvider(ProviderName).ForModel("memory-1", static cfg => cfg.EchoesUserMessage());
        var store = new InMemoryVectorStore();
        var memory = new MemorySettings { EnableConversationMemory = true };

        var host = await StartHostAsync(
            store,
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(MemoryAgent("memory-agent", memory));

                if (extraAgent is not null)
                {
                    builder.AddAgent(MemoryAgent(extraAgent, memory));
                }
            });

        return (host, provider, store);
    }

    private static Task<TraconTestHost> StartHostAsync(InMemoryVectorStore store, Action<ITraconBuilder> configure)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.UseConversationMemory(static options =>
                {
                    options.CollectionName = Collection;
                    options.VectorDimensions = 8;
                });
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                configure(builder);
            },
            configureServices: services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<VectorStore>(store);
                services.AddSingleton<IRunAttributionContext>(static provider =>
                    new HeaderAttribution(provider.GetRequiredService<IHttpContextAccessor>()));
                services.AddSingleton<IDataSubjectResolver>(new SessionPrefixResolver());
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

    private static async Task<string> RunAsync(
        TraconTestHost host,
        string? user,
        string sessionId,
        string message,
        string agent = "memory-agent",
        string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = message, SessionId = sessionId }),
        };

        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        return body;
    }

    private static async Task QueueAsync(TraconTestHost host, string user, string sessionId, string message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/memory-agent/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = message, SessionId = sessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");
        request.Headers.Add(UserHeader, user);

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
    }

    /// <summary>Everything the model was sent on its last call, as one text.</summary>
    private static string ModelSaw(FakeModelProvider provider)
        => string.Join("\n", provider.Requests[^1].Messages.Select(static message => message.Text));

    /// <summary>Answers from the request; outside a request (the worker) it reports the ambient scope Tracon opens from the job.</summary>
    private sealed class HeaderAttribution(IHttpContextAccessor accessor) : IRunAttributionContext
    {
        public string? UserId => accessor.HttpContext is { } context
            ? (context.Request.Headers.TryGetValue(UserHeader, out var user) && user.ToString() is { Length: > 0 } value ? value : null)
            : AmbientRunAttributionScope.CurrentUserId;

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>A subject named "ada" owns the sessions that start with "ada"; a subject named "grace" those that start with "grace".</summary>
    private sealed class SessionPrefixResolver : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(string subjectId, string tenantId, CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = [$"{subjectId}-session"], RunIds = [], ConversationIds = [] });
    }
}
