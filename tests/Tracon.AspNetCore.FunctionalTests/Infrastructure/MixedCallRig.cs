using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// An agent with a server tool and a client-side tool, run through the real compiled stack (host, run recording, tool
/// wrapper chain, the OpenAI adapter) against <see cref="StrictToolCallServer"/>, which answers from a script and
/// refuses a history that holds a tool call without a result.
/// </summary>
/// <remarks>
/// The test writes the model's answers into <see cref="Server"/> before the first turn and drives the turns with
/// <see cref="RunAsync"/>; a turn is read the way a caller reads it, as the contents of the answer or the frames of the
/// stream.
/// </remarks>
internal sealed class MixedCallRig : IAsyncDisposable
{
    public const string AgentName = "mixed-call-agent";
    public const string SessionId = "mixed-session";
    public const string Tenant = "tenant-a";

    public const string BackendTool = "lookup_order";
    public const string SecondBackendTool = "lookup_customer";
    public const string ApprovalTool = "cancel_order";
    public const string ClientTool = "open_page";

    public const string BackendCallId = "call_backend";
    public const string SecondBackendCallId = "call_backend2";
    public const string ApprovalCallId = "call_approval";
    public const string ClientCallId = "call_client";

    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    private readonly bool _ownsServer;

    private MixedCallRig(StrictToolCallServer server, TraconTestHost host, Probe probe, bool ownsServer)
    {
        Server = server;
        Host = host;
        Probe = probe;
        _ownsServer = ownsServer;
    }

    /// <summary>The answer a model gives that asks for a server call and a client call at once.</summary>
    public static (string Id, string Name, string Args)[] BackendAndClientCalls { get; } =
        [(BackendCallId, BackendTool, """{"orderId":"7"}"""), (ClientCallId, ClientTool, "{}")];

    /// <summary>The answer a model gives that asks for the client call alone.</summary>
    public static (string Id, string Name, string Args)[] ClientCallOnly { get; } = [(ClientCallId, ClientTool, "{}")];

    public StrictToolCallServer Server { get; }

    public TraconTestHost Host { get; }

    public Probe Probe { get; }

    /// <summary>Starts the rig. The server is the rig's own unless the test hands one in, which the test then disposes.</summary>
    /// <param name="options">What the agent carries.</param>
    /// <param name="server">A server a former host of the same test used; the new host talks to it.</param>
    public static async Task<MixedCallRig> StartAsync(MixedCallRigOptions? options = null, StrictToolCallServer? server = null)
    {
        options ??= new MixedCallRigOptions();
        var ownsServer = server is null;
        server ??= await StrictToolCallServer.StartAsync();

        var probe = new Probe();

        var host = await TraconTestHost.StartAsync(
            builder => Configure(builder, options, probe, server),
            configureServices: services =>
            {
                if (options.ConfigureOptions is { } configure)
                {
                    services.Configure<TraconOptions>(tracon =>
                    {
                        tracon.Pricing.Currency = "USD";
                        configure(tracon);
                    });
                }

                if (options.Clock is { } clock)
                {
                    services.AddSingleton(clock);
                }

                options.Services?.Invoke(services);
            });

        return new MixedCallRig(server, host, probe, ownsServer);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();

        if (_ownsServer)
        {
            await Server.DisposeAsync();
        }
    }

    /// <summary>Sends one request and reads the answer the way a caller reads it.</summary>
    /// <param name="body">The request.</param>
    /// <param name="streaming">Whether the caller reads the stream or waits for the whole answer.</param>
    /// <param name="tenant">The tenant the request is sent for.</param>
    public async Task<Turn> RunAsync(AgentRunRequest body, bool streaming, string tenant = Tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Tracon-Tenant", tenant);

        // A request without an idempotency key is answered as a stream.
        if (!streaming)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        using var response = await Host.Client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new Turn(response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(false), [], []);
        }

        var contents = new List<JsonElement>();

