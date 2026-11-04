namespace Tracon.Tests.Differential;

/// <summary>
/// The differences between stores that are documented, each with its reason. Everything not named here is a
/// difference the differential reports.
/// </summary>
/// <remarks>
/// <para>
/// The model is one hand-written specification. A provider that is allowed to differ from it says so here, as a
/// value the model reads; the model is never loosened silently, and a difference is never allowed by comparing less.
/// </para>
/// <para>
/// Adding a trait is a decision: it needs a reason that is written down in the product (a doc comment on the store,
/// the migration, or the schema), not only observed.
/// </para>
/// </remarks>
internal sealed record ProviderTraits
{
    /// <summary>Gets the provider name used in failure messages.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether a U+0000 in a text value comes back as U+FFFD.
    /// </summary>
    /// <remarks>
    /// PostgreSQL cannot hold U+0000, so every SQL store writes U+FFFD in its place and the in-memory stores keep the
    /// value as it came (the doc comment on the shared SQL text helper states both).
    /// </remarks>
    public bool NulBecomesReplacementCharacter { get; init; }

    /// <summary>
    /// Gets how many ticks (100 ns) the store's timestamp column cannot tell apart; a stored instant is truncated to a
    /// multiple of this.
    /// </summary>
    /// <remarks>PostgreSQL <c>timestamptz</c> keeps microseconds, so its value is 10; the others keep every tick.</remarks>
    public long TimestampGranularityTicks { get; init; } = 1;

    /// <summary>
    /// Gets the instant, in UTC ticks, that the store counts its timestamp units from.
    /// </summary>
    /// <remarks>
    /// The PostgreSQL driver counts microseconds from 2000-01-01 and cuts the remainder by integer division, which rounds
    /// toward that origin: a sub-microsecond tick after the origin is dropped, one before it moves up to the next
    /// microsecond.
    /// </remarks>
    public long TimestampOriginTicks { get; init; }

    /// <summary>
    /// Gets a value indicating whether the largest representable instant is stored exactly although the column keeps less
    /// than a tick.
    /// </summary>
    /// <remarks>
    /// The PostgreSQL driver writes <see cref="DateTimeOffset.MaxValue"/> as <c>infinity</c> and reads it back as the same
    /// value, which is why "never expires" survives the microsecond column.
    /// </remarks>
    public bool MaxInstantIsExact { get; init; }

    /// <summary>
    /// Gets a value indicating whether a key that differs from another only in trailing U+0020 is the same key.
    /// </summary>
    /// <remarks>SQL Server pads the shorter operand of a string comparison, so <c>'a'</c> equals <c>'a '</c> in a key.</remarks>
    public bool TrailingSpaceInsensitiveKeys { get; init; }

    /// <summary>
    /// Gets a value indicating whether a read by key answers with the key the caller passed rather than the key the row holds.
    /// </summary>
    /// <remarks>
    /// The SQL session store builds the result of a read from the identifier in the call, so a row stored under a
    /// replaced U+0000 reads back with the caller's spelling; the list query answers with the stored one. Open
    /// finding, kept visible here until the read takes the identifier from the row.
    /// </remarks>
    public bool ReadEchoesRequestedKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether the record a create call returns carries the caller's text, not the text the row
    /// now holds.
    /// </summary>
    /// <remarks>
    /// A SQL store builds the returned record from its input and writes the row separately, so a U+0000 the store
    /// replaced still shows in the returned record and not in any later read. The tenant is not part of this: it is
    /// always returned in the form the row holds.
    /// </remarks>
    public bool CreateEchoesInputText { get; init; }

    /// <summary>
    /// Gets how many decimal places a cost column keeps, or <see langword="null"/> when the store keeps the amount as
    /// given.
    /// </summary>
    /// <remarks>
    /// The PostgreSQL and SQL Server cost columns are <c>numeric(20,10)</c> and round half away from zero; the in-memory store and SQLite (a TEXT column) keep the
    /// whole <see cref="decimal"/>.
    /// </remarks>
    public int? CostDecimals { get; init; }

    /// <summary>
    /// Gets a value indicating whether a failed outcome recorded on a subscription that is already off answers
    /// <see langword="true"/>, as if that call had switched it off.
    /// </summary>
    /// <remarks>
    /// OPEN FINDING, kept visible here until it is fixed: the interface says the call answers "true if the subscription was
    /// disabled during this call", and the in-memory store does exactly that. The three SQL statements answer
    /// <c>(NOT enabled) AND failed</c> read after the update, so a failure of a subscription that an administrator
    /// switched off, or that an earlier failure switched off, answers <see langword="true"/> again and the delivery job
    /// logs "was disabled" every time.
    /// </remarks>
    public bool OutcomeAnswersForASubscriptionThatIsOff { get; init; }

    /// <summary>
    /// Gets a value indicating whether the lease owner of a job is compared by the collation of its column, so two worker names
    /// that differ only in letter case are one worker.
    /// </summary>
    /// <remarks>
    /// OPEN FINDING, kept visible here until it is fixed: the interface says a write that needs the lease changes the job only
    /// while the lease is exactly the one the worker was given, the same owner included. On SQL Server <c>lease_owner</c> has
    /// the collation of the database (case-insensitive by default), so a worker named <c>x3M</c> can renew, complete or release
    /// the lease of a worker named <c>X3m</c>. The job model draws worker names without such twins on a provider that has this
    /// trait.
    /// </remarks>
    public bool LeaseOwnerComparesByCollation { get; init; }

    /// <summary>
    /// Gets a value indicating whether one call that reads several rows can see some of them before another writer's
    /// transaction and the rest after it.
    /// </summary>
    /// <remarks>
    /// SQL Server reads at READ COMMITTED with locks, not with row versions: a read of two counters that one transaction updates can
    /// return the first counter as it was and the second as the transaction left it. PostgreSQL gives a statement one snapshot, and
    /// SQLite serializes. The race of the quota model leaves the counter read out of its concurrent steps on a provider that has
    /// this trait; the read-back after the tasks end is exact either way.
    /// </remarks>
    public bool MultiRowReadIsNotASnapshot { get; init; }

    /// <summary>Applies the store's text rule to a value.</summary>
    /// <param name="value">The text as the caller wrote it.</param>
    /// <returns>The text as the store holds it.</returns>
    public string Text(string value)
        => NulBecomesReplacementCharacter ? value.Replace('\0', '\uFFFD') : value;

    /// <summary>Applies the store's cost precision to an amount.</summary>
    /// <param name="amount">The amount as the caller wrote it.</param>
    /// <returns>The amount as the store holds it.</returns>
    public decimal? Cost(decimal? amount)
        => amount is null || CostDecimals is not { } decimals ? amount : decimal.Round(amount.Value, decimals, MidpointRounding.AwayFromZero);

    /// <summary>Applies the store's timestamp resolution to an instant.</summary>
    /// <param name="instant">The instant as the caller wrote it.</param>
    /// <returns>The UTC ticks the store holds.</returns>
    public long Ticks(DateTimeOffset instant)
    {
        var ticks = instant.UtcTicks;

        if (MaxInstantIsExact && ticks == DateTimeOffset.MaxValue.UtcTicks)
        {
            return ticks;
        }

        var fromOrigin = ticks - TimestampOriginTicks;

        return TimestampOriginTicks + fromOrigin - (fromOrigin % TimestampGranularityTicks);
    }
}
