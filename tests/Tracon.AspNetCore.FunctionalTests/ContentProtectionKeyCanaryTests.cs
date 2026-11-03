using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The key material of content protection, which decrypts every protected column, appears in no output and in no byte of
/// the database it protects: not in a response, a log record in any of its parts, a span, a metric tag, or a file.
/// </summary>
/// <remarks>
/// <para>
/// The configuration names a key id and the NAME of the configuration key that holds the material; the material is read
/// when the first value is written or read. The scenarios are the ones that touch it: runs that write protected columns
/// and read them back, a second host started on the same database that decrypts what the first wrote, and the three
/// ways a key can be wrong (not Base64, the wrong length, well-formed material that is not the material a value was
/// written with). The wrong ones matter most: their text is written for an operator who is looking at a key.
/// </para>
/// <para>
/// The raw bytes of the key are searched as well as its Base64 text. A database that holds ciphertext holds neither; a
/// store that wrote a value unprotected would hold the run's own text, which the scan also asks for, so a scan of a file
/// that is plaintext after all cannot pass for a protected one.
/// </para>
/// </remarks>
public sealed class ContentProtectionKeyCanaryTests
{
    private const string AgentName = "protected-agent";
    private const string KeyId = "canary-2026";
    private const string KeyName = "ContentProtectionKeys:Canary";
    private const string Message = "canary-user-message-6e4f90d2";

    // 32 bytes whose Base64 text and raw bytes both carry a stem to search for.
    private static readonly byte[] KeyBytes = Encoding.ASCII.GetBytes("canary-cp-key-material-012345678");
    private static readonly byte[] OtherBytes = Encoding.ASCII.GetBytes("canary-cp-other-material-8765432");
    private static readonly byte[] ShortBytes = Encoding.ASCII.GetBytes("canary-cp-short-key");

    private static readonly string KeyBase64 = Convert.ToBase64String(KeyBytes);
    private static readonly string OtherBase64 = Convert.ToBase64String(OtherBytes);
    private static readonly string ShortBase64 = Convert.ToBase64String(ShortBytes);
    private const string NotBase64 = "canary-cp-not-base64-material-!!!-9d2c";

