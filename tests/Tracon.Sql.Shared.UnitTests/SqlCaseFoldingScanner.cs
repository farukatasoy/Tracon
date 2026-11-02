using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Finds the <c>lower()</c> and <c>upper()</c> calls of a migration and says which of them name an ordinal collation in
/// their own argument.
/// </summary>
/// <remarks>
/// <para>
/// The text is read once, left to right, and everything that is not code is blanked out before any call is looked for:
/// comments (<c>--</c> to the end of the line, <c>/* */</c>, nested) and the contents of string literals (<c>''</c> is an
/// escaped quote). A parenthesis or a <c>--</c> inside a literal then cannot move the end of a call or cut the rest of a
/// line, and a literal that merely contains the word <c>lower(</c> is not a call. The blanked text has the length of the
/// original, so a call is reported with its original text.
/// </para>
/// <para>
/// One kind of string literal is code: the text a migration hands to <c>EXEC(N'...')</c>, <c>sp_executesql</c> or
/// <c>EXECUTE format(...)</c>, and any literal that starts as a statement does. SQL Server compiles a batch up front, so a
/// migration that touches a column it is about to drop has to run the statement as a string; the calls inside it run on
/// the database like any other. Such a literal is unescaped (<c>''</c> becomes <c>'</c>) and read the same way, to any
/// depth. A quoted identifier (<c>"C"</c>, <c>[key]</c>) is kept as it is, apart from the characters that could end a call.
/// </para>
/// <para>
/// A call counts as ordinal only when its argument <em>ends</em> in the collation: <c>lower(x COLLATE "C")</c>. A
/// <c>COLLATE</c> in the middle of the argument (<c>lower(f(x COLLATE "C"))</c>, <c>lower(CASE WHEN x COLLATE "C" = 'A'
/// THEN y END)</c>) belongs to another sub-expression, and the folding of the whole still follows the locale.
/// </para>
/// </remarks>
internal static class SqlCaseFoldingScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex FoldingCallStart = new(
        @"\b(?:lower|upper)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex ArgumentEndsInOrdinalCollation = new(
        @"\bCOLLATE\s+(?:""C""|""POSIX""|\w+_BIN2?)\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex DynamicSqlUseSite = new(
        @"(?:\bEXEC(?:UTE)?\s*\(\s*|\bsp_executesql\s+|\bEXECUTE\s+|\bformat\s*\(\s*)N?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex StartsAsStatement = new(
        @"\A\s*(?:SELECT|UPDATE|INSERT|DELETE|MERGE|WITH|ALTER|CREATE|DROP|EXEC|EXECUTE|IF|BEGIN|DECLARE|SET)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>A <c>lower()</c> or <c>upper()</c> call.</summary>
    /// <param name="Text">The call as written, its whole argument list included.</param>
    /// <param name="NamesOrdinalCollation">Whether its argument ends in an ordinal collation.</param>
    /// <param name="InDynamicSql">Whether it sits in a string the migration executes.</param>
    internal sealed record FoldingCall(string Text, bool NamesOrdinalCollation, bool InDynamicSql);

    /// <summary>Finds every <c>lower()</c> and <c>upper()</c> call in <paramref name="sql"/>, dynamic SQL included.</summary>
    /// <param name="sql">The migration.</param>
    /// <param name="bracketIdentifiers">Whether <c>[...]</c> quotes an identifier (T-SQL) or is an array subscript.</param>
    /// <returns>The calls, in order; the calls of a string come after those of the text around it.</returns>
    public static IReadOnlyList<FoldingCall> Scan(string sql, bool bracketIdentifiers)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var calls = new List<FoldingCall>();
        ScanInto(sql, bracketIdentifiers, inDynamicSql: false, calls);

        return calls;
    }

    private static void ScanInto(string sql, bool bracketIdentifiers, bool inDynamicSql, List<FoldingCall> calls)
    {
        var dynamicBodies = new List<string>();
        var code = Mask(sql, bracketIdentifiers, dynamicBodies);

        foreach (Match start in FoldingCallStart.Matches(code))
        {
            var open = start.Index + start.Length - 1;
            var end = open;
            var depth = 0;

            for (; end < code.Length; end++)
            {
                if (code[end] == '(')
                {
                    depth++;
                }
                else if (code[end] == ')' && --depth == 0)
                {
                    break;
                }
            }

            var closed = Math.Min(end, code.Length);
            var argument = code[(open + 1)..closed];

            calls.Add(new FoldingCall(
                sql[start.Index..Math.Min(end + 1, sql.Length)],
                ArgumentEndsInOrdinalCollation.IsMatch(argument),
                inDynamicSql));
        }

        foreach (var body in dynamicBodies)
        {
            ScanInto(body, bracketIdentifiers, inDynamicSql: true, calls);
        }
    }

    /// <summary>
    /// Blanks comments and string literals, keeps everything else, and collects the string literals that are SQL.
    /// </summary>
    private static string Mask(string sql, bool bracketIdentifiers, List<string> dynamicBodies)
    {
        var code = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '-' && Peek(sql, i + 1) == '-')
            {
                while (i < sql.Length && sql[i] is not ('\n' or '\r'))
                {
                    code.Append(' ');
                    i++;
                }
            }
            else if (c == '/' && Peek(sql, i + 1) == '*')
            {
                i = BlankBlockComment(sql, i, code);
            }
            else if (c == '\'')
            {
                i = BlankStringLiteral(sql, i, code, dynamicBodies);
            }
            else if (c == '"' || (c == '[' && bracketIdentifiers))
            {
                i = CopyQuotedIdentifier(sql, i, c == '"' ? '"' : ']', code);
            }
            else
            {
                code.Append(c);
                i++;
            }
        }

        return code.ToString();
    }

    private static int BlankBlockComment(string sql, int index, StringBuilder code)
    {
        var depth = 0;

        do
        {
            if (sql[index] == '/' && Peek(sql, index + 1) == '*')
            {
                depth++;
                code.Append("  ");
                index += 2;
            }
            else if (sql[index] == '*' && Peek(sql, index + 1) == '/')
            {
                depth--;
                code.Append("  ");
                index += 2;
            }
            else
            {
                code.Append(sql[index] is '\n' or '\r' ? sql[index] : ' ');
                index++;
            }
        }
        while (depth > 0 && index < sql.Length);

        return index;
    }

    private static int BlankStringLiteral(string sql, int index, StringBuilder code, List<string> dynamicBodies)
    {
        var start = index;
        var body = new StringBuilder();

        index++;

        while (index < sql.Length)
        {
            if (sql[index] != '\'')
            {
                body.Append(sql[index]);
                index++;
            }
            else if (Peek(sql, index + 1) == '\'')
            {
                body.Append('\'');
                index += 2;
            }
            else
            {
                index++;

                break;
            }
        }

        var text = body.ToString();

        if (StartsAsStatement.IsMatch(text) || DynamicSqlUseSite.IsMatch(Tail(code)))
        {
            dynamicBodies.Add(text);
        }

        code.Append(' ', index - start);

        return index;
    }

    /// <summary>Copies a quoted identifier, with the characters that could end a call or open a comment turned into '_'.</summary>
    private static int CopyQuotedIdentifier(string sql, int index, char close, StringBuilder code)
    {
        code.Append(sql[index]);
        index++;

        while (index < sql.Length)
        {
            var c = sql[index];

            if (c == close && Peek(sql, index + 1) == close)
            {
                code.Append("__");
                index += 2;
            }
            else if (c == close)
            {
                code.Append(c);

                return index + 1;
            }
            else
            {
                code.Append(c is '(' or ')' or '\'' or '-' or '/' or '*' ? '_' : c);
                index++;
            }
        }

        return index;
    }

    private static char Peek(string sql, int index) => index < sql.Length ? sql[index] : '\0';

    private static string Tail(StringBuilder code)
        => code.ToString(Math.Max(0, code.Length - 64), Math.Min(64, code.Length));
}
