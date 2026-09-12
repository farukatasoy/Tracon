using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Benchmarks;

/// <summary>
/// A minimal concrete <see cref="AIAgent"/> used only as an identity object in
/// <see cref="CompiledAgentCacheBenchmarks"/> - the cache stores and returns
/// the reference, it never calls any member below.
/// </summary>
internal sealed class PlaceholderAgent : AIAgent
{
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Benchmark placeholder - never actually run.");

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Benchmark placeholder - never actually run.");

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Benchmark placeholder - never actually run.");

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Benchmark placeholder - never actually run.");

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Benchmark placeholder - never actually run.");
}