    [Fact]
    public async Task The_key_material_decrypts_the_columns_and_is_in_no_output_and_no_byte_of_the_database()
    {
        using var database = new TempSqliteDatabase("content-protection-canary");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();
        var logs = new List<string>();
        var seen = new StringBuilder();

        foreach (var second in (bool[])[false, true])
        {
            await using var host = await StartAsync(database, wire, KeyBase64, structured);
            var drive = new CanaryDrive(host);

            if (!second)
            {
                wire.Mode = CanaryWireServer.Behavior.Accept;

                foreach (var status in await drive.RunTheSixWaysAsync(AgentName))
                {
                    status.ShouldBe(HttpStatusCode.OK);
                }

                wire.Mode = CanaryWireServer.Behavior.Refuse;
                await drive.RunTheSixWaysAsync(AgentName);

                // One run whose input is a value of its own, to find it in the file if the column is not protected.
                wire.Mode = CanaryWireServer.Behavior.Accept;
                (await drive.SendAsync(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run", new { message = Message }, idempotent: true)).ShouldBe(HttpStatusCode.OK);
            }

            // The second host decrypts what the first wrote: the key is read again, from the configuration, on a new process.
            (await drive.ReadEverythingAsync()).ShouldBeGreaterThan(40);

            logs.AddRange(host.Logs.Entries);
            seen.AppendLine(drive.Seen);
        }

        var bytes = database.ReadAllBytes();
        SecretHits.FindInBytes(bytes, "$apEnc").ShouldNotBeEmpty("no column is protected: protection is off");

        // The protected columns are ciphertext: the file holds the agent's name (not a protected column) and no run text.
        SecretHits.FindInBytes(bytes, AgentName).ShouldNotBeEmpty("the runs are not in the database");
        SecretHits.FindInBytes(bytes, Message).ShouldBeEmpty("the run's input is in the database as plaintext: a protected column is not protected");

        // The runs decrypt on the second host: the reads that carry the model's sentence are answers, not 500s.
        seen.ToString().ShouldContain("hello from the provider");

        var outputs = Outputs(seen.ToString(), logs, structured, probe);
        var hits = SecretHits.Find(outputs, [new("key material (Base64)", KeyBase64), new("key material (stem)", "canary-cp-key-material")]);

        if (bytes.AsSpan().IndexOf(KeyBytes) >= 0 || SecretHits.FindInBytes(bytes, KeyBase64).Count > 0 || SecretHits.FindInBytes(bytes, "canary-cp-key-material").Count > 0)
        {
            hits.Add("the key material is in a byte of the database");
        }

        hits.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("wrong-length")]
    [InlineData("other-material")]
    public async Task A_wrong_key_is_named_by_its_id_and_its_value_is_in_no_output(string wrong)
    {
        using var database = new TempSqliteDatabase("content-protection-wrong-key");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();
        var logs = new List<string>();
        var seen = new StringBuilder();

        // A first host writes protected rows with the good key.
        await using (var first = await StartAsync(database, wire, KeyBase64, structured))
        {
            wire.Mode = CanaryWireServer.Behavior.Accept;
            var drive = new CanaryDrive(first);

            (await drive.SendAsync(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run", new { message = Message }, idempotent: true)).ShouldBe(HttpStatusCode.OK);
        }

        // The second host is given a key that cannot decrypt them, and a new run has to write with it.
        var wrongValue = wrong switch
        {
            "not-base64" => NotBase64,
            "wrong-length" => ShortBase64,
            _ => OtherBase64,
        };

        await using (var second = await StartAsync(database, wire, wrongValue, structured))
        {
            var drive = new CanaryDrive(second);

            // A run writes with the wrong key; a read decrypts with it. Neither may answer with the value.
            await drive.SendAsync(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run", new { message = Message }, idempotent: true);
            await drive.ReadEverythingAsync();

            logs.AddRange(second.Logs.Entries);
            seen.AppendLine(drive.Seen);
        }

        var outputs = Outputs(seen.ToString(), logs, structured, probe);

        // The operator is told which key is wrong, by id and by the name of its configuration key, never by its value.
        var everything = string.Join('\n', outputs.Values);

        everything.ShouldContain(KeyName, Case.Sensitive, string.Join('\n', everything.Split('\n').Where(static line => line.Contains("rotection", StringComparison.OrdinalIgnoreCase)).Take(6).Select(static line => line[..Math.Min(line.Length, 300)])));

        var hits = SecretHits.Find(
            outputs,
            [
                new("good key material", KeyBase64),
                new("good key material (stem)", "canary-cp-key-material"),
                new("wrong key material", wrongValue),
                new("wrong key material (stem)", wrongValue[..20]),
            ]);

        hits.ShouldBeEmpty();
    }

    private static Dictionary<string, string> Outputs(string seen, List<string> logs, StructuredLogProbe structured, LeakProbe probe)
        => new(StringComparer.Ordinal)
        {
            ["responses and records"] = seen,
            ["log lines"] = string.Join('\n', logs),
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database, CanaryWireServer wire, string keyValue, StructuredLogProbe structured)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .UseSqlite(database.ConnectionString)
                    .UseOpenAI("canary-cp-provider-key-1a2b3c4d", options => options.Endpoint = wire.BaseAddress)
                    .AddContentProtection(options =>
                    {
                        options.Enabled = true;
                        options.ActiveKeyId = KeyId;
                        options.Keys[KeyId] = KeyName;
                    })
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = "openai", Model = "canary-model" },
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, keyValue)]).Build());
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = false;
                    options.Observability.SuccessSampleRatio = 1;
                    options.CircuitBreaker.FailureThreshold = 1000;
                });
            });
}
