using Tracon.Testing.Contracts.Internal;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IIdempotencyStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory store and the three SQL providers must pass the same
/// scenarios.
/// </para>
/// <para>
/// The tenant id is passed <em>explicitly</em> to every call (not through
/// the ambient <see cref="ITenantContext"/>) — the contract therefore does
/// not rely on the generic CRUD shape of
/// <see cref="TenantIsolationContract{TStore}"/> and derives from
/// <see cref="StoreCancellationContract{TStore}"/> directly; the same pattern
/// as <c>SingletonLeaseStoreContract</c>.
/// </para>
/// </remarks>
public abstract class IdempotencyStoreContract : StoreCancellationContract<IIdempotencyStore>
{
    private const string Tenant = "test";

    /// <summary>The key the cancellation contract reserves against.</summary>
    private readonly string _cancellationKey = Key();

    /// <inheritdoc />
    /// <remarks>
    /// A reservation for a key that already exists is the read path: it
    /// reports the stored state instead of opening a new slot.
    /// </remarks>
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
    {
        var key = Key();
        await Store.ReserveAsync(Request(Tenant, key), CancellationToken.None);

        await Store.ReserveAsync(Request(Tenant, key), cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
        => await Store.ReserveAsync(Request(Tenant, _cancellationKey), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A key nothing ever reserved comes back <see cref="IdempotencyState.Reserved"/>
    /// -- a fresh slot. Any other state means the cancelled call wrote a row.
    /// </remarks>
    protected override async ValueTask<bool> WroteAnythingAsync()
        => (await Store.ReserveAsync(Request(Tenant, _cancellationKey), CancellationToken.None)).State
            is not IdempotencyState.Reserved;

    private static string Key() => $"key-{Guid.NewGuid():N}";

    private static IdempotencyRequest Request(string tenantId, string key, string fingerprint = "fp-a")
        => new()
        {
            TenantId = tenantId,
            Key = key,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>A request made <paramref name="later"/> after <paramref name="first"/>, with the given reservation timeout.</summary>
    private static IdempotencyRequest RequestAfter(IdempotencyRequest first, TimeSpan later, TimeSpan? timeout, string? fingerprint = null)
        => first with
        {
            CreatedAt = first.CreatedAt + later,
            Fingerprint = fingerprint ?? first.Fingerprint,
            ReservationTimeout = timeout,
        };

    private static IdempotencyResponse Response(int statusCode = 200, IReadOnlyDictionary<string, string>? headers = null)
        => new()
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Body = """{"ok":true}""",
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

    [Fact]
    public async Task New_key_returns_Reserved()
    {
        var reservation = await Store.ReserveAsync(Request(Tenant, Key()));

        reservation.State.ShouldBe(IdempotencyState.Reserved);
        reservation.Response.ShouldBeNull();
    }

    /// <summary>
    /// The key is the client's: one that differs only in letter case is another reservation. A store that
    /// folds case answers the second client with the first one's slot, and replays its response.
    /// </summary>
    [Fact]
    public async Task Keys_that_differ_only_in_case_are_two_reservations()
    {
        var lower = Key();
        var upper = lower.ToUpperInvariant();

        (await Store.ReserveAsync(Request(Tenant, lower))).State.ShouldBe(IdempotencyState.Reserved);
        (await Store.ReserveAsync(Request(Tenant, upper))).State.ShouldBe(IdempotencyState.Reserved);

        await Store.CompleteAsync(Tenant, lower, Response(statusCode: 201));

        (await Store.ReserveAsync(Request(Tenant, lower))).State.ShouldBe(IdempotencyState.Completed);
        (await Store.ReserveAsync(Request(Tenant, upper))).State.ShouldBe(IdempotencyState.InProgress);
    }

    [Fact]
    public async Task Key_in_progress_returns_InProgress_on_second_reservation()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));

        var second = await Store.ReserveAsync(Request(Tenant, key));

        second.State.ShouldBe(IdempotencyState.InProgress);
    }

    /// <summary>
    /// A process that dies between the reservation and the response leaves a reservation nobody will ever finish. Without
    /// a limit, every later request with the key is told it is still being processed until the record is deleted.
    /// </summary>
    [Fact]
    public async Task A_reservation_older_than_the_timeout_is_taken_over_by_the_next_request()
    {
        var first = Request(Tenant, Key());
        var timeout = TimeSpan.FromMinutes(30);

        (await Store.ReserveAsync(first)).State.ShouldBe(IdempotencyState.Reserved);

        var retry = await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromMinutes(31), timeout));

