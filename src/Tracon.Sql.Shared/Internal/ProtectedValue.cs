namespace Tracon;

/// <summary>
/// Applies <see cref="SqlStoreContext.ContentProtector"/> at the specific,
/// named write/read sites the twelve protected columns go through.
/// </summary>
/// <remarks>
/// <para>
/// There is no global hook: <c>payload</c> alone appears in four tables and
/// only one of them carries user content, so a store calls this type by
/// hand at each site instead of a blanket wrapper.
/// </para>
/// <para>
/// Reads never consult <see cref="SqlStoreContext.ProtectedColumns"/> — only
/// writes do. <see cref="IContentProtector.Unprotect"/>/<see cref="IContentProtector.UnprotectBytes"/>
/// are self-describing (they look at the value, not configuration), so a row
/// stays readable whether or not its column is still in scope or protection
/// is still turned on.
/// </para>
/// </remarks>
internal static class ProtectedValue
{
    /// <summary>Protects a text value before it is written, if its column is in scope.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="column">The column <paramref name="plaintext"/> is going into.</param>
    /// <param name="plaintext">The value to write; passed through unchanged when <see langword="null"/>.</param>
    /// <returns>The value to bind to the write parameter.</returns>
    public static string? Write(SqlStoreContext context, ProtectedColumn column, string? plaintext)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (plaintext is null || context.ContentProtector is not { } protector || !context.ProtectedColumns.Contains(column))
        {
            return plaintext;
        }

        return protector.Protect(plaintext);
    }

    /// <summary>Reverses <see cref="Write"/> for a text value read back from storage.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="stored">The value as read back; passed through unchanged when <see langword="null"/>.</param>
    /// <returns>The plaintext.</returns>
    public static string? Read(SqlStoreContext context, string? stored)
    {
        ArgumentNullException.ThrowIfNull(context);

        return stored is null ? null : (context.ContentProtector?.Unprotect(stored) ?? stored);
    }

    /// <summary>
    /// Builds the value reader a raw row copy needs: the named columns are read back
    /// as plaintext (text with <see cref="Read"/>, binary with <see cref="ReadBytes"/>),
    /// every other column is written as stored.
    /// </summary>
    /// <param name="context">The store context.</param>
    /// <param name="columns">The protected column names of the row's table.</param>
    /// <returns>The reader, or <see langword="null"/> when the table has no protected column.</returns>
    /// <remarks>
    /// A row copy that leaves the store (a data subject export, a retention archive)
    /// reaches the consumer, and "your own code never sees ciphertext".
    /// </remarks>
    public static Func<string, object, object>? RowReader(SqlStoreContext context, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0)
        {
            return null;
        }

        return (column, stored) => !columns.Contains(column, StringComparer.OrdinalIgnoreCase)
            ? stored
            : stored switch
            {
                string text => Read(context, text)!,
                byte[] bytes => ReadBytes(context, bytes)!,
                _ => stored,
            };
    }

    /// <summary>Protects binary data before it is written, if its column is in scope.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="column">The column <paramref name="plaintext"/> is going into.</param>
    /// <param name="plaintext">The value to write; passed through unchanged when <see langword="null"/>.</param>
    /// <returns>The value to bind to the write parameter.</returns>
    public static byte[]? WriteBytes(SqlStoreContext context, ProtectedColumn column, byte[]? plaintext)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (plaintext is null || context.ContentProtector is not { } protector || !context.ProtectedColumns.Contains(column))
        {
            return plaintext;
        }

        return protector.ProtectBytes(plaintext);
    }

    /// <summary>Reverses <see cref="WriteBytes"/> for binary data read back from storage.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="stored">The value as read back; passed through unchanged when <see langword="null"/>.</param>
    /// <returns>The plaintext bytes.</returns>
    public static byte[]? ReadBytes(SqlStoreContext context, byte[]? stored)
    {
        ArgumentNullException.ThrowIfNull(context);

        return stored is null ? null : (context.ContentProtector?.UnprotectBytes(stored) ?? stored);
    }
}
