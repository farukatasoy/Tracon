using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Forces a tenant-, provider- and tool-set-aware cache key on top of
/// <see cref="DistributedCachingChatClient"/>, and gives a cached entry a
/// bounded lifetime.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Measured, not designed (2026-08-21).</strong>
/// <see cref="DistributedCachingChatClient.GetCacheKey"/> separates by
/// <c>ModelId</c>, <c>Temperature</c>, <c>ToolMode</c>,
/// <c>ChatOptions.Instructions</c> and the messages — but NOT by
/// <see cref="ChatOptions.Tools"/>, and not by tenant. Two agents in the same
/// tenant, with the same instructions but a DIFFERENT tool set, would land on
/// the same cache entry; a hit could carry a <c>FunctionCallContent</c> for a
/// tool the calling agent never registered. This is a tool-authorization gap,
/// not only a cache-precision one — <c>FunctionInvokingChatClient.TerminateOnUnknownCalls</c>
/// defaults to <see langword="false"/>, so the loop would not stop it.
/// </para>
/// <para>
/// The key is therefore forced with three additional inputs: the tenant, the
/// sorted list of tool names, and the provider name (the same model name can
/// be reused by an OpenAI-compatible endpoint under a different provider).
/// The agent name is deliberately left OUT: two agents in the same tenant
/// sharing instructions, tool set and model binding are behaviorally
/// identical, and sharing the entry is correct.
/// </para>
/// <para>
/// <see cref="DistributedCachingChatClient"/> does not expose a TTL: it
/// writes with an empty <see cref="DistributedCacheEntryOptions"/>, so an
/// entry would otherwise live as long as the store keeps it. This class keeps
/// its own reference to the SAME <see cref="IDistributedCache"/> the base
/// class holds privately, and overrides the write path to pair a bounded
/// <see cref="DistributedCacheEntryOptions.AbsoluteExpirationRelativeToNow"/>
/// with the exact JSON encoding the (non-overridden) base read path expects.
/// </para>
/// </remarks>
internal sealed class TraconResponseCachingChatClient : DistributedCachingChatClient
{
    private readonly IDistributedCache _storage;
    private readonly string _provider;
    private readonly string? _tenantId;
    private readonly ResponseCacheSettings _settings;
    private readonly TraconMetrics? _metrics;
    private readonly ILogger? _logger;

