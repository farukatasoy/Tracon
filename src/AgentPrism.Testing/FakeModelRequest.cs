using Microsoft.Extensions.AI;

namespace AgentPrism.Testing;

/// <summary>Sahte saglayiciya ulasan tek bir istek.</summary>
public sealed record FakeModelRequest
{
    /// <summary>Istekteki mesaj gecmisi.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Istekle birlikte gelen secenekler. Tool tanimlarini da tasir.</summary>
    public ChatOptions? Options { get; init; }

    /// <summary>Akisli bir cagri mi.</summary>
    public bool IsStreaming { get; init; }
}
