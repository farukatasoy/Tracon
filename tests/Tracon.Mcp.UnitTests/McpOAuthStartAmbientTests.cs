using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The authorization flow of an MCP server runs on after the request that started it has answered. It must
/// not carry that request's ambient state (the tenant, the audit actor, the trace) along.
/// </summary>
public sealed class McpOAuthStartAmbientTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    [Fact]
    public async Task The_flow_that_outlives_the_request_does_not_inherit_the_requests_ambient_state()
    {
        var servers = new InMemoryMcpServerStore();
        await servers.SaveAsync(
            McpOAuthTestKit.Server(name: "prod", endpoint: "http://127.0.0.1:1/mcp", tenantId: "acme"),
            TestContext.Current.CancellationToken);

        var loggers = new AmbientRecordingLoggerFactory();
        var coordinator = NewCoordinator(servers, loggers);
        var seenBeforeStart = loggers.Seen.Count;

        Ambient.Value = "the request that started the flow";

        try
        {
            var started = await coordinator.StartAsync("acme", "prod", TestContext.Current.CancellationToken);

            // Nothing listens on the endpoint: the background attempt fails, which is the result this test needs, because
            // the attempt has run by then.
            started.Status.ShouldBe(McpOAuthOperationStatus.ConnectionFailed);
        }
        finally
        {
            Ambient.Value = null;
        }

        // What the flow itself asked a logger factory for, from the thread it ran on.
        var fromTheFlow = loggers.Seen.Skip(seenBeforeStart).ToList();

        fromTheFlow.ShouldNotBeEmpty("the flow built a transport and a client, which ask for loggers");
        fromTheFlow.ShouldAllBe(
            ambient => ambient == null,
            "the flow was started inside the request and inherited its ambient state");
    }

    private static McpOAuthAuthorizationCoordinator NewCoordinator(IMcpServerStore servers, ILoggerFactory loggers)
    {
        var mcpOptions = Options.Create(new TraconMcpOptions
        {
            OAuthCallbackBaseUri = new Uri("https://localhost/tracon/"),
            ConnectionTimeout = TimeSpan.FromSeconds(10),
        });

        var catalog = new McpToolCatalog(
            servers,
            new InMemoryTenantStore(),
            new EmptyConfiguration(),
            Options.Create(new TraconOptions()),
            mcpOptions,
            NullLoggerFactory.Instance,
            new McpOAuthTokenCacheRegistry(),
            new AllowAllToolAuthorizationHandler(),
            McpToolPolicyTestGates.Open(),
            NoOpToolArgumentsValidator.Instance,
            attribution: null);

        return new McpOAuthAuthorizationCoordinator(
            servers,
            new EmptyConfiguration(),
            mcpOptions,
            loggers,
            new McpOAuthTokenCacheRegistry(),
            catalog,
            McpOAuthDistributedBackend.Disabled,
            Options.Create(new TraconOptions()));
    }

    /// <summary>Notes the ambient value of the thread each time a logger is asked for.</summary>
    private sealed class AmbientRecordingLoggerFactory : ILoggerFactory
    {
        private readonly Lock _gate = new();
        private readonly List<string?> _seen = [];

        public IReadOnlyList<string?> Seen
        {
            get
            {
                lock (_gate)
                {
                    return [.. _seen];
                }
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            lock (_gate)
            {
                _seen.Add(Ambient.Value);
            }

            return NullLogger.Instance;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
