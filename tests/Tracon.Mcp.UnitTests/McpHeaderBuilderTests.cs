using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The one builder of an MCP server's request headers: stored plain headers
/// plus credential headers resolved from configuration (phase 190).
/// </summary>
/// <remarks>
/// Every log line is checked for the value: a warning that names a header
/// must never carry what the header holds.
/// </remarks>
public sealed class McpHeaderBuilderTests
{
    private const string Value = "resolved-value-190";

    private static readonly McpKeySpace KeySpace = new("Tracon:McpSecrets:", "default");

    [Fact]
    public void Plain_and_resolved_headers_are_merged()
    {
        var headers = Build(Server(
            headers: new(StringComparer.Ordinal) { ["X-Team"] = "platform" },
            keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey" }));

        headers["X-Team"].ShouldBe("platform");
        headers["x-api-key"].ShouldBe(Value);
    }

    [Fact]
    public void A_header_declared_by_key_wins_over_a_plain_one()
    {
        var headers = Build(Server(
            headers: new(StringComparer.Ordinal) { ["X-Api-Key"] = "stale-plain" },
            keys: new(StringComparer.Ordinal) { ["x-api-key"] = "Tracon:McpSecrets:SearchKey" }));

        headers.Count.ShouldBe(1);
        headers["X-Api-Key"].ShouldBe(Value);
    }

    /// <summary>
    /// A stored map is case-sensitive; the copy constructor threw on
    /// <c>X-A</c> next to <c>x-a</c> and the server never connected.
    /// </summary>
    [Fact]
    public void Plain_names_that_differ_only_in_case_do_not_throw()
    {
        var logger = new RecordingLogger();

        var headers = Build(Server(headers: new(StringComparer.Ordinal) { ["X-Team"] = "a", ["x-team"] = "b" }), logger);

        headers.Count.ShouldBe(1);
        logger.Text.ShouldContain("more than once");
    }

    /// <summary>
    /// A row written before phase 190 keeps working (decision 7), and the
    /// operator is told to move the credential — by name.
    /// </summary>
    [Fact]
    public void A_stored_plain_credential_is_sent_with_a_warning_that_carries_no_value()
    {
        var logger = new RecordingLogger();

        var headers = Build(Server(headers: new(StringComparer.Ordinal) { ["X-Api-Key"] = "legacy-plain-190" }), logger);

        headers["X-Api-Key"].ShouldBe("legacy-plain-190");
        logger.Text.ShouldContain("X-Api-Key");
        logger.Text.ShouldContain("headerConfigurationKeys");
        logger.Text.ShouldNotContain("legacy-plain-190");
    }

    [Fact]
    public void Under_oauth_a_keyed_authorization_is_not_sent_but_a_plain_one_is()
    {
        var logger = new RecordingLogger();

        var keyed = Build(Server(oauth: true, keys: new(StringComparer.Ordinal) { ["Authorization"] = "Tracon:McpSecrets:Bearer" }), logger);

        keyed.ContainsKey("Authorization").ShouldBeFalse();
        logger.Text.ShouldContain("OAuth");

        // Decision 7: the plain header is still sent; the SDK then skips its
        // own bearer token while the header is present (measured in phase 190).
        var plain = Build(Server(oauth: true, headers: new(StringComparer.Ordinal) { ["Authorization"] = "Bearer plain-190" }), logger);

        plain["Authorization"].ShouldBe("Bearer plain-190");
        logger.Text.ShouldNotContain("plain-190");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_resolved_value_is_not_sent(string? configured)
    {
        var logger = new RecordingLogger();

        var headers = Build(
            Server(keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey" }),
            logger,
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:McpSecrets:SearchKey"] = configured });

        headers.ShouldBeEmpty();
        logger.Text.ShouldContain("Tracon:McpSecrets:SearchKey");
    }

    /// <summary>
    /// The transport throws on a header it cannot add, with the VALUE in the
    /// message, and the connection path logs that exception. The builder must
    /// keep such a header out of the transport.
    /// </summary>
    [Theory]
    [InlineData("Content-Type", Value)]
    [InlineData("X-Api-Key", "line\r\nbreak-190")]
    public void A_header_the_transport_cannot_add_is_dropped_without_logging_its_value(string header, string configured)
    {
        var logger = new RecordingLogger();

        var headers = Build(
            Server(keys: new(StringComparer.Ordinal) { [header] = "Tracon:McpSecrets:SearchKey" }),
            logger,
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:McpSecrets:SearchKey"] = configured });

        headers.ShouldBeEmpty();
        logger.Text.ShouldContain(header);
        logger.Text.ShouldNotContain(Value);
        logger.Text.ShouldNotContain("break-190");
    }

    [Fact]
    public void A_key_outside_the_tenant_throws_before_any_value_is_read()
    {
        var configuration = new ReadRecordingConfiguration();

        var server = Server(keys: new(StringComparer.Ordinal)
        {
            ["X-Team-Key"] = "Tracon:McpSecrets:SearchKey",
            ["X-Api-Key"] = "Tracon:McpSecrets:globex:SearchKey",
        });

        var exception = Should.Throw<TraconException>(
            () => McpHeaderBuilder.Build(server, configuration, KeySpace, new RecordingLogger()));

        exception.Message.ShouldContain("headerConfigurationKeys[X-Api-Key]");
        configuration.Reads.ShouldBeEmpty("no key may be read while any key of the record is outside its tenant");
    }

    [Fact]
    public void The_deprecated_field_still_resolves()
    {
#pragma warning disable CS0618 // The deprecated field is resolved until 1.0.0.
        var server = Server() with { AuthorizationConfigurationKey = "Tracon:McpSecrets:Legacy" };
#pragma warning restore CS0618

        Build(server, configured: new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:McpSecrets:Legacy"] = "Bearer legacy" })
            ["Authorization"].ShouldBe("Bearer legacy");
    }

    [Fact]
    public void The_fingerprint_carries_key_names_and_never_resolved_values()
    {
        var first = Server(keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey" });
        var renamed = Server(keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = "Tracon:McpSecrets:OtherKey" });

        var fingerprint = McpConnection.ComputeFingerprint(first);

        fingerprint.ShouldContain("X-Api-Key=Tracon:McpSecrets:SearchKey");
        fingerprint.ShouldNotContain(Value);
        string.Equals(McpConnection.ComputeFingerprint(renamed), fingerprint, StringComparison.Ordinal).ShouldBeFalse("a new key name must reconnect");
    }

    /// <summary>
    /// 🚨 One reader of a server's headers. The OAuth coordinator used to copy
    /// <c>server.Headers</c> on its own; a copy that survives misses the
    /// resolved credential headers on that path.
    /// </summary>
    [Fact]
    public void Only_the_builder_and_the_fingerprint_read_a_servers_headers()
    {
        var source = Path.Combine(FindRepositoryRoot(), "src", "Tracon.Mcp");

        var readers = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(static file => File.ReadAllText(file).Contains("server.Headers", StringComparison.Ordinal))
            .Select(static file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal)
            .ToList();

        readers.ShouldBe(["McpConnection.cs", "McpHeaderBuilder.cs"]);
        File.ReadAllLines(Path.Combine(source, "Internal", "McpConnection.cs"))
            .Count(static line => line.Contains("server.Headers", StringComparison.Ordinal))
            .ShouldBe(1, "McpConnection may read the headers only for the fingerprint");
    }

    private static Dictionary<string, string> Build(
        McpServerDefinition server,
        RecordingLogger? logger = null,
        Dictionary<string, string?>? configured = null)
        => McpHeaderBuilder.Build(
            server,
            new ConfigurationBuilder()
                .AddInMemoryCollection(configured ?? new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Tracon:McpSecrets:SearchKey"] = Value,
                    ["Tracon:McpSecrets:Bearer"] = "Bearer " + Value,
                })
                .Build(),
            KeySpace,
            logger ?? new RecordingLogger());

    private static McpServerDefinition Server(
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? keys = null,
        bool oauth = false)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = "default",
            Name = "m1",
            Endpoint = new Uri("https://mcp.example.com/mcp"),
            Headers = headers ?? new Dictionary<string, string>(StringComparer.Ordinal),
            HeaderConfigurationKeys = keys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            OAuthEnabled = oauth,
            OAuthClientId = oauth ? "client" : null,
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root not found above '{AppContext.BaseDirectory}'.");
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Text => string.Join('\n', _lines);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _lines.Enqueue($"{logLevel} {formatter(state, exception)} {exception}");
    }

    private sealed class ReadRecordingConfiguration : IConfiguration
    {
        public List<string> Reads { get; } = [];

        public string? this[string key]
        {
            get
            {
                Reads.Add(key);
                return Value;
            }

            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
