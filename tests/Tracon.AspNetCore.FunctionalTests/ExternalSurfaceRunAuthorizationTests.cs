using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An installation's own <see cref="IRunAuthorizationHandler"/> decides whether a caller may start a run over the two
/// external protocols, A2A and MCP, exactly as it does on every other run surface.
/// </summary>
/// <remarks>
/// <para>
/// Both surfaces start a run in the process, and neither shares an endpoint filter with the management API: the A2A
/// library calls the agent proxy and the MCP library calls a tool handler, so a surface that forgot to ask the gate would
/// look identical to every other test passing. Each protocol therefore gets its own refusing case, and every refusal case
/// proves what the refusal is for: no model call, no run row and no stored turn.
/// </para>
/// <para>
/// The refusal takes each protocol's own shape. A2A answers a JSON-RPC error (the library hides the cause of a handler
/// failure behind an opaque internal error and logs it, as it does for the other A2A refusals); an MCP tool call answers a
/// tool result with <c>isError</c> set, as it does for the other MCP refusals.
/// </para>
/// </remarks>
public sealed class ExternalSurfaceRunAuthorizationTests
{
    private const string Agent = "kod-agent";
    private const string ProviderName = "external-auth-provider";
    private const string ModelName = "external-auth-model";
    private const string DenyReason = "This caller may not run this agent.";
    private const string FailedCheckDetail = "The run authorization check failed. Retry the request.";

    // ---- A2A: SendMessage --------------------------------------------------------

