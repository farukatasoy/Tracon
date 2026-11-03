using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// An agent that is only an identity: it carries the key it was compiled for and counts how often it is disposed.
/// </summary>
/// <remarks>
/// The cache stores and returns the reference and never calls a run member, so every run member throws. The tag
/// is what lets a test say "this is the agent compiled for tenant A", and the dispose count is what shows the
/// cache never disposes an agent that a run may still hold.
/// </remarks>
internal sealed class TaggedAgent(string tag) : AIAgent, IDisposable
{
    private int _disposed;

    /// <summary>Gets what the agent was compiled for.</summary>
    public string Tag { get; } = tag;

    /// <summary>Gets how often <see cref="Dispose"/> was called.</summary>
    public int DisposeCount => Volatile.Read(ref _disposed);

    /// <inheritdoc />
    public void Dispose() => Interlocked.Increment(ref _disposed);

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("An identity object, never run.");

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("An identity object, never run.");

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("An identity object, never run.");

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("An identity object, never run.");

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("An identity object, never run.");
}
