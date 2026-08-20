using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Computes the hash chain link for an audit trail entry. The single
/// place the canonical form is defined; both the write path and the verify path
/// call it, so the two can never drift apart.
/// </summary>
/// <remarks>
/// <para>
/// The canonical form is a fixed-shape, hand-written JSON object — fields are
/// written in a FIXED order and the writer is not System.Text.Json's default
/// serializer, so there is no property-ordering ambiguity to worry about. The
/// field order is part of the contract: changing it changes every hash computed
/// afterward and makes an already-written chain unverifiable.
/// </para>
/// <para>
/// <c>createdAt</c> is rounded down to microsecond precision before it is
/// hashed. PostgreSQL's <c>timestamptz</c> stores only microsecond precision (it
/// drops the last digit of a .NET 100ns tick); without this rounding, a value
/// hashed with full tick precision at write time would never match the value
/// read back from PostgreSQL at verify time, and every entry would misreport as
/// <see cref="AuditChainStatus.Broken"/>. SQL Server and SQLite both preserve
/// full tick precision, so rounding is harmless there — it is applied
/// unconditionally so the same canonical form works on all three providers.
/// </para>
/// </remarks>
public static class AuditChainHasher
{
    private const long TicksPerMicrosecond = 10;

    /// <summary>Computes the hash of an audit entry.</summary>
    /// <param name="previousHash">
    /// The hash of the previous entry of the same tenant; <see langword="null"/> for
    /// the first entry.
    /// </param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="actor">The actor.</param>
    /// <param name="action">The action name.</param>
    /// <param name="entity">The affected entity.</param>
    /// <param name="before">The state before the change, as JSON text.</param>
    /// <param name="after">The state after the change, as JSON text.</param>
    /// <param name="createdAt">The time the record was written.</param>
    /// <returns>The hash, as a lowercase 64-character hex string (SHA-256).</returns>
    public static string ComputeHash(
        string? previousHash,
        string tenantId,
        string? actor,
        string action,
        string entity,
        string? before,
        string? after,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(entity);

        var canonical = BuildCanonicalForm(previousHash, tenantId, actor, action, entity, before, after, createdAt);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        // Convert.ToHexStringLower arrived with .NET 9; net8.0 is also targeted.
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Rounds a timestamp DOWN to microsecond precision — the precision PostgreSQL's
    /// <c>timestamptz</c> actually stores.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>The rounded timestamp, in UTC.</returns>
    /// <remarks>
    /// The SQL write path (<c>SqlAuditLog.WriteAsync</c>) truncates
    /// <see cref="AuditEntry.CreatedAt"/> to THIS value BEFORE it is hashed AND
    /// before it is written to the <c>created_at</c> column. Truncating only for
    /// the hash (and leaving the column at full precision) is not enough: whether
    /// Npgsql's client-side conversion truncates or ROUNDS a sub-microsecond
    /// remainder is an implementation detail this code must not depend on —
    /// measured, it does not always agree with a simple truncation, and every
    /// entry misreported as <see cref="AuditChainStatus.Broken"/> as a result. By
    /// pre-truncating the value that is ACTUALLY stored, there is no remainder
    /// left for the database to round any way at all.
    /// </remarks>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var roundedTicks = value.UtcTicks - (value.UtcTicks % TicksPerMicrosecond);

        return new DateTimeOffset(roundedTicks, TimeSpan.Zero);
    }

    /// <summary>Builds the canonical text form that is hashed. Exposed for tests only.</summary>
    internal static string BuildCanonicalForm(
        string? previousHash,
        string tenantId,
        string? actor,
        string action,
        string entity,
        string? before,
        string? after,
        DateTimeOffset createdAt)
    {
        var rounded = TruncateToMicroseconds(createdAt);

        var builder = new StringBuilder(256);
        builder.Append("{\"prevHash\":");
        AppendJsonString(builder, previousHash);
        builder.Append(",\"tenantId\":");
        AppendJsonString(builder, tenantId);
        builder.Append(",\"actor\":");
        AppendJsonString(builder, actor);
        builder.Append(",\"action\":");
        AppendJsonString(builder, action);
        builder.Append(",\"entity\":");
        AppendJsonString(builder, entity);
        builder.Append(",\"before\":");
        AppendJsonString(builder, before);
        builder.Append(",\"after\":");
        AppendJsonString(builder, after);
        builder.Append(",\"createdAt\":\"");
        builder.Append(rounded.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture));
        builder.Append("\"}");

        return builder.ToString();
    }

    /// <summary>
    /// Appends a JSON string value (or <c>null</c>), escaping the four characters that
    /// could change the field boundary.
    /// </summary>
    private static void AppendJsonString(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("null");

            return;
        }

        builder.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");

                    break;
                case '\\':
                    builder.Append("\\\\");

                    break;
                case '\n':
                    builder.Append("\\n");

                    break;
                case '\r':
                    builder.Append("\\r");

                    break;
                default:
                    builder.Append(c);

                    break;
            }
        }

        builder.Append('"');
    }
}