    /// <summary>Creates a new caching ring.</summary>
    /// <param name="innerClient">The client that answers on a cache miss.</param>
    /// <param name="storage">The backing distributed cache.</param>
    /// <param name="provider">The model provider name; part of the cache key.</param>
    /// <param name="tenantId">The tenant; part of the cache key. <see langword="null"/> in a single-tenant setup.</param>
    /// <param name="settings">The cache settings, including the entry lifetime.</param>
    /// <param name="metrics">Optional metrics sink for cache hit/miss counting.</param>
    /// <param name="logger">Optional logger for cache read/write failures.</param>
    public TraconResponseCachingChatClient(
        IChatClient innerClient,
        IDistributedCache storage,
        string provider,
        string? tenantId,
        ResponseCacheSettings settings,
        TraconMetrics? metrics = null,
        ILogger? logger = null)
        : base(innerClient, storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(settings);

        _storage = storage;
        _provider = provider;
        _tenantId = tenantId;
        _settings = settings;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override string GetCacheKey(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        params ReadOnlySpan<object?> additionalValues)
    {
        var baseKey = base.GetCacheKey(messages, options, additionalValues);

        var toolNames = options?.Tools is { Count: > 0 } tools
            ? string.Join(' ', tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal))
            : string.Empty;

        var scope = new CacheKeyScope(baseKey, _tenantId ?? DefaultTenantId, _provider, toolNames);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.ToString())));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A store failure is treated as a miss, not rethrown: the cache is an
    /// optimization, not a dependency, and the real model still answers
    /// (the same "observability never breaks functionality" rule the run
    /// recording path follows).
    /// </para>
    /// <para>
    /// The stored <see cref="ChatResponse"/> carries the ORIGINAL call's
    /// <see cref="UsageContent"/> and <see cref="ChatResponse.Usage"/>. Serving
    /// them again on a hit would double-report tokens/cost the provider was never
    /// billed for a second time - the run's own usage aggregation has no other
    /// way to tell "replayed" apart from "just billed". <see cref="StripUsage(ChatResponse)"/>
    /// removes them from the object being returned; the STORED bytes are
    /// untouched; a future hit strips the same way from a fresh deserialize.
    /// </para>
    /// </remarks>
    protected override async Task<ChatResponse?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        JsonSerializerOptions.MakeReadOnly();

        ChatResponse? result = null;

        try
        {
            if (await _storage.GetAsync(key, cancellationToken).ConfigureAwait(false) is byte[] existingJson)
            {
                result = (ChatResponse?)JsonSerializer.Deserialize(existingJson, JsonSerializerOptions.GetTypeInfo(typeof(ChatResponse)));

                if (result is not null)
                {
                    StripUsage(result);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Response-cache read failed for provider '{Provider}'; treated as a miss.", _provider);
        }

        _metrics?.RecordModelCacheLookup(_provider, _tenantId ?? DefaultTenantId, hit: result is not null);

        return result;
    }

    /// <inheritdoc />
    /// <remarks>Same fail-open and usage-stripping rationale as <see cref="ReadCacheAsync"/>.</remarks>
    protected override async Task<IReadOnlyList<ChatResponseUpdate>?> ReadCacheStreamingAsync(string key, CancellationToken cancellationToken)
    {
        JsonSerializerOptions.MakeReadOnly();

        IReadOnlyList<ChatResponseUpdate>? result = null;

        try
        {
            if (await _storage.GetAsync(key, cancellationToken).ConfigureAwait(false) is byte[] existingJson)
            {
                result = (IReadOnlyList<ChatResponseUpdate>?)JsonSerializer.Deserialize(
                    existingJson,
                    JsonSerializerOptions.GetTypeInfo(typeof(IReadOnlyList<ChatResponseUpdate>)));

                if (result is not null)
                {
                    StripUsage(result);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Response-cache read failed for provider '{Provider}'; treated as a miss.", _provider);
        }

        _metrics?.RecordModelCacheLookup(_provider, _tenantId ?? DefaultTenantId, hit: result is not null);

        return result;
    }

    /// <summary>Removes the aggregate usage and every per-message usage content a cache hit would otherwise re-report.</summary>
    private static void StripUsage(ChatResponse response)
    {
        response.Usage = null;

        foreach (var message in response.Messages)
        {
            if (message.Contents.Any(static content => content is UsageContent))
            {
                message.Contents = [.. message.Contents.Where(static content => content is not UsageContent)];
            }
        }
    }

    /// <summary>Removes the per-frame usage content a cache hit would otherwise re-report.</summary>
    private static void StripUsage(IReadOnlyList<ChatResponseUpdate> updates)
    {
        foreach (var update in updates)
        {
            if (update.Contents.Any(static content => content is UsageContent))
            {
                update.Contents = [.. update.Contents.Where(static content => content is not UsageContent)];
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A write failure (a store outage, a size limit rejected the payload) is
    /// logged and swallowed, never rethrown: the model call this write
    /// follows already succeeded, and a cache-write problem must not turn
    /// that success into a failed run.
    /// </remarks>
    protected override async Task WriteCacheAsync(string key, ChatResponse value, CancellationToken cancellationToken)
    {
        JsonSerializerOptions.MakeReadOnly();

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.GetTypeInfo(typeof(ChatResponse)));

            await _storage.SetAsync(key, bytes, BuildEntryOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Response-cache write failed for provider '{Provider}'; the response was still returned.", _provider);
        }
    }

    /// <inheritdoc />
    /// <remarks>Same fail-open rationale as <see cref="WriteCacheAsync"/>.</remarks>
    protected override async Task WriteCacheStreamingAsync(string key, IReadOnlyList<ChatResponseUpdate> value, CancellationToken cancellationToken)
    {
        JsonSerializerOptions.MakeReadOnly();

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.GetTypeInfo(typeof(IReadOnlyList<ChatResponseUpdate>)));

            await _storage.SetAsync(key, bytes, BuildEntryOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Response-cache write failed for provider '{Provider}'; the response was still returned.", _provider);
        }
    }

    private DistributedCacheEntryOptions BuildEntryOptions()
        => new() { AbsoluteExpirationRelativeToNow = _settings.Lifetime };

    private const string DefaultTenantId = "default";

    // A record's compiler-generated ToString prints every field labeled and
    // distinct (`CacheKeyScope { BaseKey = ..., TenantId = ..., ... }`) —
    // there is no hand-rolled separator between DIFFERENT fields for an
    // invisible character to hide in (K-525).
    private readonly record struct CacheKeyScope(string BaseKey, string TenantId, string Provider, string ToolNames);
}
