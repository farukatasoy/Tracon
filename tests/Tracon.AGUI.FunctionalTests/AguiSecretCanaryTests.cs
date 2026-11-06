using System.Net;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The provider's key is in no output of a run that a front end starts through the AG-UI endpoint: not in the event stream
/// (the <c>RUN_ERROR</c> message included), the run record, the log, a span, a metric or the database file.
/// </summary>
/// <remarks>
/// <para>
/// The AG-UI endpoint turns a failure into an event of its own, through the same normalizer the other streaming endpoints
/// use, but it is a separate writer: it is the last place a failure's text is composed before it reaches the front end.
/// The provider here accepts, refuses and quotes the key it was sent, and holds the call open while the client leaves; the
/// key is the startup key or a key a tenant bound to the provider.
/// </para>
/// <para>
/// A search that finds nothing proves nothing unless the run got that far, so the test asks for the provider's number of
/// calls, the credential it was sent, a <c>RUN_FINISHED</c> for the accepted run and a <c>RUN_ERROR</c> for the refused one.
/// </para>
/// </remarks>
public sealed class AguiSecretCanaryTests
{
    private const string Agent = "agui-key-agent";
    private const string Tenant = "tenant-a";
    private const string TenantProvider = "canary-provider";
    private const string TenantKeyName = "Tracon:ProviderKeys:tenant-a:Canary";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_provider_key_is_in_no_output_of_an_AGUI_run(bool tenantKey)
    {
        var stem = $"canary-agui-{(tenantKey ? "tenant" : "setup")}-key";
        var key = stem + "-6b02d9e4c718a35f";

        using var database = new TempSqliteDatabase("agui-key-canary");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();
        await using var host = await AGUITestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);

                if (tenantKey)
                {
                    builder
                        .UseTenancy(static options =>
                        {
                            options.Enabled = true;
                            options.AllowHeaderResolution = true;
                        })
                        .UseOpenAICompatible(TenantProvider, options => options.Endpoint = wire.BaseAddress);
                }
                else
                {
                    builder.UseOpenAI(key, options => options.Endpoint = wire.BaseAddress);
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = Agent,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = tenantKey ? TenantProvider : "openai", Model = "canary-model" },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                if (tenantKey)
                {
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(TenantKeyName, key)]).Build());
                }

                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = true;
                    options.Observability.SuccessSampleRatio = 1;
                    options.CircuitBreaker.FailureThreshold = 1000;
                });
            });

        var seen = new StringBuilder();

        if (tenantKey)
        {
            using var binding = new HttpRequestMessage(HttpMethod.Put, new Uri($"/tracon/api/tenants/{Tenant}/providers/{TenantProvider}", UriKind.Relative))
            {
                Content = new StringContent($$"""{"apiKeyConfigurationName":"{{TenantKeyName}}","endpoint":"{{wire.BaseAddress}}"}""", Encoding.UTF8, "application/json"),
            };
            binding.Headers.Add("X-Tracon-Tenant", Tenant);

            using var bound = await host.Client.SendAsync(binding, TestContext.Current.CancellationToken);
            bound.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        void AsTenant(HttpRequestMessage request)
        {
            if (tenantKey)
            {
                request.Headers.Add("X-Tracon-Tenant", Tenant);
            }
        }

        // The provider accepts: the stream ends in RUN_FINISHED.
        wire.Mode = CanaryWireServer.Behavior.Accept;
        var accepted = await host.RunAsync(Agent, AGUIInputs.User("thread-ok", "hello", "run-ok"), AsTenant);
        seen.AppendLine(accepted.Body);
        accepted.Types[^1].ShouldBe("RUN_FINISHED", accepted.Body);
        accepted.Text.ShouldContain(CanaryWireServer.Sentence, Case.Sensitive);

        // The provider refuses and quotes the key: the stream ends in RUN_ERROR.
        wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;
        var refused = await host.RunAsync(Agent, AGUIInputs.User("thread-refused", "hello", "run-refused"), AsTenant);
        seen.AppendLine(refused.Body);
        refused.Events.OfType<RunErrorEvent>().ShouldHaveSingleItem();

        // The provider holds the call open and the front end leaves.
        wire.Mode = CanaryWireServer.Behavior.Hold;
        using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AGUITestHost.Pattern + "/" + Agent, UriKind.Relative))
        {
            Content = new StringContent(JsonSerializer.Serialize(AGUIInputs.User("thread-held", "hello", "run-held"), AGUIJsonSerializerContext.Default.RunAgentInput), Encoding.UTF8, "application/json"),
        })
        {
            AsTenant(request);

            using var leaves = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            leaves.CancelAfter(TimeSpan.FromMilliseconds(600));

            try
            {
                using var held = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, leaves.Token);
                seen.AppendLine(await held.Content.ReadAsStringAsync(leaves.Token));
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                // The front end left while the provider held the call.
            }
        }

        wire.Mode = CanaryWireServer.Behavior.Accept;

        // What an operator reads afterwards: the list, every part of every run, the audit trail, the diagnostics.
        var runs = await ReadAsync(host, "/tracon/api/runs?limit=200", tenantKey, seen);

        foreach (var run in runs.EnumerateArray())
        {
            var id = run.GetProperty("id").GetGuid();

            foreach (var suffix in (string[])["", "/events", "/trace", "/tools", "/input", "/tree", "/usage", "/feedback"])
            {
                await ReadAsync(host, $"/tracon/api/runs/{id}{suffix}", tenantKey, seen);
            }
        }

        foreach (var path in (string[])["/tracon/api/definitions/export", "/tracon/api/audit?limit=500", "/tracon/api/diagnostics", "/tracon/api/agents"])
        {
            await ReadAsync(host, path, tenantKey, seen);
        }

        // Proof that the runs reached the provider and were recorded.
        runs.GetArrayLength().ShouldBeGreaterThanOrEqualTo(3, "the three runs are not in the run list");
        wire.Requests.ShouldBeGreaterThanOrEqualTo(3);
        wire.Credentials.ShouldContain(credential => credential.Contains(key, StringComparison.Ordinal), "the provider was never sent the key");

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, Agent).ShouldNotBeEmpty("the runs are not in the database, so the scan reads the wrong place");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event streams and reads"] = seen.ToString(),
            ["log lines"] = string.Join('\n', host.Logs.Lines),
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        var hits = SecretHits.Find(outputs, [new("provider key", stem)]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, stem))
        {
            hits.Add($"the provider key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    private static async Task<JsonElement> ReadAsync(AGUITestHost host, string path, bool tenantKey, StringBuilder seen)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (tenantKey)
        {
            request.Headers.Add("X-Tracon-Tenant", Tenant);
        }

        using var reply = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        seen.AppendLine((int)reply.StatusCode + " " + reply.Headers + " " + reply.Content.Headers + "\n" + text);

        return reply.Content.Headers.ContentType?.MediaType is "application/json" ? JsonDocument.Parse(text).RootElement.Clone() : default;
    }
}
