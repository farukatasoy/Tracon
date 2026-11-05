using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// The secrets a host holds are sent where they belong and appear nowhere else on a database server: not in a response,
/// a log record in any of its parts, a span, a metric tag, and not in a single text, JSON or binary value of any table.
/// </summary>
/// <remarks>
/// <para>
/// Three secrets, each a value of its own: the key a model provider is registered with at setup, the key a tenant binds
/// to a provider (the binding stores the NAME of a configuration key, the value is read when the call is made), and the
/// password in the connection string of the login the host connects with. The server is a real container, so the
/// password travels the whole way: it is a login of the server, the host reads it from the connection string, the driver
/// sends it, and every failure of a connection is written by the driver's own words.
/// </para>
/// <para>
/// The scenarios are the ones a secret meets: the provider accepts, refuses (and quotes back the key the call was made
/// with, the tenant's or the setup key), the client leaves while the provider holds the call, and a second host on the same schema reads what the
/// first wrote and replays a run. Then the connection fails three ways (a wrong password, a server nothing listens on
/// with migrations on, the same with migrations off) and the same search runs over what each of them wrote.
/// </para>
/// <para>
/// The search of the database reads every text, JSON and binary column of every table of the schema through a new
/// connection and looks for each secret's stem, so a value stored in a column nobody thought to list is found too.
/// </para>
/// </remarks>
public abstract class SecretCanaryAcceptanceContract
{
    private const string SetupStem = "canary-db-setup-key";
    private const string TenantStem = "canary-db-tenant-key";
    private const string PasswordStem = "canary-db-login-password";
    private const string WrongStem = "canary-db-wrong-password";
    private const string UnreachableStem = "canary-db-unreachable-password";

    private const string SetupAgent = "setup-key-agent";
    private const string TenantAgent = "tenant-key-agent";
    private const string Tenant = "tenant-a";
    private const string TenantProvider = "canary-provider";
    private const string TenantKeyName = "Tracon:ProviderKeys:tenant-a:Canary";

    /// <summary>Gets a schema on the class's server, owned by a login with the given password.</summary>
    /// <param name="password">The password of the login.</param>
    /// <returns>The database the hosts connect to.</returns>
    protected abstract Task<TwoNodeDatabase> NewDatabaseAsync(string password);

    /// <summary>The three secrets reach where they belong and nothing else, through two hosts and every store the runs wrote.</summary>
    [Fact]
    public async Task The_secrets_of_a_host_are_in_no_output_and_no_value_of_the_database()
    {
        var database = await NewDatabaseAsync(PasswordStem + "-8c1e5d70");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();

        var logs = new List<string>();
        var seen = new StringBuilder();
        var answered = 0;

        foreach (var second in (bool[])[false, true])
        {
            await using var host = await StartAsync(database.Use, wire, structured);
            var setup = new CanaryDrive(host);
            var tenant = new CanaryDrive(host, Tenant);

            if (!second)
            {
                (await tenant.SendAsync(
                    HttpMethod.Put,
                    $"/tracon/api/tenants/{Tenant}/providers/{TenantProvider}",
                    new { apiKeyConfigurationName = TenantKeyName, endpoint = wire.BaseAddress.ToString() })).ShouldBe(HttpStatusCode.OK);

                wire.Mode = CanaryWireServer.Behavior.Accept;

                foreach (var status in (await setup.RunTheSixWaysAsync(SetupAgent)).Concat(await tenant.RunTheSixWaysAsync(TenantAgent)))
                {
                    status.ShouldBe(HttpStatusCode.OK);
                }

                // The provider refuses and quotes the key it was called with: the tenant's key, which the call's own mask
                // replaces, and the setup key, which the provider reports and the mask of its calls replaces.
                wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;
                await setup.RunTheSixWaysAsync(SetupAgent);
                await tenant.RunTheSixWaysAsync(TenantAgent);

                wire.Mode = CanaryWireServer.Behavior.Hold;
                await setup.AbandonStreamedRunAsync(SetupAgent, TimeSpan.FromMilliseconds(400));
                await tenant.AbandonStreamedRunAsync(TenantAgent, TimeSpan.FromMilliseconds(400));
                await setup.WaitForRunsToSettleAsync();
                await tenant.WaitForRunsToSettleAsync();
            }

            wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;
            answered += await setup.ReadEverythingAsync(replayFirstCompleted: second);
            answered += await tenant.ReadEverythingAsync(replayFirstCompleted: second);

            logs.AddRange(host.Logs.Entries);
            seen.AppendLine(setup.Seen);
            seen.AppendLine(tenant.Seen);
        }

        answered.ShouldBeGreaterThan(80, "the read sweeps reached too few operations for an empty search to mean anything");
        wire.Requests.ShouldBeGreaterThanOrEqualTo(26, "the runs have to have reached the provider, or an empty search proves nothing");
        wire.Credentials.ShouldContain(credential => credential.Contains(SetupStem, StringComparison.Ordinal), "the provider was never sent the setup key");
        wire.Credentials.ShouldContain(credential => credential.Contains(TenantStem, StringComparison.Ordinal), "the provider was never sent the tenant's key");

        var dump = await database.DumpTextAsync();

        // The dump is not empty and holds what the host wrote: the NAME of the tenant's key, and the agents the runs used.
        dump.ShouldContain(TenantKeyName, Case.Sensitive, "the binding is not in the database, so the dump reads the wrong place");
        dump.ShouldContain(SetupAgent, Case.Sensitive, "the runs are not in the database");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = seen.ToString(),
            ["log lines"] = string.Join('\n', logs),
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
            ["every text, JSON and binary value of the database"] = dump,
        };