        retry.State.ShouldBe(IdempotencyState.Reserved, "the first request never finished, so the retry owns the key now");

        // The taker owns it from its own time: a request right after it is told the key is in progress again.
        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromMinutes(32), timeout)))
            .State.ShouldBe(IdempotencyState.InProgress);
    }

    [Fact]
    public async Task A_reservation_younger_than_the_timeout_is_still_in_progress()
    {
        var first = Request(Tenant, Key());

        await Store.ReserveAsync(first);

        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30))))
            .State.ShouldBe(IdempotencyState.InProgress, "the request may still be running");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task A_reservation_is_never_taken_over_without_a_positive_timeout(int? minutes)
    {
        var first = Request(Tenant, Key());

        await Store.ReserveAsync(first);

        TimeSpan? timeout = minutes is { } value ? TimeSpan.FromMinutes(value) : null;

        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromDays(30), timeout)))
            .State.ShouldBe(IdempotencyState.InProgress);
    }

    /// <summary>
    /// A consumer that wants no takeover can pass the largest time span there is. Subtracting it from a date would run off
    /// the range of a date, so a store has to answer "in progress" for it rather than throw.
    /// </summary>
    [Fact]
    public async Task A_timeout_longer_than_any_reservation_has_been_alive_takes_nothing_over_and_does_not_throw()
    {
        var first = Request(Tenant, Key());

        await Store.ReserveAsync(first);

        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromDays(30), TimeSpan.MaxValue)))
            .State.ShouldBe(IdempotencyState.InProgress);
    }

    [Fact]
    public async Task A_completed_key_is_never_taken_over_however_old_it_is()
    {
        var first = Request(Tenant, Key());

        await Store.ReserveAsync(first);
        await Store.CompleteAsync(Tenant, first.Key, Response(statusCode: 201));

        var replay = await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromDays(30), TimeSpan.FromMinutes(30)));

        replay.State.ShouldBe(IdempotencyState.Completed, "a finished request is replayed, not run again");
        replay.Response!.StatusCode.ShouldBe(201);
    }

    [Fact]
    public async Task A_taken_over_reservation_belongs_to_the_taker_with_its_own_fingerprint()
    {
        var first = Request(Tenant, Key(), "fp-a");
        var timeout = TimeSpan.FromMinutes(30);

        await Store.ReserveAsync(first);

        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromHours(1), timeout, "fp-b")))
            .State.ShouldBe(IdempotencyState.Reserved);

        await Store.CompleteAsync(Tenant, first.Key, Response(statusCode: 201));

        // The finished request is the taker's: its body is the one that matches.
        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromHours(1), timeout, "fp-b")))
            .State.ShouldBe(IdempotencyState.Completed);
        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromHours(1), timeout, "fp-a")))
            .State.ShouldBe(IdempotencyState.FingerprintMismatch);
    }

    [Fact]
    public async Task Only_one_of_several_requests_that_race_for_an_expired_reservation_takes_it()
    {
        const int Racers = 8;

        var first = Request(Tenant, Key());
        var timeout = TimeSpan.FromMinutes(30);

        await Store.ReserveAsync(first);

        // Real parallelism: every racer starts on a thread of its own and waits at the gate, so a store that answers
        // synchronously is still asked by several callers at the same moment.
        using var gate = new ManualResetEventSlim(false);

        var racers = Enumerable.Range(0, Racers).Select(index => Task.Run(async () =>
        {
            gate.Wait();

            return (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromHours(1) + TimeSpan.FromTicks(index), timeout))).State;
        })).ToList();

        gate.Set();

        var states = await Task.WhenAll(racers);

        states.Count(static state => state == IdempotencyState.Reserved)
            .ShouldBe(1, "two requests that both took the key would run the work twice");
        states.Count(static state => state == IdempotencyState.InProgress).ShouldBe(Racers - 1);
    }

    /// <summary>
    /// The request that was taken over may still be running. Its response is not the key's: stored, it would sit under
    /// the fingerprint of the request that took the key, and the next request with that body would replay it.
    /// </summary>
    [Fact]
    public async Task A_request_that_was_taken_over_cannot_store_its_response_under_the_new_owner()
    {
        var old = Request(Tenant, Key(), "fp-old");
        var timeout = TimeSpan.FromMinutes(30);
        var taker = RequestAfter(old, TimeSpan.FromHours(1), timeout, "fp-new");

        await Store.ReserveAsync(old);
        (await Store.ReserveAsync(taker)).State.ShouldBe(IdempotencyState.Reserved);

        // The old owner finishes late.
        await Store.CompleteAsync(Tenant, old.Key, Response(statusCode: 201), old.CreatedAt);

        (await Store.ReserveAsync(RequestAfter(taker, TimeSpan.FromSeconds(1), timeout, "fp-new")))
            .State.ShouldBe(IdempotencyState.InProgress, "the key is still the taker's and the taker has not finished");

        await Store.CompleteAsync(Tenant, taker.Key, Response(statusCode: 202), taker.CreatedAt);

        var replay = await Store.ReserveAsync(RequestAfter(taker, TimeSpan.FromSeconds(2), timeout, "fp-new"));

        replay.State.ShouldBe(IdempotencyState.Completed);
        replay.Response!.StatusCode.ShouldBe(202, "the response stored is the one of the request that owns the key");
    }

    [Fact]
    public async Task A_request_that_was_taken_over_cannot_release_the_new_owners_reservation()
    {
        var old = Request(Tenant, Key());
        var timeout = TimeSpan.FromMinutes(30);
        var taker = RequestAfter(old, TimeSpan.FromHours(1), timeout);

        await Store.ReserveAsync(old);
        await Store.ReserveAsync(taker);

        await Store.ReleaseAsync(Tenant, old.Key, old.CreatedAt);

        // Had the release gone through, this request would reserve the key and run beside the taker.
        (await Store.ReserveAsync(RequestAfter(taker, TimeSpan.FromSeconds(1), timeout)))
            .State.ShouldBe(IdempotencyState.InProgress);
    }

    [Fact]
    public async Task A_request_that_was_taken_over_cannot_delete_a_response_the_new_owner_stored()
    {
        var old = Request(Tenant, Key());
        var timeout = TimeSpan.FromMinutes(30);
        var taker = RequestAfter(old, TimeSpan.FromHours(1), timeout);

        await Store.ReserveAsync(old);
        await Store.ReserveAsync(taker);
        await Store.CompleteAsync(Tenant, taker.Key, Response(statusCode: 201), taker.CreatedAt);

        await Store.ReleaseAsync(Tenant, old.Key, old.CreatedAt);

        (await Store.ReserveAsync(RequestAfter(taker, TimeSpan.FromSeconds(1), timeout)))
            .State.ShouldBe(IdempotencyState.Completed, "a finished request is never taken over, and an old owner cannot undo it");
    }

    [Fact]
    public async Task The_owner_of_a_reservation_completes_and_releases_it_as_before()
    {
        var completed = Request(Tenant, Key());
        var released = Request(Tenant, Key());

        await Store.ReserveAsync(completed);
        await Store.ReserveAsync(released);

        await Store.CompleteAsync(Tenant, completed.Key, Response(statusCode: 201), completed.CreatedAt);
        await Store.ReleaseAsync(Tenant, released.Key, released.CreatedAt);

        (await Store.ReserveAsync(completed)).State.ShouldBe(IdempotencyState.Completed);
        (await Store.ReserveAsync(released)).State.ShouldBe(IdempotencyState.Reserved, "the release freed the key");
    }

    [Fact]
    public async Task A_reservation_of_another_tenant_is_not_taken_over()
    {
        var key = Key();
        var first = Request("tenant-a", key);

        await Store.ReserveAsync(first);
        await Store.ReserveAsync(Request("tenant-b", key));

        // Tenant B's slot is its own: tenant A's expired reservation is taken over by A's retry alone.
        (await Store.ReserveAsync(RequestAfter(Request("tenant-b", key), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30))))
            .State.ShouldBe(IdempotencyState.InProgress);
        (await Store.ReserveAsync(RequestAfter(first, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))))
            .State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public async Task Completed_key_returns_the_stored_response_with_the_same_fingerprint()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.CompleteAsync(Tenant, key, Response(statusCode: 201));

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.State.ShouldBe(IdempotencyState.Completed);
        replay.Response.ShouldNotBeNull();
        replay.Response!.StatusCode.ShouldBe(201);
        replay.Response!.Body.ShouldBe("""{"ok":true}""");
    }

    [Fact]
    public async Task Completed_key_also_returns_the_stored_HTTP_headers()
    {
        var key = Key();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Location"] = "/api/runs/abc",
            ["Preference-Applied"] = "respond-async",
        };

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.CompleteAsync(Tenant, key, Response(statusCode: 202, headers: headers));

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.State.ShouldBe(IdempotencyState.Completed);
        replay.Response!.Headers["Location"].ShouldBe("/api/runs/abc");
        replay.Response!.Headers["Preference-Applied"].ShouldBe("respond-async");
    }

    [Fact]
    public async Task Completed_key_returns_an_empty_dictionary_when_there_are_NO_headers()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.CompleteAsync(Tenant, key, Response());

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.Response!.Headers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Completed_key_with_a_DIFFERENT_fingerprint_returns_FingerprintMismatch()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key, "fp-a"));
        await Store.CompleteAsync(Tenant, key, Response());

        var mismatch = await Store.ReserveAsync(Request(Tenant, key, "fp-b"));

        mismatch.State.ShouldBe(IdempotencyState.FingerprintMismatch);
        mismatch.Response.ShouldBeNull();
    }

    [Fact]
    public async Task Released_key_can_be_reserved_again()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));
        await Store.ReleaseAsync(Tenant, key);

        // 🚨 Retrying with the same key after a failed run MUST work — the
        // record must have been deleted.
        var retried = await Store.ReserveAsync(Request(Tenant, key));

        retried.State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public async Task Same_key_lives_independently_in_two_tenants()
    {
        var key = Key();

        var first = await Store.ReserveAsync(Request("tenant-a", key));
        var second = await Store.ReserveAsync(Request("tenant-b", key));

        // The second tenant must NOT be affected by the FIRST tenant's reservation.
        first.State.ShouldBe(IdempotencyState.Reserved);
        second.State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public async Task Completed_record_does_NOT_affect_another_tenant()
    {
        var key = Key();

        await Store.ReserveAsync(Request("tenant-a", key));
        await Store.CompleteAsync("tenant-a", key, Response());

        var otherTenant = await Store.ReserveAsync(Request("tenant-b", key));

        otherTenant.State.ShouldBe(IdempotencyState.Reserved);
    }

    [Fact]
    public Task Concurrent_reservation_of_the_same_key_only_one_gets_Reserved()
    {
        // The callers start on threads of their own, released together, and the group is released at a fresh key many
        // times: a store that looks and then writes lets two callers through in some round, not once in a while.
        const int Callers = 8;
        const int Rounds = 40;

        for (var round = 0; round < Rounds; round++)
        {
            var key = Key();

            var results = SimultaneousCalls.Run(
                Callers,
                _ => Store.ReserveAsync(Request(Tenant, key)).AsTask().GetAwaiter().GetResult());

            results.Count(r => r.State == IdempotencyState.Reserved).ShouldBe(1, $"round {round}: two requests reserved the same key");
            results.Count(r => r.State == IdempotencyState.InProgress).ShouldBe(Callers - 1, $"round {round}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Two spellings of one tenant reserve, complete and replay the same key.
    /// </summary>
    /// <remarks>
    /// The tenant is matched case-insensitively across the product. A store that keys on the raw text hands a
    /// retry that arrives under another spelling a fresh slot, and the request runs twice.
    /// </remarks>
    [Fact]
    public async Task A_tenant_spelled_in_another_letter_case_reaches_the_same_reservation()
    {
        var key = Key();

        (await Store.ReserveAsync(Request("Test", key))).State.ShouldBe(IdempotencyState.Reserved);
        (await Store.ReserveAsync(Request(Tenant, key))).State.ShouldBe(
            IdempotencyState.InProgress,
            "the retry under the other spelling was given a fresh slot");

        await Store.CompleteAsync("TEST", key, Response(statusCode: 201));

        var replay = await Store.ReserveAsync(Request(Tenant, key));

        replay.State.ShouldBe(IdempotencyState.Completed, "the completion under the other spelling was not found");
        replay.Response.ShouldNotBeNull().StatusCode.ShouldBe(201);
    }
}
