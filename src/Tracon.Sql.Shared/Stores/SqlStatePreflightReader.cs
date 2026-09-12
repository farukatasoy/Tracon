using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Reads stored state generations out of the SQL database WITHOUT writing anything.
/// </summary>
/// <remarks>
/// <para>
/// Every query here is a <c>SELECT</c>. Nothing is inserted, updated, deleted
/// or locked, and no migration runs — an operator can point this at a live
/// production database while it serves traffic.
/// </para>
/// <para>
/// Unlike <see cref="SqlSessionStore"/>, this reader carries <strong>no tenant
/// predicate</strong>. An upgrade replaces the process for every tenant at
/// once, so "can this build read my data" is a whole-database question.
/// </para>
/// <para>
/// The reader reports what is stored and interprets none of it. Whether a
/// generation is readable, and whether a sampled payload decodes, is decided
/// in <c>Tracon.Core</c>, which knows what the running build writes.
/// </para>
/// </remarks>
internal sealed class SqlStatePreflightReader : IStatePreflightReader
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new reader.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlStatePreflightReader(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <inheritdoc />
    public string ProviderName => _context.ProviderName;

    /// <inheritdoc />
    [TenantAgnostic(
        "A state preflight answers an upgrade-wide question: an upgrade replaces the process for every tenant at once. Filtering by tenant here would under-report how many rows the new build cannot read, which is the one number this surface exists to produce. It never writes and never returns a row to a request-scoped caller.")]
    public async ValueTask<IReadOnlyList<StateGenerationTally>> TallyAsync(
        StatePreflightTarget target,
        CancellationToken cancellationToken = default)
    {
        var command = _context.CreateCommand(target is StatePreflightTarget.Sessions
            ? _sql.CountSessionStateGenerations
            : _sql.CountCheckpointStateGenerations);

        var tallies = await DbHelpers.ReadListAsync(
            command,
            static reader => new StateGenerationTally
            {
                SchemaGeneration = DbHelpers.GetNullableInt32(reader, 0),
                RecordCount = reader.GetInt64(1),
            },
            cancellationToken).ConfigureAwait(false);

        // Ordered here, not in SQL: the three providers disagree on where a
        // NULL group sorts, and the report reads better with the unstamped
        // rows first, oldest generation onwards.
        tallies.Sort(static (left, right) => Nullable.Compare(left.SchemaGeneration, right.SchemaGeneration));

        return tallies;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "A state preflight answers an upgrade-wide question: an upgrade replaces the process for every tenant at once. Filtering by tenant here would under-report how many rows the new build cannot read, which is the one number this surface exists to produce. It never writes and never returns a row to a request-scoped caller.")]
    public async ValueTask<IReadOnlyList<StateSample>> SampleAsync(
        StatePreflightTarget target,
        int perGeneration,
        CancellationToken cancellationToken = default)
    {
        if (perGeneration <= 0)
        {
            return [];
        }

        var isSessions = target is StatePreflightTarget.Sessions;
        var command = _context.CreateCommand(isSessions ? _sql.SampleSessionStates : _sql.SampleCheckpointStates);
        DbHelpers.Add(command, "per_generation", perGeneration);

        return await DbHelpers.ReadListAsync(
            command,
            reader => new StateSample
            {
                // 🚨 NOT reader.GetString(0). `sessions.id` is text on all
                // three providers, but `workflow_checkpoints.id` is `uuid` on
                // PostgreSQL and `uniqueidentifier` on SQL Server — both hand
                // back a boxed Guid and GetString throws InvalidCastException
                // there, while SQLite's TEXT column hides it. DbHelpers.ToGuid
                // exists for exactly this split; here the identifier is only
                // ever printed, so it is normalized to text.
                Id = isSessions ? reader.GetString(0) : DbHelpers.ToGuid(reader.GetValue(0)).ToString("d", CultureInfo.InvariantCulture),
                SchemaGeneration = DbHelpers.GetNullableInt32(reader, 1),
                MafVersion = DbHelpers.GetNullableString(reader, 2),
                // Only `sessions.state` goes through the at-rest protector;
                // `workflow_checkpoints.state` is not a protected column.
                State = ParseState(isSessions ? Unprotect(reader.GetString(3)) : reader.GetString(3)),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Decrypts a stored session payload, tolerating the absence of the key.</summary>
    /// <param name="stored">The value as read back from storage.</param>
    /// <returns>The plaintext, or the protected envelope itself when it cannot be opened.</returns>
    /// <remarks>
    /// <c>ProtectedValue.Read</c> throws when the row carries an envelope this
    /// process holds no key for, so a preflight — which normally runs from the
    /// CLI, holding none — has to absorb that rather than crash on it.
    /// </remarks>
    private string Unprotect(string stored)
    {
        // 🚨 NullContentProtector.Unprotect THROWS on an envelope, and
        // AddTracon() registers it whenever the consumer never called
        // AddContentProtection(...). Measured: with this try/catch removed,
        // `tracon state-check` against the sample application's own
        // database exits 134 with a stack trace instead of reporting that the
        // rows are encrypted. Leaving the protector null in a test does NOT
        // reproduce it — a null one short-circuits and hands the envelope
        // back, which is the opposite behaviour.
        try
        {
            return ProtectedValue.Read(_context, stored)!;
        }
        catch (TraconException)
        {
            // No key at all, or not the key this row was written under (a
            // rotated or retired key id). Both mean the same thing here: this
            // process cannot open the row, which says nothing about whether
            // the application that holds the key can.
            return stored;
        }
    }

    /// <summary>Parses a stored state payload, tolerating text that is not JSON at all.</summary>
    /// <param name="stored">The stored text.</param>
    /// <returns>
    /// The parsed payload, or an <see cref="JsonValueKind.Undefined"/> element
    /// when the text does not parse.
    /// </returns>
    /// <remarks>
    /// A preflight exists to REPORT unreadable data, so one corrupt row must
    /// not abort the whole sweep. <c>Undefined</c> is the reader's way of
    /// saying "the stored text is not JSON"; <c>StatePreflight</c> turns it
    /// into a named sample failure.
    /// </remarks>
    private static JsonElement ParseState(string stored)
    {
        try
        {
            using var document = JsonDocument.Parse(stored);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
