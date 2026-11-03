using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Tracon;

/// <summary>
/// The ambient passing mechanism for running an operation executed in the
/// background (outside an HTTP request) as a specific tenant.
/// </summary>
/// <remarks>
/// The <see cref="ITenantContext"/> implementations (the single-tenant
/// default, the HTTP-based multi-tenant resolver) are singletons and read the
/// tenant either from a fixed default or from <c>HttpContext</c>. Which
/// tenant a scheduled job runs for is neither fixed nor lives in an HTTP
/// context — it is carried inside <c>JobRecord.TenantId</c>. This class
/// provides a pass-through using the same <see cref="AsyncLocal{T}"/> pattern
/// <c>IHttpContextAccessor</c> uses: while the value is set, both
/// <see cref="ITenantContext"/> implementations check it before their own default resolution.
/// </remarks>
public static class AmbientTenantScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The current ambient tenant if set right now; <see langword="null"/> otherwise.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>
    /// Sets the ambient tenant for the duration of the scope.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to use for the duration of the scope.</param>
    /// <returns>
    /// An object that restores the previous value when
    /// <see cref="IDisposable.Dispose"/> is called. Nested use is safe.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is empty, whitespace only, or holds a character outside the allowed set
    /// (see <see cref="Normalize"/>).
    /// </exception>
    /// <remarks>
    /// The value is stored in the form <see cref="Normalize"/> returns, so
    /// <see cref="Current"/> is always canonical whatever letter case the
    /// caller wrote.
    /// </remarks>
    public static IDisposable Begin(string tenantId)
    {
        var previous = Ambient.Value;
        Ambient.Value = Normalize(tenantId);

        return new RestoreScope(previous);
    }

    /// <summary>
    /// Returns the canonical form a tenant identifier is compared and stored in.
    /// </summary>
    /// <param name="tenantId">The tenant identifier as written by the caller.</param>
    /// <returns>
    /// The canonical (invariant lower-case) form. A value that is already canonical comes back as the same
    /// instance.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The tenant identifier is the product's primary isolation key. It is made of the ASCII letters, the
    /// digits, <c>_</c>, <c>.</c> and <c>-</c>, and nothing else, and it is matched
    /// <strong>case-insensitively</strong>. Every layer that compares or persists one applies this first: the
    /// tenant context, the allow-list check, the endpoint filter, and every store, the in-memory ones
    /// included. A store should return the canonical form in the records it reads back, whichever spelling
    /// wrote them; the contract tests for sessions and runs check it.
    /// </para>
    /// <para>
    /// The alphabet is closed on purpose. A relational engine compares text by the collation of its column,
    /// and a collation that folds a composed letter with its decomposed spelling (an e with an acute accent against
    /// an e followed by a combining accent) or a full-width letter with its plain one turns two identifiers into
    /// one tenant: the idempotency keys, the API key list and the runs of the two would be shared. Inside
    /// this alphabet no collation joins two identifiers that differ after the fold, so a bare <c>=</c>
    /// behaves the same on PostgreSQL, SQLite, SQL Server and in memory. A caller that holds client text
    /// asks <see cref="TryNormalize"/> first and answers the client; a value that reaches this method
    /// outside the alphabet is a defect of the code that supplied it.
    /// </para>
    /// <para>
    /// Normalizing the <em>value</em> (rather than comparing case-insensitively at query time) is
    /// deliberate, and it is the same choice <see cref="TenantProviderBinding.NormalizeProviderName"/> makes:
    /// the primary key keeps rejecting duplicates on all engines and the index stays usable.
    /// </para>
    /// <para>
    /// A consumer that implements <see cref="ITenantContext"/> or a store itself must apply this to the value
    /// it returns, writes <em>and</em> queries with. A store that skips it is case-sensitive while the rest of
    /// the system is not.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is null, empty, whitespace only, or holds a character other than an ASCII
    /// letter, a digit, <c>_</c>, <c>.</c> or <c>-</c>. The message names the index and the code point of the
    /// first such character and never repeats the value.
    /// </exception>
    public static string Normalize(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var invalid = IndexOfCharacterOutsideAlphabet(tenantId);

        if (invalid >= 0)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A tenant identifier may hold only the ASCII letters, the digits, '_', '.' and '-'; the character at index {invalid} is U+{CodePointAt(tenantId, invalid):X4}."),
                nameof(tenantId));
        }

        // ToLowerInvariant, never ToLower: under tr-TR the latter maps 'I' to
        // the DOTLESS lower-case i, so the same identifier would fold to two
        // different canonical values depending on the server's culture. On a
        // value that has no capital it returns the instance it was given.
        return tenantId.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the canonical form of a tenant identifier, or says it has none.
    /// </summary>
    /// <param name="tenantId">The tenant identifier as written by the caller; may be <see langword="null"/>.</param>
    /// <param name="canonical">The canonical form when the method returns <see langword="true"/>; <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="tenantId"/> is not blank and holds only the ASCII letters, the
    /// digits, <c>_</c>, <c>.</c> and <c>-</c>; <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// This is the check for text that came from a client: a header, a claim, a route segment. It decides
    /// without throwing, so the caller can answer <c>400</c> instead of failing. It applies exactly the rule
    /// of <see cref="Normalize"/> and adds no length limit. A custom <see cref="ITenantContext"/> calls it to
    /// validate what it is about to return.
    /// </remarks>
    public static bool TryNormalize(string? tenantId, [NotNullWhen(true)] out string? canonical)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || IndexOfCharacterOutsideAlphabet(tenantId) >= 0)
        {
            canonical = null;

            return false;
        }

        canonical = tenantId.ToLowerInvariant();

        return true;
    }

    /// <summary>
    /// Returns the canonical form of an optional tenant identifier.
    /// </summary>
    /// <param name="tenantId">The tenant identifier, or <see langword="null"/>.</param>
    /// <returns>
    /// The canonical form; <see langword="null"/> when <paramref name="tenantId"/>
    /// is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// A <see langword="null"/> tenant means "every tenant" in the query types
    /// that accept one, so it passes through unchanged.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is empty, whitespace only, or outside the allowed set (see <see cref="Normalize"/>).
    /// </exception>
    public static string? NormalizeOrNull(string? tenantId)
        => tenantId is null ? null : Normalize(tenantId);

    private static int IndexOfCharacterOutsideAlphabet(string tenantId)
    {
        for (var i = 0; i < tenantId.Length; i++)
        {
            if (tenantId[i] is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-'))
            {
                return i;
            }
        }

        return -1;
    }

    private static int CodePointAt(string text, int index)
        => Rune.TryGetRuneAt(text, index, out var rune) ? rune.Value : text[index];

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