        if (streaming)
        {
            await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken).ConfigureAwait(false)))
            {
                if (!string.Equals(frame.Event, "update", StringComparison.Ordinal))
                {
                    continue;
                }

                using var update = JsonDocument.Parse(frame.Data);

                if (update.RootElement.TryGetProperty("contents", out var updateContents))
                {
                    contents.AddRange(updateContents.EnumerateArray().Select(static content => content.Clone()));
                }
            }
        }
        else
        {
            var json = await TraconTestHost.ReadJsonAsync(response).ConfigureAwait(false);

            foreach (var message in json.GetProperty("response").GetProperty("messages").EnumerateArray())
            {
                contents.AddRange(message.GetProperty("contents").EnumerateArray().Select(static content => content.Clone()));
            }
        }

        // The usage a stream reports is not part of what a caller acts on.
        contents.RemoveAll(static content => string.Equals(content.GetProperty("$type").GetString(), "usage", StringComparison.Ordinal));

        return new Turn(response.StatusCode, string.Empty, [.. contents.Select(Describe)], contents);
    }

    /// <summary>The runs of the test's session, oldest first.</summary>
    public async Task<List<RunRecord>> RunsAsync(string session = SessionId, string tenant = Tenant)
        => [.. (await GetAsync<List<RunRecord>>($"/tracon/api/runs?sessionId={session}&take=100", tenant).ConfigureAwait(false)).OrderBy(static run => run.StartedAt)];

    /// <summary>The tool rows of one run.</summary>
    public Task<List<ToolInvocationRecord>> ToolRowsAsync(Guid runId, string tenant = Tenant)
        => GetAsync<List<ToolInvocationRecord>>($"/tracon/api/runs/{runId}/tools", tenant);

    /// <summary>The recorded events of one run, as the events endpoint writes them (event-stream text).</summary>
    public async Task<string> EventsTextAsync(Guid runId, string tenant = Tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));
        request.Headers.Add("X-Tracon-Tenant", tenant);

        using var response = await Host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async Task<T> GetAsync<T>(string uri, string tenant = Tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, UriKind.Relative));
        request.Headers.Add("X-Tracon-Tenant", tenant);

        using var response = await Host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken).ConfigureAwait(false))!;
    }

    private static void Configure(ITraconBuilder builder, MixedCallRigOptions options, Probe probe, StrictToolCallServer server)
    {
        builder.UseOpenAI(openai =>
        {
            openai.ApiKey = "scripted-server-key";
            openai.Endpoint = server.BaseAddress;
        });

        builder.UseTenancy(static tenancy =>
        {
            tenancy.Enabled = true;
            tenancy.AllowHeaderResolution = true;
        });

        if (options.SqliteConnectionString is { } connectionString)
        {
            builder.UseSqlite(connectionString);
        }

        var backendResult = options.BackendResult;
        var spends = options.BackendSpendsTokens;
        var body = options.BackendBody;

        builder.AddTool(
            AIFunctionFactory.Create(
                async () =>
                {
                    probe.BackendStarted();

                    if (body is not null)
                    {
                        await body().ConfigureAwait(false);
                    }

                    probe.BackendRan();

                    if (spends > 0)
                    {
                        TraconRunContext.Current!.Budget!.RecordUsage(spends);
                    }

                    return backendResult;
                },
                BackendTool,
                "Looks an order up."),
            tool => tool.MaxOutputBytes = options.BackendMaxOutputBytes);

        builder.AddTool(
            AIFunctionFactory.Create(
                () =>
                {
                    probe.SecondBackendRan();

                    return "customer 3";
                },
                SecondBackendTool,
                "Looks a customer up."));

        if (options.WithApprovalTool)
        {
            builder.AddTool(
                AIFunctionFactory.Create(
                    () =>
                    {
                        probe.ApprovalRan();

                        return "order canceled";
                    },
                    ApprovalTool,
                    "Cancels an order."),
                tool => tool.RequiresApproval = true);
        }

        if (options.WithClientTool)
        {
            builder.AddClientTool(ClientTool, "Opens a page in the user's browser.", EmptyObjectSchema);
        }

        var toolNames = new List<string> { BackendTool, SecondBackendTool };

        if (options.WithApprovalTool)
        {
            toolNames.Add(ApprovalTool);
        }

        if (options.WithClientTool)
        {
            toolNames.Add(ClientTool);
        }

        var model = new ModelBinding { Provider = "openai", Model = "gpt-test" };

        if (!options.FactoryAgent)
        {
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer the user.",
                Model = model,
                ToolNames = [.. toolNames],
                Harness = options.Harness,
                Origin = AgentDefinitionOrigin.Code,
            });

            return;
        }

        var names = toolNames.ToArray();

        builder.AddAgent(
            AgentName,
            model,
            context =>
            {
                var registry = context.Services.GetRequiredService<IToolRegistry>();
                var tools = names.Select(name => registry.TryGet(name, out var tool) ? (AITool)tool : null!).ToList();

                return context.ChatClient.AsAIAgent(new ChatClientAgentOptions
                {
                    Id = AgentName,
                    Name = AgentName,
                    ChatOptions = new ChatOptions { Instructions = "Answer the user.", Tools = tools },
                });
            });
    }

    private static string Describe(JsonElement content)
    {
        var type = content.GetProperty("$type").GetString();

        return type switch
        {
            "functionCall" => $"functionCall({content.GetProperty("callId").GetString()}:{content.GetProperty("name").GetString()})",
            "functionResult" => $"functionResult({content.GetProperty("callId").GetString()})",
            "text" => $"text({content.GetProperty("text").GetString()})",
            _ when content.TryGetProperty("requestId", out var requestId) => $"{type}({requestId.GetString()})",
            _ => type ?? "?",
        };
    }

    /// <summary>What one request got back.</summary>
    /// <param name="Status">The HTTP status.</param>
    /// <param name="Body">The body of a refusal; empty for an answer.</param>
    /// <param name="Contents">What the answer holds, in order: <c>functionCall(id:name)</c>, <c>functionResult(id)</c>, <c>text(...)</c>, or the approval request.</param>
    /// <param name="Raw">The contents as the server wrote them.</param>
    internal sealed record Turn(HttpStatusCode Status, string Body, IReadOnlyList<string> Contents, IReadOnlyList<JsonElement> Raw)
    {
        /// <summary>The id of the first approval request the answer holds.</summary>
        public string ApprovalRequestId => Raw.First(static content => content.TryGetProperty("requestId", out _)).GetProperty("requestId").GetString()!;

        /// <summary>The text of the answer.</summary>
        public string Text => string.Concat(Raw.Where(static content => string.Equals(content.GetProperty("$type").GetString(), "text", StringComparison.Ordinal)).Select(static content => content.GetProperty("text").GetString()));

        /// <summary>The arguments of the call with this id, as the caller received them.</summary>
        public string ArgumentsOf(string callId)
            => Raw.First(content => string.Equals(content.GetProperty("$type").GetString(), "functionCall", StringComparison.Ordinal) &&
                                    string.Equals(content.GetProperty("callId").GetString(), callId, StringComparison.Ordinal))
                .GetProperty("arguments").GetRawText();

    }
}

