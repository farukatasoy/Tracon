using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Which key a canary host sends to the provider.</summary>
public enum CanaryKeyKind
{
    /// <summary>The key set in code at startup: every tenant shares it.</summary>
    Setup,

    /// <summary>A key a tenant binds to the provider through the management API.</summary>
    Tenant,
}

/// <summary>
/// A host for the canary cells that drive one agent, on SQLite, against <see cref="CanaryWireServer"/>, with either the
/// startup key or a tenant's own key, and everything a search needs to read afterwards.
/// </summary>
/// <remarks>
/// <para>
/// The provider is <c>openai</c> (Chat Completions) for the startup key and an OpenAI-compatible provider for the tenant
/// key; both speak the wire the server answers with a tool call. Each cell gives its own <c>scenario</c>, so the key it
/// searches for is its own and a probe that listens to the whole process cannot meet another class's key.
/// </para>
/// </remarks>
internal sealed class KeyedCanaryHost : IAsyncDisposable
{
    /// <summary>The agent the host registers.</summary>
    public const string AgentName = "keyed-agent";

    /// <summary>The tool the agent owns; the wire asks for it by this name.</summary>
    public const string ToolName = "canary_tool";

    private const string Tenant = "tenant-a";
    private const string TenantProvider = "canary-provider";
    private const string TenantKeyName = "Tracon:ProviderKeys:tenant-a:Canary";

    private readonly CanaryKeyKind _kind;

    private KeyedCanaryHost(CanaryKeyKind kind, string scenario, TraconTestHost host, CanaryWireServer wire, StructuredLogProbe structured, TempSqliteDatabase database)
    {
        _kind = kind;
        Scenario = scenario;
        Host = host;
        Wire = wire;
        Structured = structured;
        Database = database;
        Probe = new LeakProbe();
        Drive = new CanaryDrive(host, kind is CanaryKeyKind.Tenant ? Tenant : null);
    }

    /// <summary>Gets the scenario name the keys are built from.</summary>
    public string Scenario { get; }

    /// <summary>Gets the host.</summary>
    public TraconTestHost Host { get; }

    /// <summary>Gets the provider.</summary>
    public CanaryWireServer Wire { get; }

    /// <summary>Gets the structured log probe the host's logging writes to.</summary>
    public StructuredLogProbe Structured { get; }

    /// <summary>Gets the span and metric probe.</summary>
    public LeakProbe Probe { get; }

    /// <summary>Gets the database file of the host.</summary>
    public TempSqliteDatabase Database { get; }

    /// <summary>Gets the drive that sends the requests as the key's tenant.</summary>
    public CanaryDrive Drive { get; }

    /// <summary>Creates a second drive for the same tenant, whose record is kept apart from <see cref="Drive"/>'s.</summary>
    /// <returns>The drive.</returns>
    public CanaryDrive NewDrive() => new(Host, _kind is CanaryKeyKind.Tenant ? Tenant : null);

    /// <summary>Gets the part of the key that identifies it.</summary>
    public string Stem => StemOf(_kind, Scenario);

    /// <summary>Starts a host.</summary>
    /// <param name="kind">Which key the host sends.</param>
    /// <param name="scenario">The cell's name; part of the key.</param>
    /// <param name="database">The database of the host.</param>
    /// <param name="configure">Adds to the host (tools, extra agents) after the provider is registered.</param>
    /// <param name="configureServices">Adds to the host's services.</param>
    /// <param name="recordSensitiveData">Whether the host records the content of prompts, results and failures in its traces.</param>
    /// <returns>The running host, with the tenant's binding already written for <see cref="CanaryKeyKind.Tenant"/>.</returns>
    public static async Task<KeyedCanaryHost> StartAsync(
        CanaryKeyKind kind,
        string scenario,
        TempSqliteDatabase database,
        Action<ITraconBuilder>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        bool recordSensitiveData = true)
    {
        var wire = await CanaryWireServer.StartAsync();
        var structured = new StructuredLogProbe();
        var key = KeyOf(kind, scenario);
        var provider = kind is CanaryKeyKind.Setup ? "openai" : TenantProvider;

        var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);

                if (kind is CanaryKeyKind.Tenant)
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
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = provider, Model = "canary-model" },
                    ToolNames = [ToolName],
                    Origin = AgentDefinitionOrigin.Code,
                });

                configure?.Invoke(builder);
            },
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                if (kind is CanaryKeyKind.Tenant)
                {
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(TenantKeyName, key)]).Build());
                }

                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(options =>
                {
                    options.Observability.RecordSensitiveData = recordSensitiveData;
                    options.Observability.SuccessSampleRatio = 1;
                    options.CircuitBreaker.FailureThreshold = 1000;
                });

                configureServices?.Invoke(services);
            });

        var started = new KeyedCanaryHost(kind, scenario, host, wire, structured, database);

        if (kind is CanaryKeyKind.Tenant)
        {
            (await started.Drive.SendAsync(
                HttpMethod.Put,
                $"/tracon/api/tenants/{Tenant}/providers/{TenantProvider}",
                new { apiKeyConfigurationName = TenantKeyName, endpoint = wire.BaseAddress.ToString() })).ShouldBe(HttpStatusCode.OK);
        }

        return started;
    }

    /// <summary>The part of a key that identifies its slot.</summary>
    /// <param name="kind">The kind of key.</param>
    /// <param name="scenario">The cell's name.</param>
    /// <returns>The stem.</returns>
    public static string StemOf(CanaryKeyKind kind, string scenario) => $"canary-{scenario}-{kind.ToString().ToLowerInvariant()}-key";

    /// <summary>The whole key.</summary>
    /// <param name="kind">The kind of key.</param>
    /// <param name="scenario">The cell's name.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(CanaryKeyKind kind, string scenario) => StemOf(kind, scenario) + "-9a41c7d2e60b83f5";

    /// <summary>Collects every output a search reads, in the shape the canary classes use.</summary>
    /// <param name="extra">Further outputs the cell has, by name.</param>
    /// <returns>The outputs, by name.</returns>
    public Dictionary<string, string> Outputs(IEnumerable<KeyValuePair<string, string>>? extra = null)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = Drive.Seen,
            ["log lines"] = string.Join('\n', Host.Logs.Entries),
            ["log records (state, scopes, exception parts)"] = Structured.Text,
            ["span names, tags, baggage and events"] = Probe.Spans,
            ["metric tags"] = Probe.Measurements,
        };

        foreach (var (name, text) in extra ?? [])
        {
            outputs[name] = text;
        }

        return outputs;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Probe.Dispose();
        await Host.DisposeAsync();
        await Wire.DisposeAsync();
    }
}