        SecretHits.Find(outputs, Secrets()).ShouldBeEmpty();
    }

    /// <summary>
    /// A connection that fails names the login and the server it tried and never the password it sent, in what a host
    /// throws when it starts, in what it answers and in what it logs.
    /// </summary>
    [Fact]
    public async Task A_connection_that_fails_is_told_without_its_password()
    {
        var database = await NewDatabaseAsync(PasswordStem + "-8c1e5d70");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();

        var seen = new StringBuilder();
        var failures = 0;

        (string Name, string ConnectionString, bool ApplyMigrations)[] attempts =
        [
            ("wrong password", database.WithPassword(WrongStem + "-4d90a2f1"), true),
            ("nothing listens, migrations on", database.Unreachable(UnreachableStem + "-b7036e58"), true),
            ("nothing listens, migrations off", database.Unreachable(UnreachableStem + "-b7036e58"), false),
        ];

        foreach (var (name, connectionString, applyMigrations) in attempts)
        {
            try
            {
                await using var host = await StartAsync(builder => database.Use(builder, connectionString, applyMigrations), wire, structured);
                var drive = new CanaryDrive(host);

                // A host that started with its store out of reach answers what it can, and each answer is kept.
                foreach (var path in (string[])[$"/tracon/api/agents/{SetupAgent}/run", "/tracon/api/runs", "/tracon/api/audit", "/tracon/api/diagnostics", "/tracon/api/tenants", "/tracon/api/meta"])
                {
                    var status = await drive.SendAsync(
                        path.EndsWith("/run", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get,
                        path,
                        path.EndsWith("/run", StringComparison.Ordinal) ? new { message = "hello" } : null,
                        idempotent: path.EndsWith("/run", StringComparison.Ordinal));

                    if ((int)status >= 500)
                    {
                        failures++;
                    }
                }

                seen.AppendLine(drive.Seen);
                seen.AppendLine(string.Join('\n', host.Logs.Entries));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The host refused to start: what it threw is what an operator is shown.
                failures++;
                seen.AppendLine(name + ": " + exception);
            }
        }

        failures.ShouldBeGreaterThanOrEqualTo(3, "a connection that fails has to fail, or an empty search proves nothing");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses, startup failures and log lines"] = seen.ToString(),
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        SecretHits.Find(outputs, Secrets()).ShouldBeEmpty();
    }

    /// <summary>
    /// A search that finds nothing proves nothing unless it can read the column a value is in: a value planted in a text,
    /// a JSON and a binary column of the schema is found in each.
    /// </summary>
    [Fact]
    public async Task The_search_of_the_database_reads_a_text_a_json_and_a_binary_column()
    {
        var database = await NewDatabaseAsync(PasswordStem + "-plant-1a2b");

        await using var wire = await CanaryWireServer.StartAsync();

        // A host migrates the schema when it starts; the table is planted in what it made.
        await using (var host = await StartAsync(database.Use, wire, new StructuredLogProbe()))
        {
            host.Services.ShouldNotBeNull();
        }

        await database.PlantAsync("canary-db-planted-value");

        var dump = await database.DumpTextAsync();

        foreach (var column in (string[])["a", "b", "c"])
        {
            dump.ShouldContain($"canary_plant.{column}=", Case.Sensitive, $"the dump did not read the column '{column}' of the planted row");
        }

        dump.Split('\n').Count(static line => line.Contains("canary-db-planted-value", StringComparison.Ordinal)).ShouldBe(3);
    }

    private static List<KeyValuePair<string, string>> Secrets()
        =>
        [
            new("setup provider key", SetupStem),
            new("tenant provider key", TenantStem),
            new("login password", PasswordStem),
            new("wrong password", WrongStem),
            new("unreachable server's password", UnreachableStem),
        ];

    private static Task<TraconTestHost> StartAsync(Action<ITraconBuilder> useDatabase, CanaryWireServer wire, StructuredLogProbe structured)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                useDatabase(builder);

                builder
                    .UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    })
                    .UseOpenAI(SetupStem + "-6e21c0b9", options => options.Endpoint = wire.BaseAddress)
                    .UseOpenAICompatible(TenantProvider, options => options.Endpoint = wire.BaseAddress)
                    .AddAgent(new AgentDefinition
                    {
                        Name = SetupAgent,
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = "openai", Model = "canary-model" },
                        Origin = AgentDefinitionOrigin.Code,
                    })
                    .AddAgent(new AgentDefinition
                    {
                        Name = TenantAgent,
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = TenantProvider, Model = "canary-model" },
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection([new(TenantKeyName, TenantStem + "-f4a7d3c2")])
                    .Build());
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = true;
                    options.Observability.SuccessSampleRatio = 1;
                    options.CircuitBreaker.FailureThreshold = 1000;
                });
            });
}
