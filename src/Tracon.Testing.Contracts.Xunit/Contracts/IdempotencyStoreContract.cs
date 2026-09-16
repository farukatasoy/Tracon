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

    [Fact]
    public async Task Key_in_progress_returns_InProgress_on_second_reservation()
    {
        var key = Key();

        await Store.ReserveAsync(Request(Tenant, key));

        var second = await Store.ReserveAsync(Request(Tenant, key));

        second.State.ShouldBe(IdempotencyState.InProgress);
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
        // record must have been deleted (docs/arsiv/fazlar/43-IDEMPOTENCY-KEY.md, 43.2).
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
    public async Task Concurrent_reservation_of_the_same_key_only_one_gets_Reserved()
    {
        var key = Key();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Store.ReserveAsync(Request(Tenant, key)).AsTask()));

        results.Count(r => r.State == IdempotencyState.Reserved).ShouldBe(1);
        results.Count(r => r.State == IdempotencyState.InProgress).ShouldBe(7);
    }
}