/// <summary>What the agent of a <see cref="MixedCallRig"/> carries.</summary>
internal sealed class MixedCallRigOptions
{
    public bool WithClientTool { get; init; } = true;

    public bool WithApprovalTool { get; init; }

    public HarnessSettings? Harness { get; init; }

    /// <summary>Builds the agent from a factory over the client Tracon hands it, not from a definition.</summary>
    public bool FactoryAgent { get; init; }

    public string BackendResult { get; init; } = "order 7 shipped";

    public int? BackendMaxOutputBytes { get; init; }

    public int BackendSpendsTokens { get; init; }

    /// <summary>Runs inside the server tool, after it started and before it finishes.</summary>
    public Func<Task>? BackendBody { get; init; }

    public string? SqliteConnectionString { get; init; }

    public TimeProvider? Clock { get; init; }

    public Action<IServiceCollection>? Services { get; init; }

    public Action<TraconOptions>? ConfigureOptions { get; init; }
}

/// <summary>Counts what the tool bodies of a <see cref="MixedCallRig"/> did.</summary>
internal sealed class Probe
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _backendRuns;
    private int _secondBackendRuns;
    private int _approvalRuns;

    public int BackendRuns => Volatile.Read(ref _backendRuns);

    public int SecondBackendRuns => Volatile.Read(ref _secondBackendRuns);

    public int ApprovalRuns => Volatile.Read(ref _approvalRuns);

    /// <summary>Completes when the server tool has started.</summary>
    public Task BackendHasStarted => _started.Task;

    public ConcurrentBag<string> BackendSeenRun { get; } = [];

    public void BackendStarted()
    {
        BackendSeenRun.Add($"tenant={TraconRunContext.Current?.TenantId}");
        _started.TrySetResult();
    }

    public void BackendRan() => Interlocked.Increment(ref _backendRuns);

    public void SecondBackendRan() => Interlocked.Increment(ref _secondBackendRuns);

    public void ApprovalRan() => Interlocked.Increment(ref _approvalRuns);
}
