namespace Tracon;

/// <summary>
/// Validates SQL identifiers.
/// </summary>
/// <remarks>
/// <para>
/// The schema name comes from configuration and is placed <em>directly</em> into the
/// SQL text — identifiers cannot be sent as parameters. The name is therefore validated
/// strictly here before it enters the SQL text: only lowercase letters, digits and
/// underscores are accepted. That makes SQL injection through the schema name
/// impossible.
/// </para>
/// <para>
/// The rule is <strong>the same</strong> on every provider, even though SQL Server
/// allows a wider identifier set. The reason is portability: the same schema name must
/// be usable between PostgreSQL and SQL Server without change. The lowercase
/// requirement comes from PostgreSQL folding unquoted identifiers to lowercase; a name
/// that contains uppercase letters cannot be read back as it was written.
/// </para>
/// </remarks>
internal static class SqlIdentifier
{
    /// <summary>
    /// The identifier length limit.
    /// </summary>
    /// <remarks>
    /// The PostgreSQL limit is <c>NAMEDATALEN - 1</c> = 63; SQL Server allows 128.
    /// The narrower one is chosen for portability between the two providers.
    /// </remarks>
    private const int MaxLength = 63;

    /// <summary>Determines whether the value is a valid identifier usable without quoting.</summary>
    /// <param name="value">The name to check.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    public static bool IsValidUnquoted(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        var first = value[0];

        if (!IsLowerLetter(first) && first != '_')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsLowerLetter(character) && !IsDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns a valid schema name; throws when it is not valid.</summary>
    /// <param name="value">The schema name to check.</param>
    /// <returns>The validated schema name.</returns>
    /// <exception cref="TraconException">The name is not a valid identifier.</exception>
    public static string RequireSchemaName(string? value)
    {
        if (!IsValidUnquoted(value))
        {
            throw new TraconException(
                $"'{value}' is not a valid schema name. It must start with a lowercase letter or an underscore; " +
                $"it must contain only lowercase letters, digits and underscores; it must be at most {MaxLength} characters.");
        }

        return value!;
    }

    private static bool IsLowerLetter(char character) => character is >= 'a' and <= 'z';

    private static bool IsDigit(char character) => character is >= '0' and <= '9';
}
