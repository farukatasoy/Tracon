using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The pieces the distributed OAuth state tests share: a cache that records what is written to it,
/// a real content protector, a lease store, and intervals that run in milliseconds.
/// </summary>
internal static class McpOAuthTestKit
{
    /// <summary>The intervals the tests run with; the production values are seconds.</summary>
    public static readonly McpOAuthDistributedTiming FastTiming = new()
    {
        CallbackPoll = TimeSpan.FromMilliseconds(10),
        OutcomePoll = TimeSpan.FromMilliseconds(10),
        OutcomeWait = TimeSpan.FromSeconds(10),
        RefreshPoll = TimeSpan.FromMilliseconds(10),
        RefreshWait = TimeSpan.FromSeconds(10),
    };

    /// <summary>Builds the distributed backend over the given cache and lease store.</summary>
    /// <param name="cache">The shared cache.</param>
    /// <param name="leases">The lease store; an in-memory one when omitted.</param>
    /// <param name="protector">The protector; one with a fresh key when omitted.</param>
    /// <param name="timing">The timing; <see cref="FastTiming"/> when omitted.</param>
    /// <returns>The backend.</returns>
    public static McpOAuthDistributedBackend Backend(
        IDistributedCache cache,
        ISingletonLeaseStore? leases = null,
        IContentProtector? protector = null,
        McpOAuthDistributedTiming? timing = null)
        => new(
            cache,
            protector ?? Protector(),
            leases ?? new InMemorySingletonLeaseStore(),
            TimeSpan.FromDays(30),
            TimeProvider.System,
            timing ?? FastTiming);

    /// <summary>Builds the AES-GCM protector Tracon ships, over a fresh random key.</summary>
    /// <param name="keyId">The id of the key; two protectors with different ids cannot read each other's values.</param>
    /// <returns>The protector.</returns>
    public static AesGcmContentProtector Protector(string keyId = "k1")
    {
        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = keyId };
        options.Keys[keyId] = $"Keys:{keyId}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"Keys:{keyId}"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            })
            .Build();

        return new AesGcmContentProtector(new FixedMonitor(options), configuration);
    }

    /// <summary>Builds a token with distinctive text in every field, so a search of stored bytes can find it.</summary>
    /// <param name="obtainedAt">When the token was obtained.</param>
    /// <param name="expiresIn">The lifetime in seconds.</param>
    /// <param name="suffix">Text that makes the values of two tokens differ.</param>
    /// <returns>The token.</returns>
    public static TokenContainer Token(DateTimeOffset? obtainedAt = null, int? expiresIn = 3600, string suffix = "1")
        => new()
        {
            AccessToken = $"access-token-{suffix}-3f9a1c",
            RefreshToken = $"refresh-token-{suffix}-77be02",
            ExpiresIn = expiresIn,
            ObtainedAt = obtainedAt ?? DateTimeOffset.UtcNow,
            Scope = "read write",
            TokenType = "Bearer",
            AuthorizationServer = "https://auth.example.com",
            ClientId = "client-id-9d41",
            ClientSecret = $"client-secret-{suffix}-c0ffee",
            TokenEndpointAuthMethod = "client_secret_post",
        };

    /// <summary>Builds a server definition.</summary>
    /// <param name="name">The server name.</param>
    /// <param name="endpoint">The endpoint address.</param>
    /// <param name="clientId">The OAuth client id.</param>
    /// <returns>The definition.</returns>
    /// <param name="tenantId">The tenant the server belongs to.</param>
    /// <param name="enabled">Whether the server is switched on.</param>
    public static McpServerDefinition Server(
        string name = "prod",
        string endpoint = "https://mcp.example.com/mcp",
        string clientId = "client",
        string tenantId = "acme",
        bool enabled = true)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Endpoint = new Uri(endpoint),
            OAuthEnabled = true,
            OAuthClientId = clientId,
            Enabled = enabled,
        };

    /// <summary>Gets the bytes as text, with the bytes that are not text kept visible, for a search.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>Latin-1 text.</returns>
    public static string AsText(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private sealed class FixedMonitor(TraconContentProtectionOptions value) : IOptionsMonitor<TraconContentProtectionOptions>
    {
        public TraconContentProtectionOptions CurrentValue => value;

        public TraconContentProtectionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconContentProtectionOptions, string?> listener) => null;
    }
}

