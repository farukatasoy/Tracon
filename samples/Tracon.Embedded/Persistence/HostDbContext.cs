using Microsoft.EntityFrameworkCore;

namespace Tracon.Embedded;

/// <summary>
/// The host application's own schema — entirely separate from Tracon's
/// <c>tracon</c> schema. When a connection string is configured, this
/// context and Tracon's own store layer share one <c>NpgsqlDataSource</c>
/// (see <c>Program.cs</c> and the EF Core guide on the documentation site);
/// they never share a transaction.
/// </summary>
public sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options)
{
    /// <summary>Support tickets, each referencing the run that answered it.</summary>
    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();
}
