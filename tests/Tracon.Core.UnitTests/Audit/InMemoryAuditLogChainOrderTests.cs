namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// The chain of the in-memory audit log is verified in the order it was written, also when two entries carry the same instant.
/// </summary>
/// <remarks>
/// The caller sets <c>CreatedAt</c>, and two writes can read the same tick of the clock. Ordering a tie by identifier made the
/// second entry come first whenever its identifier sorted lower, and the verification reported a gap in a chain that had none
/// (about one run in five of the contract case with two long labels, measured before and after this test).
/// </remarks>
public sealed class InMemoryAuditLogChainOrderTests
{
    private static readonly DateTimeOffset Instant = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Two_entries_of_one_instant_verify_in_the_order_they_were_written_whatever_their_identifiers()
    {
        var log = new InMemoryAuditLog();

        // The first entry's identifier sorts AFTER the second's.
        var first = Entry(new Guid("ffffffff-ffff-7fff-bfff-ffffffffffff"), "first");
        var second = Entry(new Guid("00000000-0000-7000-8000-000000000001"), "second");

        await log.WriteAsync(first);
        await log.WriteAsync(second);

        var verification = await log.VerifyChainAsync(new AuditChainQuery { TenantId = "default" });

        verification.Status.ShouldBe(AuditChainStatus.Valid);
        verification.EntriesChecked.ShouldBe(2);
    }

    private static AuditEntry Entry(Guid id, string entity)
        => new()
        {
            Id = id,
            TenantId = "default",
            Actor = "user-1",
            Action = "session.delete",
            Entity = entity,
            Before = """{"version":1}""",
            After = """{"version":2}""",
            CreatedAt = Instant,
        };
}