/// <summary>An <see cref="IDistributedCache"/> in a dictionary that records every write and can be made to fail.</summary>
internal sealed class RecordingDistributedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DistributedCacheEntryOptions> _options = new(StringComparer.Ordinal);
    private int _writes;

    /// <summary>Gets or sets a value indicating whether every call throws.</summary>
    public bool Failing { get; set; }

    /// <summary>Gets how many times a value was written.</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>Gets the keys that hold a value.</summary>
    public IReadOnlyCollection<string> Keys => [.. _entries.Keys];

    /// <summary>Gets the stored bytes of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The bytes; <see langword="null"/> when none are stored.</returns>
    public byte[]? Peek(string key) => _entries.TryGetValue(key, out var value) ? value : null;

    /// <summary>Gets the options a key was last written with.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The options.</returns>
    public DistributedCacheEntryOptions OptionsOf(string key) => _options[key];

    /// <summary>Writes bytes under a key as somebody with access to the cache could.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The bytes.</param>
    public void Plant(string key, byte[] value) => _entries[key] = value;

    /// <inheritdoc />
    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ThrowIfFailing();

        return Task.FromResult(_entries.TryGetValue(key, out var value) ? value.ToArray() : null);
    }

    /// <inheritdoc />
    public void Refresh(string key) => ThrowIfFailing();

    /// <inheritdoc />
    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        ThrowIfFailing();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        ThrowIfFailing();
        _entries.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        => SetAsync(key, value, options).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ThrowIfFailing();
        _entries[key] = value.ToArray();
        _options[key] = options;
        Interlocked.Increment(ref _writes);

        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (Failing)
        {
            throw new InvalidOperationException("The cache is unavailable.");
        }
    }
}

/// <summary>A lease store that records what each caller was told, so a test can wait for a refusal.</summary>
internal sealed class RecordingLeaseStore(ISingletonLeaseStore inner) : ISingletonLeaseStore
{
    private int _acquired;
    private int _refused;

    /// <summary>Gets how many acquisitions succeeded.</summary>
    public int Acquired => Volatile.Read(ref _acquired);

    /// <summary>Gets how many acquisitions were refused because another owner holds the lease.</summary>
    public int Refused => Volatile.Read(ref _refused);

    /// <inheritdoc />
    public async ValueTask<bool> TryAcquireAsync(string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var acquired = await inner.TryAcquireAsync(name, ownerId, duration, cancellationToken).ConfigureAwait(false);

        if (acquired)
        {
            Interlocked.Increment(ref _acquired);
        }
        else
        {
            Interlocked.Increment(ref _refused);
        }

        return acquired;
    }

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
        => inner.RenewAsync(name, ownerId, duration, cancellationToken);

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default)
        => inner.ReleaseAsync(name, ownerId, cancellationToken);
}

/// <summary>A logger factory that keeps every message and the level it was written at.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _messages = new();

    /// <summary>Gets what was logged.</summary>
    public IReadOnlyCollection<(LogLevel Level, string Message)> Messages => [.. _messages];

    /// <summary>Counts the messages of a level that contain a fragment.</summary>
    /// <param name="level">The level.</param>
    /// <param name="fragment">The fragment.</param>
    /// <returns>The count.</returns>
    public int Count(LogLevel level, string fragment)
        => _messages.Count(entry => entry.Level == level && entry.Message.Contains(fragment, StringComparison.Ordinal));

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Recorder(_messages);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Recorder(ConcurrentQueue<(LogLevel Level, string Message)> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Enqueue((logLevel, formatter(state, exception)));
    }
}
