using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The credential a tenant binds to a model provider is sent to that provider and appears nowhere else: not in a
/// response, a run record, a trace, an audit entry, a log line, a span, a metric tag, an export or a byte of the database.
/// </summary>
/// <remarks>
/// <para>
/// The binding names a configuration key; the value lives in configuration and is read at the moment of the call. The
/// provider here is a real local HTTP server, so the credential travels the whole way (binding, resolution, client
/// construction, the wire) and the test can say that it arrived. The tenant's endpoint override points at that server,
/// which is the case the egress guard exists for.
/// </para>
/// <para>
/// The failing runs are the hard part. The server answers 401 and echoes the credential it was sent in the body and in a
/// header, as a provider that quotes the key back in its error does. Whatever the run records, streams or returns about
/// that failure must not carry it. A second value, one an operator pastes into the field that wants a key NAME, must
/// not come back out of the validation either.
/// </para>
/// <para>
/// Five loggers and the status of the chat span write the provider's own error text, because an operator needs it. That
/// text may quote the key the call was made with; the call's own value is replaced in it, so those lines and that status
/// carry the sentence and not the key. The test asks for the sentence, so a log that vanished cannot pass for a clean one,
/// and it runs once with the trace content setting on, which makes the trace store keep the status.
/// </para>
/// <para>
/// The search looks for the stem of the value, not only the whole value, so a truncated or masked copy is found too.
/// A search that finds nothing proves nothing unless the run reached the provider, so the test also asks the provider
/// what it was sent.
/// </para>
/// </remarks>
public sealed class TenantProviderCanaryTests
{
    private const string Stem = "canary-tenant-provider-key";
    private const string Canary = Stem + "-6b1d09f2e7a35c48";
    private const string PastedStem = "canary-pasted-into-the-name-field";
    private const string Pasted = PastedStem + "-93c0e5a7d412f68b";
    private const string KeyName = "Tracon:ProviderKeys:tenant-a:Canary";
    private const string Provider = "canary-provider";
    private const string AgentName = "tenant-agent";
    private const string StoredAgentName = "tenant-stored-agent";
    private const string Tenant = "tenant-a";
    private const string TenantHeader = "X-Tracon-Tenant";

    /// <summary>
    /// What the refusing provider says, with the credential it quotes replaced. The provider quotes the whole key; the
    /// text Tracon writes keeps the sentence and replaces the key, so an operator reads what the provider said and the key
    /// is gone. A search for the key that finds nothing passes just as well when the line is missing, so every place that
    /// must carry this sentence is asked for it.
    /// </summary>
    private const string MaskedQuote = "Incorrect API key provided: Bearer [redacted]";

    /// <summary>
    /// The lines that write a provider's failure with the exception object itself, inner exceptions included: the chat
    /// telemetry ring, the model provider boundary, the run recorder and the agent, chat-completions, responses and replay
    /// endpoints, buffered and streamed. Each one wrote the credential before the call's credential was masked. A line is
    /// named by its logger and the start of its message, so an endpoint that stops writing its line fails here.
    /// </summary>
    private static readonly string[] QuotingLines =
    [
        "Microsoft.Extensions.AI.OpenTelemetryChatClient gen_ai.client.operation.exception",
        "Tracon.ModelProvider Model provider canary-provider failed during invocation",
        "Tracon.ModelProvider Model provider canary-provider failed during streaming invocation",
        "Tracon.RunRecordingAgent Run ",
        "Tracon.AgentEndpoints Agent run ",
        "Tracon.AgentEndpoints Streaming agent run ",
        "Tracon.OpenAICompat Chat Completions run for agent",
        "Tracon.OpenAICompat Streaming Chat Completions run",
        "Tracon.OpenAICompat Responses run for agent",
        "Tracon.OpenAICompat Streaming Responses run",
        "Tracon.RunEndpoints Run replay",
    ];

