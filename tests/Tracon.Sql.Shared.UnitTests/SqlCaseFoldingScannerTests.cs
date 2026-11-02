namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The scanner behind <see cref="MigrationCaseFoldingTests"/>, on SQL written to show what it must not be fooled by.
/// </summary>
/// <remarks>
/// The first version of the scan cut comments with a regular expression and matched parentheses by counting them, over text
/// that still held its string literals. Three things got past it, each measured on a crafted line: a parenthesis inside a
/// literal moved the end of a call to the end of the statement, so the <c>COLLATE</c> of the call after it was counted for
/// both; a <c>--</c> inside a literal cut the rest of the line, calls included; and a <c>COLLATE</c> anywhere in the call, even
/// in a sub-expression of its argument, passed it.
/// </remarks>
public sealed class SqlCaseFoldingScannerTests
{
    private static int Bare(string sql, bool bracketIdentifiers = false)
        => SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers).Count(static call => !call.NamesOrdinalCollation);

    [Theory]
    [InlineData("SELECT lower(a COLLATE \"C\") FROM t", 0)]
    [InlineData("SELECT lower(a) FROM t", 1)]
    [InlineData("WHERE UPPER(f(x)) = 'A'", 1)]
    [InlineData("SELECT lower(a COLLATE \"C\"), upper(b COLLATE \"POSIX\"), lower(c)", 1)]
    [InlineData("SELECT lower(a collate \"C\") FROM t", 0)]
    [InlineData("SELECT lower(a COLLATE \"en_US\") FROM t", 1)]
    public void A_call_is_ordinal_only_when_its_argument_says_so(string sql, int bare)
        => Bare(sql).ShouldBe(bare);

    [Theory]
    [InlineData("WHERE LOWER(x.[key] COLLATE Latin1_General_BIN2) = N'a'", 0)]
    [InlineData("WHERE LOWER(x.[key] COLLATE Latin1_General_BIN) = N'a'", 0)]
    [InlineData("WHERE LOWER(x.[key] COLLATE Turkish_CI_AS) = N'a'", 1)]
    [InlineData("WHERE LOWER(x.[key]) = N'a' COLLATE Latin1_General_BIN2", 1)]
    public void Collation_names_are_read_as_SQL_Server_writes_them(string sql, int bare)
        => Bare(sql, bracketIdentifiers: true).ShouldBe(bare);

    [Theory]
    [InlineData("-- lower(a) would be flagged\nSELECT 1")]
    [InlineData("SELECT 1 -- upper(a)")]
    [InlineData("/* lower(a) */ SELECT 1")]
    [InlineData("/* outer /* lower(a) */ still inside upper(b) */ SELECT 1")]
    [InlineData("SELECT 'lower(a)'")]
    [InlineData("SELECT N'text with upper(x) in it'")]
    public void A_call_in_a_comment_or_in_a_string_that_is_only_text_is_not_a_call(string sql)
        => SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: false).ShouldBeEmpty();

    [Fact]
    public void A_comment_inside_the_argument_does_not_hide_its_collation()
        => Bare("SELECT lower(a /* the key */ COLLATE \"C\" -- folded as bytes\n) FROM t").ShouldBe(0);

    /// <summary>
    /// The first gap. A '(' inside a literal opened a level that nothing closed, so the first call ran to the end of the
    /// text and the <c>COLLATE</c> of the next call was counted for it.
    /// </summary>
    [Theory]
    [InlineData("SELECT lower(replace(a, '(', '')), lower(b COLLATE \"C\") FROM t", 1)]
    [InlineData("SELECT lower(replace(a, ')', '') COLLATE \"C\"), lower(b) FROM t", 1)]
    [InlineData("SELECT lower(replace(a, '(', '') COLLATE \"C\") FROM t", 0)]
    public void A_parenthesis_inside_a_string_literal_does_not_move_the_end_of_a_call(string sql, int bare)
        => Bare(sql).ShouldBe(bare);

    /// <summary>
    /// The second gap. A '--' inside a literal was read as a comment, and the rest of the line, with the calls on it, was
    /// cut before it was scanned.
    /// </summary>
    [Theory]
    [InlineData("SELECT lower(a COLLATE \"C\") || '--', lower(b) FROM t", 1)]
    [InlineData("SELECT '-- not a comment', lower(b) FROM t", 1)]
    [InlineData("SELECT '/* not a comment', lower(b) FROM t /* a real one */", 1)]
    [InlineData("SELECT lower(a COLLATE \"C\") || '--' FROM t", 0)]
    public void A_comment_marker_inside_a_string_literal_does_not_cut_the_rest_of_the_line(string sql, int bare)
        => Bare(sql).ShouldBe(bare);

    /// <summary>
    /// The third gap. A <c>COLLATE</c> anywhere in the text of a call passed it, though it belonged to a sub-expression and
    /// the folding of the whole still follows the locale.
    /// </summary>
    [Theory]
    [InlineData("SELECT lower(f(a COLLATE \"C\")) FROM t", 1)]
    [InlineData("SELECT lower(CASE WHEN a COLLATE \"C\" = 'X' THEN b ELSE c END) FROM t", 1)]
    [InlineData("SELECT lower(a COLLATE \"C\" || b) FROM t", 1)]
    [InlineData("SELECT lower(f(a) COLLATE \"C\") FROM t", 0)]
    [InlineData("SELECT lower(f(a COLLATE \"C\") COLLATE \"C\") FROM t", 0)]
    public void A_collation_of_another_sub_expression_does_not_pass_the_call(string sql, int bare)
        => Bare(sql).ShouldBe(bare);

    [Fact]
    public void A_call_inside_another_is_judged_on_its_own_argument()
    {
        Bare("SELECT lower(upper(a COLLATE \"C\") COLLATE \"C\") FROM t").ShouldBe(0);
        Bare("SELECT lower(upper(a) COLLATE \"C\") FROM t").ShouldBe(1);
        Bare("SELECT lower(upper(a COLLATE \"C\")) FROM t").ShouldBe(1);
    }

    [Fact]
    public void A_call_that_never_closes_is_not_given_the_benefit_of_the_doubt()
        => Bare("SELECT lower(a COLLATE \"C\"").ShouldBe(0, "the argument still ends in the collation; the missing ')' is the database's to refuse");

    [Fact]
    public void A_quoted_identifier_with_odd_characters_does_not_end_the_call()
    {
        Bare("SELECT lower(\"we(ird--name\" COLLATE \"C\") FROM t").ShouldBe(0);
        Bare("SELECT lower([we)ird'name] COLLATE Latin1_General_BIN2) FROM t", bracketIdentifiers: true).ShouldBe(0);
        Bare("SELECT lower(\"we(ird--name\") FROM t").ShouldBe(1);
    }

    /// <summary>
    /// SQL Server compiles a batch up front, so a migration that reads a column it is about to drop runs the statement as a
    /// string. The calls in it are calls: blanking the literal would lose them.
    /// </summary>
    [Theory]
    [InlineData("EXEC(N'SELECT LOWER(x) FROM t');", 1)]
    [InlineData("EXEC(N'SELECT LOWER(x COLLATE Latin1_General_BIN2) FROM t WHERE y = N''abc''');", 0)]
    [InlineData("EXEC (N'UPDATE t SET a = UPPER(a)');", 1)]
    [InlineData("EXECUTE(N'UPDATE t SET a = UPPER(a COLLATE Latin1_General_BIN2)');", 0)]
    [InlineData("EXEC sp_executesql N'SELECT LOWER(x) FROM t';", 1)]
    [InlineData("DECLARE @sql nvarchar(max) = N'SELECT LOWER(x) FROM t'; EXEC(@sql);", 1)]
    [InlineData("EXECUTE format('SELECT lower(%I) FROM t', 'x');", 1)]
    public void A_call_in_the_string_a_migration_executes_is_found(string sql, int bare)
        => Bare(sql, bracketIdentifiers: true).ShouldBe(bare);

    [Fact]
    public void A_literal_inside_the_executed_string_is_text_again_and_its_calls_are_not_calls()
    {
        // Two levels: the string is SQL, and the doubled quotes inside it make a literal of the SQL it holds.
        var sql = "EXEC(N'SELECT LOWER(x COLLATE Latin1_General_BIN2), N''LOWER(y) -- text'' FROM t');";

        var calls = SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: true);

        calls.ShouldHaveSingleItem().NamesOrdinalCollation.ShouldBeTrue();
        calls[0].InDynamicSql.ShouldBeTrue();
    }

    [Fact]
    public void A_call_in_a_string_inside_a_string_is_found_at_every_depth()
    {
        var sql = "EXEC(N'EXEC(N''SELECT LOWER(x) FROM t'')');";

        SqlCaseFoldingScanner.Scan(sql, bracketIdentifiers: true).ShouldHaveSingleItem().NamesOrdinalCollation.ShouldBeFalse();
    }

    [Fact]
    public void A_report_shows_the_call_as_written()
    {
        var calls = SqlCaseFoldingScanner.Scan("SELECT 1; SELECT lower(a /* c */) FROM t", bracketIdentifiers: false);

        calls.ShouldHaveSingleItem().Text.ShouldBe("lower(a /* c */)");
    }
}
