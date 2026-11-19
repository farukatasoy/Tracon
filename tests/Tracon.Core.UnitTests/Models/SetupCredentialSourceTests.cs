using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The pair a provider sends is read from the configuration keys the host named, once per call, and a value that cannot be
/// used leaves the last valid one in place.
/// </summary>
public sealed class SetupCredentialSourceTests
{
    private const string KeyName = "Platform:Provider:Key";
    private const string EndpointName = "Platform:Provider:Endpoint";

    [Fact]
    public void The_startup_values_are_the_first_pair_and_carry_the_setup_scope()
    {
        var source = Source(startupKey: "startup-key-0001", startupEndpoint: "https://one.example/v1/", keyName: KeyName, endpointName: EndpointName, configuration: Configuration());

        var snapshot = source.Current();

        snapshot.ApiKey.ShouldBe("startup-key-0001");
        snapshot.Endpoint.ShouldBe("https://one.example/v1/");
        TenantCredentialScope.IsSetup(snapshot).ShouldBeTrue();
        snapshot.BindingScope.ShouldBe("setup:acme");
        source.RecentKeys.ShouldBe(["startup-key-0001"]);
    }

    [Fact]
    public void A_value_the_names_hold_replaces_the_startup_pair_and_the_keys_are_listed_newest_first()
    {
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, "https://one.example/v1/"));
        var source = Source("startup-key-0001", "https://start.example/", KeyName, EndpointName, configuration);

        source.Current().ApiKey.ShouldBe("key-one-0001");

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = "https://two.example/v1/";

        var second = source.Current();

        second.ApiKey.ShouldBe("key-two-0002");
        second.Endpoint.ShouldBe("https://two.example/v1/");
        source.RecentKeys.ShouldBe(["key-two-0002", "key-one-0001", "startup-key-0001"]);
    }

    [Fact]
    public void The_key_and_the_address_come_from_one_read()
    {
        // A configuration whose second read differs from its first: a source that read the address after the key would
        // hand out the old key with the new address. One Current() call must show one pair.
        var configuration = new FlippingConfiguration((KeyName, ["key-one-0001", "key-two-0002"]), (EndpointName, ["https://one.example/", "https://two.example/"]));
        var source = Source(null, null, KeyName, EndpointName, configuration);

        var snapshot = source.Current();

        (snapshot.ApiKey, snapshot.Endpoint).ShouldBeOneOf(
            ("key-one-0001", "https://one.example/"),
            ("key-two-0002", "https://two.example/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("key\nwith a line break")]
    public void A_key_that_is_missing_blank_or_unusable_leaves_the_last_valid_key_and_warns_once(string? bad)
    {
        var configuration = Configuration((KeyName, "key-one-0001"));
        using var logs = new Logs();
        var source = Source(null, null, KeyName, null, configuration, logs.Logger);

        source.Current().ApiKey.ShouldBe("key-one-0001");

        configuration[KeyName] = bad;

        source.Current().ApiKey.ShouldBe("key-one-0001");
        source.Current().ApiKey.ShouldBe("key-one-0001");
        source.Current().ApiKey.ShouldBe("key-one-0001");

        logs.Messages.Count(static message => message.Contains(KeyName, StringComparison.Ordinal)).ShouldBe(1);
        logs.Messages.ShouldAllBe(static message => !message.Contains("with a line break", StringComparison.Ordinal));

        configuration[KeyName] = "key-two-0002";

        source.Current().ApiKey.ShouldBe("key-two-0002");
    }

    [Fact]
    public void An_address_that_is_not_absolute_leaves_the_last_valid_address()
    {
        var configuration = Configuration((EndpointName, "https://one.example/v1/"));
        var source = Source("startup-key-0001", null, null, EndpointName, configuration);

        source.Current().Endpoint.ShouldBe("https://one.example/v1/");

        configuration[EndpointName] = "not an address";

        source.Current().Endpoint.ShouldBe("https://one.example/v1/");
    }

    [Fact]
    public void A_configuration_that_throws_leaves_the_last_valid_pair_and_the_text_of_the_exception_stays_out_of_the_log()
    {
        using var logs = new Logs();
        var source = Source("startup-key-0001", null, KeyName, null, new ThrowingConfiguration("secret-text-the-store-wrote"), logs.Logger);

        source.Current().ApiKey.ShouldBe("startup-key-0001");

        logs.Messages.ShouldAllBe(static message => !message.Contains("secret-text-the-store-wrote", StringComparison.Ordinal));
        logs.Messages.ShouldContain(static message => message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    [Fact]
    public void A_keyless_provider_carries_the_placeholder_and_reports_no_key_to_mask()
    {
        var source = Source(null, "https://local.example/v1/", null, EndpointName, Configuration(), keyless: true);

        var snapshot = source.Current();

        snapshot.ApiKey.ShouldBe(SetupCredentialSource.KeylessPlaceholder);
        source.RecentKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_newest_keys_are_kept()
    {
        var configuration = Configuration((KeyName, "key-00000001"));
        var source = Source(null, null, KeyName, null, configuration);

        for (var index = 1; index <= 8; index++)
        {
            configuration[KeyName] = $"key-0000000{index}";
            source.Current();
        }

        source.RecentKeys.Count.ShouldBe(4);
        source.RecentKeys[0].ShouldBe("key-00000008");
    }

    [Fact]
    public async Task A_live_mask_asks_for_the_keys_again_when_a_failure_is_written()
    {
        var configuration = Configuration((KeyName, "canary-key-one-aaaa1111"));
        var source = Source(null, null, KeyName, null, configuration);

        // The live setup reads the pair when the host starts (its first factory is built from it).
        _ = source.Current();

        var provider = new RotatingProvider(source);
        var registry = new ModelProviderRegistry([provider]);

        using var client = registry.CreateChatClient(new ModelBinding { Provider = "acme", Model = "m" });

        // The pipeline is built here, with the first key. The key rotates, and the call that follows is sent with the new
        // one: its failure quotes the new key, and the mask built with the pipeline has never seen it.
        configuration[KeyName] = "canary-key-two-bbbb2222";
        provider.Failure = new InvalidOperationException("401 Incorrect API key provided: Bearer canary-key-two-bbbb2222.");

        var thrown = await Should.ThrowAsync<ProviderInvocationException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));

        thrown.ToString().ShouldNotContain("canary-key-two-bbbb2222");
        thrown.ToString().ShouldContain("Bearer [redacted].");

        // A call that began under the first key and fails after the rotation is masked as well.
        provider.Failure = new InvalidOperationException("401 Incorrect API key provided: Bearer canary-key-one-aaaa1111.");

        var older = await Should.ThrowAsync<ProviderInvocationException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));

        older.ToString().ShouldNotContain("canary-key-one-aaaa1111");
    }

    private static SetupCredentialSource Source(
        string? startupKey,
        string? startupEndpoint,
        string? keyName,
        string? endpointName,
        IConfiguration? configuration,
        ILogger? logger = null,
        bool keyless = false)
        => new(
            "acme",
            "AcmeProviderOptions.ApiKey",
            startupKey,
            startupEndpoint is null ? null : new Uri(startupEndpoint),
            keyName,
            endpointName,
            configuration,
            logger,
            keyless);

    private static IConfiguration Configuration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private sealed class RotatingProvider(SetupCredentialSource source) : IModelProvider, ILiveSetupModelProvider
    {
        public string Name => "acme";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public Exception? Failure { get; set; }

        public bool SetupKeysChange => true;

        public IReadOnlyList<string> SetupApiKeys => source.RecentKeys;

        public IChatClient CreateChatClient(ModelBinding binding)
            => new ThrowingClient(() =>
            {
                // The call reads the key it sends when it starts, as a real client does, and then the service refuses it.
                _ = source.Current();

                return Failure!;
            });
    }

    private sealed class ThrowingClient(Func<Exception> failure) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw failure();

#pragma warning disable CS1998
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            throw failure();
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
#pragma warning restore CS1998

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class Logs : IDisposable
    {
        private readonly List<string> _messages = [];
        private readonly ILoggerFactory _factory;

        public Logs()
        {
            _factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(new Provider(_messages)));
            Logger = _factory.CreateLogger("test");
        }

        public ILogger Logger { get; }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public void Dispose() => _factory.Dispose();

        private sealed class Provider(List<string> messages) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new Recorder(messages);

            public void Dispose()
            {
            }
        }

        private sealed class Recorder(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception) + (exception is null ? string.Empty : "|" + exception.GetType().Name));
                }
            }
        }
    }

    /// <summary>A configuration whose reads differ from one read to the next.</summary>
    private sealed class FlippingConfiguration((string Name, string[] Values) key, (string Name, string[] Values) endpoint) : IConfiguration
    {
        private int _reads;

        public string? this[string name]
        {
            get => ValuesOf(name) is { } values ? values[Math.Min(Interlocked.Increment(ref _reads) - 1, values.Length - 1) % values.Length] : null;
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string name) => throw new NotSupportedException();

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        private string[]? ValuesOf(string name)
            => string.Equals(name, key.Name, StringComparison.Ordinal) ? key.Values
                : string.Equals(name, endpoint.Name, StringComparison.Ordinal) ? endpoint.Values
                : null;
    }

    private sealed class ThrowingConfiguration(string text) : IConfiguration
    {
        public string? this[string name]
        {
            get => throw new InvalidOperationException(text);
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string name) => throw new NotSupportedException();

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    }
}
