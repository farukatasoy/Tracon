using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The optional health-check contract for a model provider.
/// </summary>
/// <remarks>
/// <para>
/// A member is <strong>not added</strong> to the <see cref="IModelProvider"/>
/// interface — that would break the consumer's own <see cref="IModelProvider"/>
/// implementation. This separate interface is an optional extension point: if
/// a provider does not implement it, its status is
/// <see cref="ModelProviderHealthStatus.Unknown"/>, and that is not an error.
/// See <c>docs/KARARLAR.md</c>, decision K4 (adding a member to an existing
/// interface breaks the consumer's implementation), for the rationale.
/// </para>
/// <para>
/// The check <strong>must not make a billed model call</strong>. OpenAI and
/// compatible servers offer the <c>GET {endpoint}/models</c> endpoint; this
/// endpoint returns model names and produces no charge.
/// </para>
/// </remarks>
public interface IModelProviderHealthCheck
{
    /// <summary>Checks the provider's reachability.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The check result.</returns>
    ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>A model provider's status at the last check.</summary>
public sealed record ModelProviderHealth
{
    /// <summary>The provider name.</summary>
    public required string ProviderName { get; init; }

    /// <summary>The check result.</summary>
    public required ModelProviderHealthStatus Status { get; init; }

    /// <summary>
    /// A short failure reason. <strong>Carries no secret</strong>: the
    /// response body, headers, or API key are never written here under any
    /// condition — only the HTTP status code and a short description.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>How long the check request took.</summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>The time the check was performed.</summary>
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>The model names the server reported. Empty if the check failed.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];
}

/// <summary>A provider's checked reachability status.</summary>
/// <remarks>
/// Written as a name in JSON (decision K-040): a numeric value would be
/// unreadable as a wire contract, and would silently break if the enum order changed.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ModelProviderHealthStatus>))]
public enum ModelProviderHealthStatus
{
    /// <summary>The provider does not implement <see cref="IModelProviderHealthCheck"/>.</summary>
    Unknown,

    /// <summary>The provider is reachable.</summary>
    Healthy,

    /// <summary>The provider is reachable but returned an unexpected response.</summary>
    Degraded,

    /// <summary>The provider is unreachable.</summary>
    Unhealthy,
}
