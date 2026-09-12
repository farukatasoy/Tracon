using Microsoft.Extensions.AI;

namespace Tracon.Testing;

/// <summary>A single request that reached the fake provider.</summary>
public sealed record FakeModelRequest
{
    /// <summary>Message history in the request.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Options that came with the request. Also carries tool definitions.</summary>
    public ChatOptions? Options { get; init; }

    /// <summary>Whether this was a streaming call.</summary>
    public bool IsStreaming { get; init; }
}
