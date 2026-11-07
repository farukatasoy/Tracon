namespace Tracon.Tests.Common;

/// <summary>
/// What a SQL idempotency store answers to a request that may take over an old reservation while another request
/// releases the key and reserves it again, written once for every SQL provider.
/// </summary>
/// <remarks>
/// <para>
/// A reservation that conflicts with a row is decided in three statements: the insert, the takeover (an update that only an old
/// enough reservation answers to) and the read of the row that is there. Between the takeover and the read the key can be
/// released and reserved again, and the row the read finds is then another row than the one the takeover missed. A request
/// that may take over every row it can meet, because they are all older than its cutoff, answered <c>InProgress</c> for that
/// new row, which describes a moment in which no young reservation stood in its way: no serial order of the two requests
/// allows it. Measured on SQL Server 2022 (the same recorded seed failed the differential suite): 34 rounds of 400 before the
/// change, none after.
/// </para>
/// <para>
/// Every round puts an old reservation in place and races two requests. One releases the key and reserves it again with a
/// creation time as old as the row it replaced; the other may take over anything older than a cutoff that lies after both.
/// In every serial order the second one is answered <c>Reserved</c>: it takes the old row over, or it finds no row, or it takes
/// over the new old row. Any other answer is the defect. The round count is chosen so that a store with the defect is caught in
/// all but about one run in a million.
/// </para>
/// </remarks>
public abstract class SqlIdempotencyTakeoverRaceContract
{
    private const string Tenant = "takeover-race";
    private const string Key = "the-one-key";
    private const int Rounds = 300;

    private static readonly DateTimeOffset OldReservation = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(11);

    /// <summary>Opens the idempotency store of one migrated, empty schema.</summary>
    /// <returns>The store.</returns>
    protected abstract ValueTask<IIdempotencyStore> OpenAsync();

    /// <summary>A request that may take over an old reservation is never told that a young one stands in its way.</summary>
    /// <returns>A task that completes when the rounds are over.</returns>
    [Fact]
    public async Task A_takeover_that_races_a_release_and_a_new_reservation_is_never_told_a_young_reservation_stands_in_its_way()
    {
        var store = await OpenAsync();
        var told = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            await store.ReleaseAsync(Tenant, Key, null, TestContext.Current.CancellationToken);
            await store.ReserveAsync(Request(OldReservation, timeout: null), TestContext.Current.CancellationToken);

            var start = new TaskCompletionSource();
            var replacement = Task.Run(
                async () =>
                {
                    await start.Task;
                    await store.ReleaseAsync(Tenant, Key, null, CancellationToken.None);
                    await store.ReserveAsync(Request(OldReservation, timeout: null), CancellationToken.None);
                },
                CancellationToken.None);
            var takeover = Task.Run(
                async () =>
                {
                    await start.Task;

                    return await store.ReserveAsync(Request(Now, Timeout), CancellationToken.None);
                },
                CancellationToken.None);

            start.SetResult();

            await replacement;

            var answer = (await takeover).State;

            if (answer != IdempotencyState.Reserved)
            {
                told.Add($"round {round}: {answer}");
            }
        }

        told.ShouldBeEmpty(
            "a request that may take over every row it can meet was answered as if a young reservation stood in its way: "
            + "the row it read was not the row its takeover had missed");
    }

    private static IdempotencyRequest Request(DateTimeOffset createdAt, TimeSpan? timeout)
        => new()
        {
            TenantId = Tenant,
            Key = Key,
            Fingerprint = "the-same-body",
            CreatedAt = createdAt,
            ReservationTimeout = timeout,
        };
}