    [Fact]
    public async Task A2A_SendMessage_is_answered_when_the_handler_allows_and_the_handler_is_asked_about_the_run()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartA2AAsync(provider, handler, attribution: "ada");

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false);

        body.ShouldContain("\"result\"");
        provider.Requests.Count.ShouldBe(1);

        var request = handler.RunRequests.ShouldHaveSingleItem();
        request.Access.ShouldBe(RunAccess.Start);
        request.Channel.ShouldBe(RunChannel.A2A);
        request.AgentName.ShouldBe(Agent);
        request.TenantId.ShouldBe("default");
        request.UserId.ShouldBe("ada");
        request.RunId.ShouldBeNull();
        request.SessionId.ShouldNotBeNull().ShouldStartWith(A2ASessionStore.SessionIdPrefix);
    }

    [Fact]
    public async Task A2A_SendMessage_is_refused_when_the_handler_denies_and_no_model_call_run_or_conversation_exists()
    {
        var provider = Provider();
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny(DenyReason) };
        await using var host = await StartA2AAsync(provider, handler);

        var (response, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "a JSON-RPC refusal rides a 200 like the library's other errors");
        body.ShouldContain("\"error\"");
        body.ShouldNotContain("\"result\"");
        body.ShouldNotContain("Echo", Case.Sensitive, "the model's answer never reached the caller");

        handler.RunRequests.ShouldHaveSingleItem().Access.ShouldBe(RunAccess.Start);
        provider.Requests.ShouldBeEmpty("a refusal after the model call has already cost money");
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a denied run opens no run row");
        await NoTurnWasKeptAsync(host);
        LoggedError(host, DenyReason).ShouldBeTrue("the operator can read the cause in the log");
    }

    [Fact]
    public async Task A2A_SendMessage_is_refused_for_one_user_and_answered_for_another_by_the_same_handler()
    {
        var provider = Provider();
        var identity = new SwitchableAttribution { UserId = "mallory" };
        var handler = new RecordingHandler
        {
            OnRun = static request => string.Equals(request.UserId, "ada", StringComparison.Ordinal)
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny(DenyReason),
        };
        await using var host = await StartA2AAsync(provider, handler, identity);

        var (_, refused) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false);

        refused.ShouldContain("\"error\"");
        provider.Requests.ShouldBeEmpty();

        identity.UserId = "ada";
        var (_, answered) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false);

        answered.ShouldContain("\"result\"");
        provider.Requests.Count.ShouldBe(1);
        handler.RunRequests.Select(static request => request.UserId).ShouldBe(["mallory", "ada"]);
    }

    [Fact]
    public async Task A2A_SendMessage_is_refused_when_the_handler_throws_and_the_failed_check_is_logged()
    {
        var provider = Provider();
        var handler = new RecordingHandler
        {
            OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down"),
        };
        await using var host = await StartA2AAsync(provider, handler);

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false);

        body.ShouldContain("\"error\"");
        body.ShouldNotContain("policy store is down", Case.Sensitive, "the consumer's exception text never reaches the caller");
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();

        var line = GateErrors(host).ShouldHaveSingleItem();
        line.ShouldContain(nameof(RecordingHandler));
        line.ShouldContain(nameof(RunAccess.Start));
        line.ShouldContain(Agent);
        line.ShouldContain("policy store is down");
        LoggedError(host, FailedCheckDetail).ShouldBeTrue("the refusal says the check failed, not that the handler decided");
    }

    // ---- A2A: SendStreamingMessage -----------------------------------------------

    [Fact]
    public async Task A2A_SendStreamingMessage_is_answered_when_the_handler_allows()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartA2AAsync(provider, handler, attribution: "ada");

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: true);

        body.ShouldContain("\"result\"");
        provider.Requests.Count.ShouldBe(1);
        handler.RunRequests.ShouldHaveSingleItem().Access.ShouldBe(RunAccess.Start);
    }

    [Fact]
    public async Task A2A_SendStreamingMessage_is_refused_when_the_handler_denies_and_no_model_call_run_or_conversation_exists()
    {
        var provider = Provider();
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny(DenyReason) };
        await using var host = await StartA2AAsync(provider, handler);

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: true);

        body.ShouldNotContain("Echo", Case.Sensitive, "the model's answer never reached the caller");
        body.ShouldNotContain("\"artifact", Case.Sensitive, "no answer artifact was streamed");

        handler.RunRequests.ShouldHaveSingleItem().Access.ShouldBe(RunAccess.Start);
        provider.Requests.ShouldBeEmpty("a refusal after the model call has already cost money");
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a denied run opens no run row");
        await NoTurnWasKeptAsync(host);
        LoggedError(host, DenyReason).ShouldBeTrue("the operator can read the cause in the log");
    }

    // ---- MCP: tools/call ---------------------------------------------------------

    [Fact]
    public async Task Mcp_tools_call_is_answered_when_the_handler_allows_and_the_handler_is_asked_about_the_run()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartMcpAsync(provider, handler, attribution: "ada");

        var result = await CallMcpAsync(host, "hello");

        IsError(result).ShouldBeFalse();
        provider.Requests.Count.ShouldBe(1);

        var request = handler.RunRequests.ShouldHaveSingleItem();
        request.Access.ShouldBe(RunAccess.Start);
        request.Channel.ShouldBe(RunChannel.Mcp);
        request.AgentName.ShouldBe(Agent);
        request.TenantId.ShouldBe("default");
        request.UserId.ShouldBe("ada");
        request.SessionId.ShouldBeNull("an MCP tool call is sessionless");
        request.RunId.ShouldBeNull();
    }

    [Fact]
    public async Task Mcp_tools_call_is_refused_when_the_handler_denies_and_no_model_call_or_run_exists()
    {
        var provider = Provider();
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny(DenyReason) };
        await using var host = await StartMcpAsync(provider, handler);

        var result = await CallMcpAsync(host, "hello");

        IsError(result).ShouldBeTrue();
        ResultText(result).ShouldContain(DenyReason);

        handler.RunRequests.ShouldHaveSingleItem().Access.ShouldBe(RunAccess.Start);
        provider.Requests.ShouldBeEmpty("a refusal after the model call has already cost money");
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a denied run opens no run row");
    }

    [Fact]
    public async Task Mcp_tools_call_is_refused_for_one_user_and_answered_for_another_by_the_same_handler()
    {
        var provider = Provider();
        var identity = new SwitchableAttribution { UserId = "mallory" };
        var handler = new RecordingHandler
        {
            OnRun = static request => string.Equals(request.UserId, "ada", StringComparison.Ordinal)
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny(DenyReason),
        };
        await using var host = await StartMcpAsync(provider, handler, identity);

        var refused = await CallMcpAsync(host, "hello");

        IsError(refused).ShouldBeTrue();
        provider.Requests.ShouldBeEmpty();

        identity.UserId = "ada";
        var answered = await CallMcpAsync(host, "hello");

        IsError(answered).ShouldBeFalse();
        provider.Requests.Count.ShouldBe(1);
        handler.RunRequests.Select(static request => request.UserId).ShouldBe(["mallory", "ada"]);
    }

    [Fact]
    public async Task Mcp_tools_call_is_refused_when_the_handler_throws_and_the_failed_check_is_logged()
    {
        var provider = Provider();
        var handler = new RecordingHandler
        {
            OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down"),
        };
        await using var host = await StartMcpAsync(provider, handler);

        var result = await CallMcpAsync(host, "hello");

        IsError(result).ShouldBeTrue();
        ResultText(result).ShouldContain(FailedCheckDetail);
        ResultText(result).ShouldNotContain("policy store is down", Case.Sensitive, "the consumer's exception text never reaches the caller");
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();

        var line = GateErrors(host).ShouldHaveSingleItem();
        line.ShouldContain(nameof(RecordingHandler));
        line.ShouldContain(nameof(RunAccess.Start));
        line.ShouldContain(Agent);
        line.ShouldContain("policy store is down");
    }

    [Fact]
    public async Task Mcp_tools_call_is_not_asked_about_a_tool_that_is_not_an_exposed_agent()
    {
        var provider = Provider();
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny(DenyReason) };
        await using var host = await StartMcpAsync(provider, handler);

        var (_, body) = await McpTestClient.SendAsync(
            host.Client,
            "/tracon/mcp",
            "tools/call",
            new { name = "tracon_not-exposed", arguments = new { message = "hello" } });

        var result = body!.Value.GetProperty("result");

        IsError(result).ShouldBeTrue();
        ResultText(result).ShouldContain("is not a known Tracon agent tool");
        handler.RunRequests.ShouldBeEmpty("the handler decides who may run an agent, not whether a tool name exists");
    }

    [Fact]
    public async Task Mcp_tools_call_while_the_process_drains_is_refused_by_the_drain_and_the_handler_is_not_asked()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartMcpAsync(provider, handler, drain: new FixedDrainState(isDraining: true));

        var result = await CallMcpAsync(host, "hello");

        IsError(result).ShouldBeTrue();
        ResultText(result).ShouldContain(DrainGate.Detail);
        handler.RunRequests.ShouldBeEmpty("a call the process will not accept is not put to the installation's policy");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mcp_task_mode_call_is_asked_in_the_tenant_of_the_request_that_carries_it()
    {
        // A task-mode call runs on a background task whose services have no request; the tenant it is asked in has to be
        // the one the request resolved.
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartMcpAsync(
            provider,
            handler,
            attribution: "ada",
            enableTasks: true,
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp", tenantHeader: "tenant-a");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "tracon_" + Agent, Arguments = Arguments("hello") },
            TestContext.Current.CancellationToken);

        outcome.IsTask.ShouldBeTrue();
        handler.RunRequests.ShouldHaveSingleItem().TenantId.ShouldBe("tenant-a");
    }

    // ---- Attribution limits ------------------------------------------------------

    [Fact]
    public async Task A2A_SendMessage_with_an_oversized_attribution_is_refused_with_the_sentence_before_the_handler_is_asked()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartA2AAsync(provider, handler, new OversizedAttribution());

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false);

        body.ShouldContain("\"error\"");
        body.ShouldContain("The run did not start", Case.Sensitive, "the caller can fix this, so the sentence reaches it");
        handler.RunRequests.ShouldBeEmpty("an attribution the platform refuses is not put to the installation's policy");
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A2A_SendStreamingMessage_with_an_oversized_attribution_is_refused()
    {
        var provider = Provider();
        await using var host = await StartA2AAsync(provider, new RecordingHandler(), new OversizedAttribution());

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: true);

        body.ShouldContain("The run did not start");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mcp_tools_call_with_an_oversized_attribution_is_an_error_result_before_the_handler_is_asked()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartMcpAsync(provider, handler, new OversizedAttribution());

        var result = await CallMcpAsync(host, "hello");

        IsError(result).ShouldBeTrue();
        ResultText(result).ShouldContain("The run did not start");
        handler.RunRequests.ShouldBeEmpty();
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Mcp_task_mode_call_with_an_oversized_attribution_is_refused_before_a_task_exists()
    {
        var provider = Provider();
        await using var host = await StartMcpAsync(provider, new RecordingHandler(), new OversizedAttribution(), enableTasks: true);

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "tracon_" + Agent, Arguments = Arguments("hello") },
            TestContext.Current.CancellationToken);

        outcome.IsTask.ShouldBeFalse("a refused call opens no task");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_attribution_within_the_limits_is_not_refused_by_either_protocol()
    {
        var provider = Provider();
        await using var a2a = await StartA2AAsync(provider, new RecordingHandler(), attribution: new string('u', 200));
        await using var mcp = await StartMcpAsync(provider, new RecordingHandler(), attribution: new string('u', 200));

        (await SendA2AAsync(a2a, "hello", contextId: "ctx-1", streaming: false)).Body.ShouldContain("\"result\"");
        IsError(await CallMcpAsync(mcp, "hello")).ShouldBeFalse();
    }

    [Fact]
    public async Task A2A_message_is_asked_in_the_tenant_of_the_request()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartA2AAsync(
            provider,
            handler,
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var (_, body) = await SendA2AAsync(host, "hello", contextId: "ctx-1", streaming: false, tenant: "tenant-a");

        body.ShouldContain("\"result\"");
        handler.RunRequests.ShouldHaveSingleItem().TenantId.ShouldBe("tenant-a");
    }

    [Fact]
    public async Task A2A_proxy_called_with_no_request_in_scope_is_refused_rather_than_run_unasked()
    {
        // The A2A library only calls the proxy from a request. A call with none has no caller to name, and a handler
        // cannot decide about nobody: the proxy refuses it instead of running it.
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartA2AAsync(provider, handler);

        var proxy = new ExternalAgentProxy(Agent);
        proxy.AttachServices(host.Services);

        var failure = await Should.ThrowAsync<TraconExternalCallException>(
            async () => await proxy.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken));

        failure.Protocol.ShouldBe("a2a");
        failure.AgentName.ShouldBe(Agent);
        handler.RunRequests.ShouldBeEmpty();
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mcp_task_mode_call_is_refused_before_a_task_or_a_run_row_is_created()
    {
        // A task-mode call runs on a background task with no request, so the caller's identity is only there on the
        // request that carries the call: the handler is asked there, before the SDK decides to create a task.
        var provider = Provider();
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny(DenyReason) };
        await using var host = await StartMcpAsync(provider, handler, attribution: "mallory", enableTasks: true);

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams
            {
                Name = "tracon_" + Agent,
                Arguments = Arguments("hello"),
            },
            TestContext.Current.CancellationToken);

        outcome.IsTask.ShouldBeFalse("no task was created for a refused call");
        outcome.Result.ShouldNotBeNull().IsError.ShouldBe(true);

        var request = handler.RunRequests.ShouldHaveSingleItem();
        request.UserId.ShouldBe("mallory", "the handler saw the caller, which only the request carries");
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a denied call opens no placeholder run row");
    }

    [Fact]
    public async Task Mcp_task_mode_call_is_asked_with_the_caller_and_runs_in_the_background_when_the_handler_allows()
    {
        var provider = Provider();
        var handler = new RecordingHandler();
        await using var host = await StartMcpAsync(provider, handler, attribution: "ada", enableTasks: true);

        await using var client = await McpTaskTestClient.ConnectAsync(host.Client, "/tracon/mcp");

        var outcome = await client.CallToolAsTaskAsync(
            new CallToolRequestParams
            {
                Name = "tracon_" + Agent,
                Arguments = Arguments("hello"),
            },
            TestContext.Current.CancellationToken);

        outcome.IsTask.ShouldBeTrue();

        var request = handler.RunRequests.ShouldHaveSingleItem();
        request.UserId.ShouldBe("ada");
        request.Access.ShouldBe(RunAccess.Start);
    }

    // ---- scaffolding -------------------------------------------------------------

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static AgentDefinition Definition()
        => TestData.Definition(Agent) with
        {
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
        };

    private static Task<TraconTestHost> StartA2AAsync(
        FakeModelProvider provider,
        IRunAuthorizationHandler handler,
        IRunAttributionContext? identity = null,
        string? attribution = null,
        Action<ITraconBuilder>? configureTracon = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(Definition())
                    .UseA2A(static options => options.ExposedAgents.Add(Agent));
                configureTracon?.Invoke(builder);
            },
            configureServices: services => Register(services, handler, identity, attribution),
            configureAfterMap: static app => app.MapTraconA2A());

    private static Task<TraconTestHost> StartMcpAsync(
        FakeModelProvider provider,
        IRunAuthorizationHandler handler,
        IRunAttributionContext? identity = null,
        string? attribution = null,
        bool enableTasks = false,
        Action<ITraconBuilder>? configureTracon = null,
        ITraconDrainState? drain = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(Definition())
                    .UseMcpServer(options =>
                    {
                        options.ExposedAgents.Add(Agent);
                        options.EnableTasks = enableTasks;
                    });
                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                Register(services, handler, identity, attribution);

                if (drain is not null)
                {
                    services.AddSingleton(drain);
                }
            },
            configureAfterMap: static app => app.MapTraconMcpServer());

    private static void Register(
        IServiceCollection services,
        IRunAuthorizationHandler handler,
        IRunAttributionContext? identity,
        string? attribution)
    {
        // Registered BEFORE Tracon's own TryAdd, the way a consumer binding its own policy would.
        services.Replace(ServiceDescriptor.Singleton(handler));

        if (identity is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(identity));
        }
        else if (attribution is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new SwitchableAttribution { UserId = attribution }));
        }
    }

    private static async Task<(HttpResponseMessage Response, string Body)> SendA2AAsync(
        TraconTestHost host,
        string text,
        string contextId,
        bool streaming,
        string? tenant = null)
    {
        // Built with the SDK's own types and serializer options, as in A2AEndpointTests: the field names and casing come
        // from the contract.
        var message = JsonSerializer.SerializeToElement(
            new A2A.SendMessageRequest
            {
                Message = new A2A.Message
                {
                    Role = A2A.Role.User,
                    Parts = [A2A.Part.FromText(text)],
                    MessageId = Guid.NewGuid().ToString(),
                    ContextId = contextId,
                },
            },
            A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{Agent}")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = streaming ? "SendStreamingMessage" : "SendMessage",
                @params = message,
            }),
        };
        request.Headers.Accept.ParseAdd(streaming ? "text/event-stream" : "application/json");

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response, body);
    }

    private static async Task<JsonElement> CallMcpAsync(TraconTestHost host, string text)
    {
        var (response, body) = await McpTestClient.SendAsync(
            host.Client,
            "/tracon/mcp",
            "tools/call",
            new { name = "tracon_" + Agent, arguments = new { message = text } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return body!.Value.GetProperty("result");
    }

    private static Dictionary<string, JsonElement> Arguments(string message)
        => new(StringComparer.Ordinal) { ["message"] = JsonSerializer.SerializeToElement(message) };

    // A result that is not an error leaves the member out.
    private static bool IsError(JsonElement result)
        => result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True;

    private static string ResultText(JsonElement result)
        => string.Concat(result.GetProperty("content").EnumerateArray().Select(static block => block.GetProperty("text").GetString()));

    // The A2A library saves the conversation of a call in a finally, whatever the call ended in, so a refused first
    // message can leave the empty shell of a session behind (its identity and nothing else). What must not exist is the
    // turn: the message the caller sent is in no stored session.
    private static async Task NoTurnWasKeptAsync(TraconTestHost host)
    {
        var sessions = await host.Services.GetRequiredService<AgentSessionManager>()
            .QuerySessionsAsync(new SessionQuery { Take = 50 }, TestContext.Current.CancellationToken);

        sessions.ShouldAllBe(
            static session => !session.State.ToString().Contains("hello", StringComparison.Ordinal),
            "a denied turn is not kept in a conversation");
    }

    // The A2A library answers a handler failure with an opaque internal error and logs the real exception at Error level.
    private static bool LoggedError(TraconTestHost host, string text)
        => host.Logs.Entries.Any(entry =>
            entry.StartsWith("Error", StringComparison.Ordinal) && entry.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>The Error lines the authorization gates wrote, in order.</summary>
    private static List<string> GateErrors(TraconTestHost host)
        => host.Logs.Entries
            .Where(static line => line.StartsWith("Error Tracon.RunAuthorization ", StringComparison.Ordinal))
            .ToList();

    /// <summary>An attribution whose user identifier is one character over the limit.</summary>
    private sealed class OversizedAttribution : IRunAttributionContext
    {
        public string? UserId => new('u', RunLabels.MaxUserIdLength + 1);

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>An <see cref="IRunAuthorizationHandler"/> that records every run question and decides through a callback.</summary>
    private sealed class RecordingHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RunAuthorizationRequest> _runRequests = [];

        public Func<RunAuthorizationRequest, RunAuthorizationResult>? OnRun { get; set; }

        public IReadOnlyList<RunAuthorizationRequest> RunRequests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _runRequests];
                }
            }
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _runRequests.Add(request);
            }

            return ValueTask.FromResult(OnRun?.Invoke(request) ?? RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
