using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The key a provider is registered with at setup (Anthropic, Azure OpenAI, Google, OpenAI on both of its surfaces) is sent to that provider and
/// appears nowhere else: not in a response, a run record, a trace, an audit entry, a log record in any of its parts, a
/// span, a metric tag, an export or a byte of the database.
/// </summary>
/// <remarks>
/// <para>
/// The tenant-bound key has its own canary; this one is the key every other tenant shares, the one set in code or in
/// configuration. Each provider gets a value of its own, so a hit names the slot it came from. The provider is a real
/// local HTTP server that speaks the provider's own wire, so the key travels the whole way (registration, client
/// construction, the SDK, the wire) and the test can say it arrived, in whichever header or query value the SDK chose.
/// </para>
/// <para>
/// The scenarios are the ones a key meets: the provider accepts, the provider refuses (without naming the key), the
/// client leaves while the provider holds the call open, and a second host started on the same database that reads
/// what the first left and replays a run. The refusal that quotes the key back is a separate gate, which asks that the
/// key the provider reports it was set up with is replaced in a failure's text.
/// </para>
/// </remarks>
public sealed class StartupProviderKeyCanaryTests
{
    private const string AgentName = "setup-key-agent";

    public static TheoryData<string, bool> Slots => new()
    {
        { "anthropic", false },
        { "anthropic", true },
        { "azure-openai", false },
        { "azure-openai", true },
        { "google", false },
        { "google", true },
        { "openai", false },
        { "openai", true },
        { "openai-responses", false },
        { "openai-responses", true },
    };

    [Theory]
    [MemberData(nameof(Slots))]
    public async Task The_setup_key_of_a_provider_reaches_that_provider_and_no_other_output(string provider, bool fromConfiguration)
    {
        using var database = new TempSqliteDatabase("setup-key-canary");
        var key = Key(provider, fromConfiguration);

        var evidence = await DriveAsync(provider, key, fromConfiguration, database, CanaryWireServer.Behavior.Refuse);
        var bytes = database.ReadAllBytes();

        evidence.AnsweredReads.ShouldBeGreaterThan(40, "the read sweep reached too few operations for an empty search to mean anything");
        evidence.ProviderRequests.ShouldBeGreaterThanOrEqualTo(13, "the runs have to have reached the provider, or an empty search proves nothing");
        evidence.Credentials.ShouldContain(credential => credential.Contains(key, StringComparison.Ordinal), "the provider was never sent the key");

        // The file is not empty and holds what the host wrote; a scan of a file with nothing in it passes for the wrong reason.
        SecretHits.FindInBytes(bytes, AgentName).ShouldNotBeEmpty("the runs are not in the database");

        var hits = SecretHits.Find(evidence.Outputs, [new(provider + " setup key", Stem(provider, fromConfiguration))]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, Stem(provider, fromConfiguration)))
        {
            hits.Add($"the {provider} setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    /// <summary>The part of a key that identifies its slot; a probe of another scenario gives its own so two runs at once cannot meet.</summary>
    internal static string Stem(string provider, bool fromConfiguration, string scenario = "")
        => $"canary-setup-{provider}-{(fromConfiguration ? "config" : "code")}{(scenario.Length == 0 ? string.Empty : "-" + scenario)}-key";

    internal static string Key(string provider, bool fromConfiguration, string scenario = "") => Stem(provider, fromConfiguration, scenario) + "-5d27c8f0b91e43a6";

    internal static async Task<Evidence> DriveAsync(
        string provider,
        string key,
        bool fromConfiguration,
        TempSqliteDatabase database,
        CanaryWireServer.Behavior refusal,
        bool expectSuccess = true)
    {
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();

        var logs = new List<string>();
        var seen = new StringBuilder();
        var answered = 0;

        // Two hosts one after the other on the same database: the second reads what the first left, and replays a run.
        foreach (var second in (bool[])[false, true])
        {
            await using var host = await TraconTestHost.StartAsync(
                configureTracon: builder =>
                {
                    builder.UseSqlite(database.ConnectionString);
                    Register(builder, provider, key, fromConfiguration, wire.BaseAddress);

                    builder.AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = provider, Model = "canary-model" },
                        Origin = AgentDefinitionOrigin.Code,
                    });
                },
                configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
                configureServices: services =>
                {
                    services.AddLogging(logging => logging.AddProvider(structured));
                    services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                    services.Configure<TraconOptions>(static options =>
                    {
                        options.Observability.RecordSensitiveData = true;
                        options.Observability.SuccessSampleRatio = 1;
                        options.CircuitBreaker.FailureThreshold = 1000;
                    });
                });

            var drive = new CanaryDrive(host);

            if (!second)
            {
                // The provider accepts, then refuses, then holds the call open while the client leaves.
                wire.Mode = CanaryWireServer.Behavior.Accept;

                foreach (var status in await drive.RunTheSixWaysAsync(AgentName))
                {
                    if (expectSuccess)
                    {
                        status.ShouldBe(HttpStatusCode.OK);
                    }
                }

                wire.Mode = refusal;
                await drive.RunTheSixWaysAsync(AgentName);

                wire.Mode = CanaryWireServer.Behavior.Hold;
                await drive.AbandonStreamedRunAsync(AgentName, TimeSpan.FromMilliseconds(400));
                await drive.WaitForRunsToSettleAsync();

                wire.Mode = CanaryWireServer.Behavior.Accept;
            }
            else
            {
                wire.Mode = refusal;
            }

            answered += await drive.ReadEverythingAsync(replayFirstCompleted: second);

            logs.AddRange(host.Logs.Entries);
            seen.AppendLine(drive.Seen);
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = seen.ToString(),
            ["log lines"] = string.Join('\n', logs),
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        return new Evidence(outputs, wire.Requests, wire.Credentials, answered);
    }

    internal static void Register(ITraconBuilder builder, string provider, string key, bool fromConfiguration, Uri endpoint)
    {
        if (fromConfiguration)
        {
            var section = new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new("ApiKey", key),
                    new("Endpoint", endpoint.ToString()),
                    new("DefaultModel", "canary-model"),
                    new("DefaultDeployment", "canary-model"),
                ])
                .Build();

            _ = provider switch
            {
                "anthropic" => builder.UseAnthropic(section),
                "azure-openai" => builder.UseAzureOpenAI(section),
                "google" => builder.UseGoogle(section),
                _ => builder.UseOpenAI(section),
            };

            return;
        }

        _ = provider switch
        {
            "anthropic" => builder.UseAnthropic(key, options => options.Endpoint = endpoint),
            "azure-openai" => builder.UseAzureOpenAI(endpoint, key),
            "google" => builder.UseGoogle(key, options => options.Endpoint = endpoint),
            _ => builder.UseOpenAI(key, options => options.Endpoint = endpoint),
        };
    }

    internal sealed record Evidence(IReadOnlyDictionary<string, string> Outputs, int ProviderRequests, IReadOnlyList<string> Credentials, int AnsweredReads);
}
