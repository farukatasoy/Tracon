namespace Tracon.Embedded;

/// <summary>
/// A host-owned record that references a Tracon run by its <see cref="RunId"/>
/// instead of copying anything the run itself already recorded — the pattern this
/// sample exists to run end to end (see the EF Core guide on the documentation site).
/// </summary>
public sealed class SupportTicket
{
    /// <summary>The ticket's own identifier, in the host's own schema.</summary>
    public Guid Id { get; set; }

    /// <summary>The tenant the ticket belongs to.</summary>
    public required string TenantId { get; set; }

    /// <summary>The customer's original message.</summary>
    public required string Subject { get; set; }

    /// <summary>
    /// The identifier of the Tracon run that answered this ticket. A
    /// reference only — the run's own content lives in Tracon's store,
    /// reachable through <c>GET /tracon/api/runs/{id}</c>.
    /// </summary>
    public required Guid RunId { get; set; }

    /// <summary>When the ticket was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