    [Fact]
    public async Task The_credential_of_a_tenant_binding_reaches_the_provider_and_no_other_output()
    {
        var evidence = await DriveAsync(database: null);

        evidence.Reached.ShouldBeGreaterThanOrEqualTo(4, "the runs have to have reached the provider, or an empty search proves nothing");
        evidence.Sent.ShouldContain("Bearer " + Canary, StringComparer.Ordinal);
        evidence.AnsweredReads.ShouldBeGreaterThan(40, "the read sweep reached too few operations for an empty search to mean anything");

        // The lines are not gone, they carry the provider's sentence with the key replaced: the operator keeps the detail.
        foreach (var line in QuotingLines)
        {
            evidence.LogLines.ShouldContain(
                entry => entry.Contains(line, StringComparison.Ordinal) && entry.Contains(MaskedQuote, StringComparison.Ordinal),
                $"no log line starting '{line}' carries the provider's sentence with the key replaced");
        }

        // The chat span of a failed call carries the exception's message as its status.
        evidence.Outputs["span names, tags, baggage and events"].ShouldContain(MaskedQuote, Case.Sensitive);

        AssertNoLeak(evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_credential_of_a_tenant_binding_is_in_no_byte_of_the_database(bool recordSensitiveData)
    {
        using var database = new TempSqliteDatabase("tenant-provider-canary");

        var evidence = await DriveAsync(database, recordSensitiveData);
        var bytes = database.ReadAllBytes();

        evidence.Reached.ShouldBeGreaterThanOrEqualTo(4);

        // The file is not empty and holds what the host wrote: the binding's key NAME and the agent a run used. A scan
        // of a file with nothing in it would pass for the wrong reason.
        ContainsBytes(bytes, Encoding.UTF8.GetBytes(KeyName)).ShouldBeTrue("the binding's key name is not in the database, so the scan reads the wrong place");
        ContainsBytes(bytes, Encoding.UTF8.GetBytes(AgentName)).ShouldBeTrue("the runs are not in the database");

        foreach (var (name, value) in (ValueTuple<string, string>[])[("tenant credential", Stem), ("pasted value", PastedStem)])
        {
            ContainsBytes(bytes, Encoding.UTF8.GetBytes(value)).ShouldBeFalse($"the {name} is in the database as UTF-8");
            ContainsBytes(bytes, Encoding.Unicode.GetBytes(value)).ShouldBeFalse($"the {name} is in the database as UTF-16");
        }

        if (recordSensitiveData)
        {
            // With the setting on, the trace store keeps a failed call's span status, which is the provider's message. The
            // database holds the sentence, and the key in it is replaced.
            ContainsBytes(bytes, Encoding.UTF8.GetBytes(MaskedQuote)).ShouldBeTrue("the stored trace does not hold the provider's sentence: the scan reads the wrong place");
            evidence.Outputs["responses and records"].ShouldContain(MaskedQuote, Case.Sensitive);
        }

        AssertNoLeak(evidence);
    }

    private static void AssertNoLeak(Evidence evidence)
    {
        var found = new List<string>();

        foreach (var (name, value) in (ValueTuple<string, string>[])[("tenant credential", Stem), ("value pasted into the name field", PastedStem)])
        {
            foreach (var (output, text) in evidence.Outputs)
            {
                var at = text.IndexOf(value, StringComparison.Ordinal);

                if (at >= 0)
                {
                    var from = Math.Max(0, at - 700);

                    found.Add($"the {name} is in the {output}: ...{text.Substring(from, Math.Min(text.Length - from, 1100))}...");
                }
            }
        }

        found.ShouldBeEmpty();
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;

    private static async Task<Evidence> DriveAsync(TempSqliteDatabase? database, bool recordSensitiveData = false)
    {
        var seen = new StringBuilder();
        using var probe = new LeakProbe();

        await using var provider = await CanaryProviderServer.StartAsync();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    })
                    .UseOpenAICompatible(Provider, options => options.Endpoint = provider.BaseAddress)
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = Provider, Model = "fake-local-model" },
                        Origin = AgentDefinitionOrigin.Code,
                    });

                if (database is not null)
                {
                    builder.UseSqlite(database.ConnectionString);
                }
            },
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection([new(KeyName, Canary)])
                    .Build());

                // The provider is on the loopback interface; the override is the tenant's, so the guard judges it.
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);

                services.Configure<TraconOptions>(options =>
                {
                    options.Observability.RecordSensitiveData = recordSensitiveData;
                    options.Observability.SuccessSampleRatio = 1;

                    // Every refused call has to reach the provider and be quoted. An open circuit answers the later calls
                    // with its own sentence, and a line that never carried the provider's text proves nothing.
                    options.CircuitBreaker.FailureThreshold = 1000;
                });
            });

        // A stored agent too, so the export has something of the tenant's to write.
        await SendAsync(host, HttpMethod.Post, "/tracon/api/agents", new AgentDefinitionRequest
        {
            Name = StoredAgentName,
            DisplayName = "Stored",
            Instructions = "Answer briefly.",
            Model = new ModelBinding { Provider = Provider, Model = "fake-local-model" },
        }, seen);

        // The operator pastes the VALUE where a key NAME belongs. The refusal must not quote it, and nothing may keep it.
        var pasted = await SendAsync(host, HttpMethod.Put, $"/tracon/api/tenants/{Tenant}/providers/{Provider}", new { apiKeyConfigurationName = Pasted }, seen);

        pasted.ShouldBe(HttpStatusCode.BadRequest);

        var bound = await SendAsync(
            host,
            HttpMethod.Put,
            $"/tracon/api/tenants/{Tenant}/providers/{Provider}",
            new { apiKeyConfigurationName = KeyName, endpoint = provider.BaseAddress.ToString() },
            seen);

        bound.ShouldBe(HttpStatusCode.OK);

        foreach (var path in (string[])[$"/tracon/api/tenants/{Tenant}/providers", $"/tracon/api/tenants/{Tenant}/egress"])
        {
            (await SendAsync(host, HttpMethod.Get, path, null, seen)).ShouldBe(HttpStatusCode.OK);
        }

        // Every run twice over: the provider accepts, then the provider refuses and quotes the credential back.
        foreach (var refuses in (bool[])[false, true])
        {
            provider.Refuses = refuses;

            var buffered = await SendAsync(host, HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run", new AgentRunRequest { Message = "hello" }, seen, idempotent: true);
            var streamed = await SendAsync(host, HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run", new AgentRunRequest { Message = "hello" }, seen);
            var chat = await SendAsync(host, HttpMethod.Post, "/tracon/v1/chat/completions", new { model = AgentName, messages = new[] { new { role = "user", content = "hello" } } }, seen);
            var chatStreamed = await SendAsync(host, HttpMethod.Post, "/tracon/v1/chat/completions", new { model = AgentName, stream = true, messages = new[] { new { role = "user", content = "hello" } } }, seen);

            var responses = await SendAsync(host, HttpMethod.Post, "/tracon/v1/responses", new { model = AgentName, input = "hello" }, seen);
            var responsesStreamed = await SendAsync(host, HttpMethod.Post, "/tracon/v1/responses", new { model = AgentName, stream = true, input = "hello" }, seen);

            if (!refuses)
            {
                foreach (var status in (HttpStatusCode[])[buffered, streamed, chat, chatStreamed, responses, responsesStreamed])
                {
                    status.ShouldBe(HttpStatusCode.OK);
                }
            }
        }

        // What the runs left behind, read the way an operator reads it.
        using var runList = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/runs?limit=50", UriKind.Relative));
        runList.Headers.Add(TenantHeader, Tenant);
        using var runListReply = await host.Client.SendAsync(runList, TestContext.Current.CancellationToken);
        var runs = await TraconTestHost.ReadJsonAsync(runListReply);

        runs.GetArrayLength().ShouldBeGreaterThanOrEqualTo(6);

        // A replay runs a recorded run again against the provider, which refuses by now: one more place that logs the failure.
        var recorded = runs.EnumerateArray().First(static run => string.Equals(run.GetProperty("status").GetString(), "completed", StringComparison.OrdinalIgnoreCase));

        // The agent is defined in code, which a replay can only run with its tools live; this one has none.
        await SendAsync(host, HttpMethod.Post, $"/tracon/api/runs/{recorded.GetProperty("id").GetGuid()}/replay", new { toolMode = "LiveTools" }, seen);

        foreach (var run in runs.EnumerateArray())
        {
            var id = run.GetProperty("id").GetGuid();

            foreach (var suffix in (string[])["", "/events", "/trace", "/tools", "/input", "/tree", "/usage", "/feedback"])
            {
                await SendAsync(host, HttpMethod.Get, $"/tracon/api/runs/{id}{suffix}", null, seen, readEventStreams: true);
            }
        }

        // The export, the audit trail through the API and through the store, the diagnostics report.
        foreach (var path in (string[])["/tracon/api/definitions/export", "/tracon/api/audit?limit=500", "/tracon/api/diagnostics", "/tracon/api/meta", "/tracon/api/agents", $"/tracon/api/agents/{StoredAgentName}"])
        {
            await SendAsync(host, HttpMethod.Get, path, null, seen);
        }

        var auditLog = host.Services.GetRequiredService<IAuditLog>();

        foreach (var tenant in (string[])[Tenant, "tenant-b", "default"])
        {
            var entries = await auditLog.QueryAsync(new AuditQuery { TenantId = tenant, Limit = 500 }, TestContext.Current.CancellationToken);

            seen.AppendLine(JsonSerializer.Serialize(entries));
        }

        // The whole read surface, for the tenant that holds the binding, for another one, and for the default.
        var reads = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not null)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.Ordinal) == true)
            .Select(static endpoint => endpoint.RoutePattern.RawText!)
            .ToList();

        var answered = 0;

        foreach (var template in reads)
        {
            foreach (var tenant in (string?[])[Tenant, "tenant-b", null])
            {
                var url = EndpointAuthorizationMatrixTests.Fill(template, tenant ?? "default");

                if (await SendAsync(host, HttpMethod.Get, url, null, seen, readEventStreams: false, tenant: tenant) == HttpStatusCode.OK)
                {
                    answered++;
                }
            }
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = seen.ToString(),
            ["log lines"] = string.Join('\n', host.Logs.Entries),
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        return new Evidence(outputs, provider.ReceivedAuthorization.ToList(), provider.Requests, answered, [.. host.Logs.Entries]);
    }

    /// <summary>Sends one request as the tenant (or as the named one) and appends what came back.</summary>
    private static async Task<HttpStatusCode> SendAsync(
        TraconTestHost host,
        HttpMethod method,
        string url,
        object? body,
        StringBuilder seen,
        bool idempotent = false,
        bool readEventStreams = true,
        string? tenant = Tenant)
    {
        using var request = SecretCanarySweepTests.Authorized(method, url, bearer: null);

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        if (idempotent)
        {
            // The header selects the single JSON answer instead of the event stream.
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await SecretCanarySweepTests.SendAsync(host, request, seen, readEventStreams);
    }

    private sealed record Evidence(IReadOnlyDictionary<string, string> Outputs, IReadOnlyList<string?> Sent, int Reached, int AnsweredReads, IReadOnlyList<string> LogLines)
    {
        public string Text => string.Join('\n', Outputs.Values);
    }

    /// <summary>
    /// An OpenAI-compatible server on the loopback interface that records the credential it is sent and, when told to,
    /// refuses it and quotes it back in the body and in a header.
    /// </summary>
    private sealed class CanaryProviderServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly List<string?> _received = [];
        private int _requests;

        private CanaryProviderServer(WebApplication app, Uri baseAddress)
        {
            _app = app;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public bool Refuses { get; set; }

        public IReadOnlyList<string?> ReceivedAuthorization
        {
            get
            {
                lock (_received)
                {
                    return [.. _received];
                }
            }
        }

        public int Requests => Volatile.Read(ref _requests);

        public static async Task<CanaryProviderServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var app = builder.Build();
            CanaryProviderServer? server = null;

            app.MapPost("/v1/chat/completions", async (HttpContext context) =>
            {
                var authorization = context.Request.Headers.Authorization.ToString();

                lock (server!._received)
                {
                    server._received.Add(authorization);
                }

                Interlocked.Increment(ref server._requests);

                using var reader = new StreamReader(context.Request.Body);
                var requestBody = await reader.ReadToEndAsync(context.RequestAborted);
                var streaming = requestBody.Contains("\"stream\":true", StringComparison.Ordinal);

                if (server.Refuses)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers["x-echoed-credential"] = authorization;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(new
                        {
                            error = new
                            {
                                message = $"Incorrect API key provided: {authorization}. Find your key at the provider's console.",
                                type = "invalid_request_error",
                                code = "invalid_api_key",
                            },
                        }),
                        context.RequestAborted);

                    return;
                }

                if (streaming)
                {
                    context.Response.ContentType = "text/event-stream";
                    await context.Response.WriteAsync(
                        $"data: {Chunk("""{"role":"assistant","content":"hello from the provider"}""", null)}\n\n" +
                        $"data: {Chunk("{}", "stop")}\n\n" +
                        "data: [DONE]\n\n",
                        context.RequestAborted);

                    return;
                }

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    """
                    {"id":"chatcmpl-canary","object":"chat.completion","created":1,"model":"fake-local-model",
                     "choices":[{"index":0,"message":{"role":"assistant","content":"hello from the provider"},"finish_reason":"stop"}],
                     "usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}
                    """,
                    context.RequestAborted);
            });

            await app.StartAsync();

            server = new CanaryProviderServer(app, new Uri($"{app.Urls.First()}/v1"));

            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static string Chunk(string delta, string? finish)
        {
            var finishJson = finish is null ? "null" : "\"" + finish + "\"";

            return "{\"id\":\"chatcmpl-canary\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"fake-local-model\","
                + "\"choices\":[{\"index\":0,\"delta\":" + delta + ",\"finish_reason\":" + finishJson + "}],\"usage\":null}";
        }
    }
}
