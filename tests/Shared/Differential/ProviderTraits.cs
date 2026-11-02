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
    /// Gets a value indicating whether the filters on a user identity ignore letter case.
    /// </summary>
    /// <remarks>
    /// OPEN FINDING, kept visible here until it is fixed: <c>sessions.owner_id</c> and <c>runs.user_id</c> are compared with
    /// the database collation, which on SQL Server is case-insensitive, so a listing for owner <c>bS7</c> returns the
    /// sessions of owner <c>Bs7</c>, and a run listing or breakdown for user <c>Alice</c> includes <c>alice</c>. The other
    /// three stores compare ordinally. Under a Turkish collation the fold also follows Turkish case rules (an
    /// <c>I</c> pairs with a dotless <c>i</c>, not with <c>i</c>), so the generated identities leave out the letters i
    /// and I when the trait is set. With the trait off, the SQL Server leg fails on the first sequence that holds two
    /// identities that differ only in letter case.
    /// </remarks>
    public bool IdentityFilterIgnoresCase { get; init; }

    /// <summary>
    /// Gets how many decimal places a cost column keeps, or <see langword="null"/> when the store keeps the amount as
    /// given.
    /// </summary>
    /// <remarks>
    /// The SQL cost columns are <c>numeric(20,10)</c> and round half away from zero; the in-memory store keeps the
    /// whole <see cref="decimal"/>.
    /// </remarks>
    public int? CostDecimals { get; init; }

    /// <summary>
    /// Gets how many significant digits a cost survives a round trip with, or <see langword="null"/> when it survives whole.
    /// </summary>
    /// <remarks>
    /// OPEN FINDING, kept visible here until it is fixed: two SQLite cost columns (<c>cached_input_cost</c> and
    /// <c>non_token_cost</c>) were declared <c>NUMERIC</c>, so SQLite turns the text the driver writes into a
    /// double. 999999999.999999 reads back as 999999999.99999905. The cost property stays inside this many digits for
    /// that provider; with the trait off it fails on SQLite.
    /// </remarks>
    public int? CostSignificantDigits { get; init; }

    /// <summary>
    /// Gets how far an aggregated cost total may be from the exact sum, relative to the sum (at least one unit), or zero when the total must be exact.
    /// </summary>
    /// <remarks>
    /// OPEN FINDING, kept visible here until it is fixed: SQLite adds the cost columns as doubles, so a total of
    /// 6865.1708977361 reads 6865.1708977361004. The cost columns written one by one and the PostgreSQL and SQL Server
    /// totals are exact.
    /// </remarks>
    public decimal AggregateCostRelativeTolerance { get; init; }

    /// <summary>Brings an amount inside the digits the store keeps exactly.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The amount rounded to <see cref="CostSignificantDigits"/> significant digits, or as given.</returns>
    public decimal? Representable(decimal? amount)
    {
        if (amount is not { } value || value == 0m || CostSignificantDigits is not { } digits)
        {
            return amount;
        }

        var magnitude = (int)Math.Floor(Math.Log10((double)Math.Abs(value))) + 1;
        var places = Math.Clamp(digits - magnitude, 0, 28);

        return decimal.Round(value, places, MidpointRounding.AwayFromZero);
    }

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
