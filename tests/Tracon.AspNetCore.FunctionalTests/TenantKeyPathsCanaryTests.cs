using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The key a tenant binds to a provider is searched for on the paths a run can take that the first canary does not
/// drive: a workflow, an MCP <c>tools/call</c>, an A2A <c>SendMessage</c>, a queued run that a job worker takes, and a
/// run made inside a conversation.
/// </summary>
/// <remarks>
/// <para>
/// Each of these paths ends a failed model call its own way: the workflow engine wraps it, the MCP and A2A servers
/// answer a protocol error, the job worker stores the failure on a job and the run, the conversation stores items. The
/// provider here refuses and quotes the key it was sent, so every path has the key in its exception; the call's own key
/// is replaced in the text each of them writes, and the search asks every output.
/// </para>
/// <para>
/// A search that finds nothing proves nothing unless the path reached the provider, so each path's number of provider
/// calls is asked for, and so is the credential the provider was sent.
/// </para>
/// <para>
/// The log records are searched as an exporter would send them (state, scopes, the outermost exception message and the
/// exception's text), not through the inner exceptions one by one: a writer that reads those sees the raw message of
/// the provider's SDK, and that is a known open point measured in its own probe.
/// </para>
/// </remarks>
public sealed class TenantKeyPathsCanaryTests
{
    private const string Stem = "canary-paths-tenant-key";
    private const string Key = Stem + "-2c8d71e0a4b95f36";
    private const string KeyName = "Tracon:ProviderKeys:tenant-a:Canary";
    private const string Provider = "canary-provider";
    private const string AgentName = "paths-agent";
    private const string WorkflowName = "paths-chain";
    private const string Tenant = "tenant-a";
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task The_key_of_a_tenant_binding_is_in_no_output_of_a_workflow_an_mcp_call_an_a2a_message_a_queued_run_or_a_conversation()
    {
        using var database = new TempSqliteDatabase("tenant-key-paths");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();
        await using var host = await StartAsync(database, wire, structured);

        var drive = new CanaryDrive(host, Tenant);
        var reached = new Dictionary<string, int>(StringComparer.Ordinal);

        (await drive.SendAsync(
            HttpMethod.Put,
            $"/tracon/api/tenants/{Tenant}/providers/{Provider}",
            new { apiKeyConfigurationName = KeyName, endpoint = wire.BaseAddress.ToString() })).ShouldBe(HttpStatusCode.OK);

        (await drive.SendAsync(
            HttpMethod.Put,
            $"/tracon/api/workflows/{WorkflowName}",
            new { description = "One step.", kind = "Sequential", agentNames = new[] { AgentName } })).ShouldBe(HttpStatusCode.OK);

        foreach (var mode in (CanaryWireServer.Behavior[])[CanaryWireServer.Behavior.Accept, CanaryWireServer.Behavior.RefuseAndQuote])
        {
            wire.Mode = mode;

            await Path(reached, wire, $"{mode}: workflow", async () =>
                await drive.SendAsync(HttpMethod.Post, $"/tracon/api/workflows/{WorkflowName}/run", new { message = "hello" }));

            await Path(reached, wire, $"{mode}: MCP tools/call", async () =>
            {
                var (response, body) = await McpTestClient.SendAsync(
                    host.Client,
                    "/tracon/mcp",
                    "tools/call",
                    new { name = "tracon_" + AgentName, arguments = new { message = "hello" } },
                    tenantHeader: Tenant);

                drive.Keep($"{(int)response.StatusCode} {response.Headers}\n{body}");

                return response.StatusCode;
            });

            await Path(reached, wire, $"{mode}: A2A SendMessage", async () =>
            {
                var sent = new A2A.SendMessageRequest
                {
                    Message = new A2A.Message { Role = A2A.Role.User, Parts = [A2A.Part.FromText("hello")], MessageId = Guid.NewGuid().ToString() },
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{AgentName}")
                {
                    Content = JsonContent.Create(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = "SendMessage",
                        @params = JsonSerializer.SerializeToElement(sent, A2A.A2AJsonUtilities.DefaultOptions),
                    }),
                };
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Add(TenantHeader, Tenant);

                using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
                drive.Keep($"{(int)response.StatusCode} {response.Headers}\n{await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");

                return response.StatusCode;
            });

            await Path(reached, wire, $"{mode}: queued run", async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
                {
                    Content = JsonContent.Create(new { message = "hello" }),
                };
                request.Headers.Add("Prefer", "respond-async");
                request.Headers.Add(TenantHeader, Tenant);

                using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
                drive.Keep($"{(int)response.StatusCode} {response.Headers}\n{await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");

                // The worker takes the job and ends it; the reads below find it finished.
                await drive.WaitForRunsToSettleAsync();

                return response.StatusCode;
            });

            await Path(reached, wire, $"{mode}: conversation", async () =>
            {
                using var created = await host.Client.PostAsJsonAsync(new Uri("/tracon/v1/conversations", UriKind.Relative), new { }, TestContext.Current.CancellationToken);
                var id = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;

                var status = await drive.SendAsync(HttpMethod.Post, "/tracon/v1/responses", new { model = AgentName, input = "hello", conversation = id });

                await drive.SendAsync(HttpMethod.Get, $"/tracon/v1/conversations/{id}");
                await drive.SendAsync(HttpMethod.Get, $"/tracon/v1/conversations/{id}/items");

                return status;
            });
        }

        var answered = await drive.ReadEverythingAsync();

        answered.ShouldBeGreaterThan(40, "the read sweep reached too few operations for an empty search to mean anything");

        foreach (var (path, calls) in reached)
        {
            calls.ShouldBeGreaterThan(0, $"the path '{path}' never reached the provider, so a search of what it wrote proves nothing");
        }

        wire.Credentials.ShouldContain(credential => credential.Contains(Key, StringComparison.Ordinal), "the provider was never sent the key");

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, KeyName).ShouldNotBeEmpty("the binding is not in the database, so the scan reads the wrong place");
        SecretHits.FindInBytes(bytes, WorkflowName).ShouldNotBeEmpty("the workflow is not in the database");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = string.Join('\n', host.Logs.Entries),
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        var hits = SecretHits.Find(outputs, [new("tenant key", Stem)]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, Stem))
        {
            hits.Add($"the tenant key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    private static async Task Path(Dictionary<string, int> reached, CanaryWireServer wire, string name, Func<Task<HttpStatusCode>> drive)
    {
        var before = wire.Requests;

        await drive();

        reached[name] = wire.Requests - before;
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database, CanaryWireServer wire, StructuredLogProbe structured)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .UseOpenAICompatible(Provider, options => options.Endpoint = wire.BaseAddress)
                .UseWorkflows()
                .UseMcpServer(static options => options.ExposedAgents.Add(AgentName))
                .UseA2A(static options => options.ExposedAgents.Add(AgentName))
                .UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20))
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = Provider, Model = "canary-model" },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, Key)]).Build());
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = true;
                    options.Observability.SuccessSampleRatio = 1;
                    options.CircuitBreaker.FailureThreshold = 1000;
                });
            },
            configureAfterMap: static app =>
            {
                app.MapTraconMcpServer();
                app.MapTraconA2A();
            });
}
