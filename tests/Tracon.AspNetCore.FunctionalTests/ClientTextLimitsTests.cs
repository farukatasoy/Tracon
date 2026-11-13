using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The gate over the text a client chooses and the width of the column that stores it: every narrow text column of the
/// SQL Server schema has a row in the table of <see cref="ClientTextLimits"/>, and every row that says a client fills the
/// column names an input that exists.
/// </summary>
/// <remarks>
/// <para>
/// The schema is read from the migrations, in order, so a column that a later migration widens, narrows or drops is
/// judged by its final width. A migration that adds a text column without a row fails the first test here; the author
/// answers once, for that column, who chooses the text and how long it can be, instead of the first client that sends too
/// much finding out as a <c>503</c> on one store and a quiet <c>200</c> on another.
/// </para>
/// <para>
/// The inputs a row names are checked to exist, not to enforce: that every one of them answers <c>400</c> over its limit is
/// the job of <c>ClientTextLengthHttpTests</c>, over HTTP, for a set of them that includes a route value, a body
/// property of each kind of type and a form file name.
/// </para>
/// </remarks>
public sealed partial class ClientTextLimitsTests
{
    /// <summary>
    /// The route parameters of a <c>PUT</c> that the table does not list, and why. A <c>PUT</c> with a parameter in neither
    /// place is a client-chosen name nobody asked about.
    /// </summary>
    private static readonly Dictionary<string, string> UnlimitedPutParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slug"] = "A tenant identifier: checked against its alphabet and 64 characters before the handler runs.",
        ["tenantId"] = "A tenant identifier: checked against its alphabet and 64 characters before the handler runs.",
    };

    [Fact]
    public void The_reader_of_the_migrations_finds_the_columns_whose_width_the_migrations_change_later()
    {
        var schema = ReadSchema();

        schema["agent_definitions.name"].ShouldBe(200);
        schema["attachments.file_name"].ShouldBe(400);
        schema["mcp_servers.endpoint"].ShouldBe(2000);
        schema["run_scores.text_value"].ShouldBe(256);

        // Dropped by a later migration: not in the schema any more.
        schema.ShouldNotContainKey("mcp_servers.authorization_configuration_key");
        schema.Count.ShouldBeGreaterThan(150);
    }

    [Fact]
    public void Every_text_column_of_the_schema_has_a_row_and_every_row_has_a_column()
    {
        var schema = ReadSchema();
        var rows = ClientTextLimits.Columns;

        var withoutRow = schema.Keys
            .Where(column => !rows.Any(row => Matches(row, column)))
            .Order(StringComparer.Ordinal)
            .ToList();

        withoutRow.ShouldBeEmpty(
            "Add a row for each of these columns to ClientTextColumns: who chooses the text, and what keeps it inside the column.");

        var withoutColumn = rows
            .Where(row => !schema.Keys.Any(column => Matches(row, column)))
            .Select(static row => row.Column)
            .ToList();

        withoutColumn.ShouldBeEmpty("These rows name a column that the migrations no longer create.");

        rows.GroupBy(static row => row.Column, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ShouldBeEmpty("A column has one row.");
    }

    [Fact]
    public void A_row_for_a_column_a_client_fills_names_an_input_and_a_limit_the_column_holds()
    {
        var schema = ReadSchema();

        foreach (var row in ClientTextLimits.Columns.Where(static row => row.Origin == ColumnOrigin.Client))
        {
            var width = schema[row.Column];

            row.Limit.ShouldBeGreaterThan(0, row.Column);
            row.Limit.ShouldBeLessThanOrEqualTo(width, $"{row.Column} limits a value to more than the column holds.");
            (row.Route.Count + row.Body.Count + row.Form.Count).ShouldBeGreaterThan(0, $"{row.Column} names no input.");

            foreach (var (type, property) in row.Body)
            {
                var member = type.GetProperty(property);

                member.ShouldNotBeNull($"{row.Column} names {type.Name}.{property}, which does not exist.");
                member.PropertyType.ShouldBe(typeof(string), $"{type.Name}.{property} is not a text.");
            }
        }
    }

    [Fact]
    public void A_row_for_a_column_nobody_limits_says_why()
    {
        foreach (var row in ClientTextLimits.Columns.Where(static row => row.Origin != ColumnOrigin.Client))
        {
            row.Note.ShouldNotBeNullOrWhiteSpace(row.Column);
            row.Limit.ShouldBe(0, $"{row.Column} states a limit but says no client fills it.");
            (row.Route.Count + row.Body.Count + row.Form.Count).ShouldBe(0, $"{row.Column} names an input but says no client fills it.");
        }
    }

    [Fact]
    public void A_column_that_follows_another_follows_one_that_is_limited_at_its_input()
    {
        var rows = ClientTextLimits.Columns;

        foreach (var row in rows.Where(static row => row.Origin == ColumnOrigin.Follows))
        {
            var source = rows.SingleOrDefault(candidate => string.Equals(candidate.Column, row.Note, StringComparison.Ordinal));

            source.ShouldNotBeNull($"{row.Column} follows {row.Note}, which has no row.");
            source.Origin.ShouldBe(ColumnOrigin.Client, $"{row.Column} follows {row.Note}, which no input limits.");
        }
    }

    [Fact]
    public async Task Every_route_parameter_of_a_PUT_is_in_the_table_or_says_why_it_is_not()
    {
        await using var host = await TraconTestHost.StartAsync();

        var parameters = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods
                && methods.HttpMethods.Contains(HttpMethods.Put, StringComparer.OrdinalIgnoreCase))
            .SelectMany(static endpoint => endpoint.RoutePattern.Parameters.Select(static parameter => parameter.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        parameters.ShouldNotBeEmpty();

        var unexplained = parameters
            .Where(static parameter => ClientTextLimits.RouteLimit(parameter) is null && !UnlimitedPutParameters.ContainsKey(parameter))
            .ToList();

        unexplained.ShouldBeEmpty(
            "A PUT route reads these names from the path and stores them. List the column in ClientTextColumns, or say here why the name is not stored as sent.");

        // The reverse: a row that names a route parameter no PUT route has is a rule nothing applies.
        foreach (var route in ClientTextLimits.Columns.SelectMany(static row => row.Route).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            parameters.Any(parameter => string.Equals(parameter, route, StringComparison.OrdinalIgnoreCase))
                .ShouldBeTrue($"A row names the route parameter '{route}', which no PUT route has.");
        }
    }

    private static bool Matches(ClientTextColumn row, string column)
        => string.Equals(row.Column, column, StringComparison.Ordinal)
            || (row.Column.StartsWith("*.", StringComparison.Ordinal)
                && column.EndsWith(row.Column[1..], StringComparison.Ordinal));

    /// <summary>Reads the final width of every <c>nvarchar</c> column from the SQL Server migrations, in order.</summary>
    private static Dictionary<string, int> ReadSchema()
    {
        var directory = FindMigrations();
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory, "*.sql").Order(StringComparer.Ordinal))
        {
            var sql = BlockComment().Replace(LineComment().Replace(File.ReadAllText(file), string.Empty), string.Empty);

            foreach (var statement in sql.Split(';'))
            {
                if (CreateTable().Match(statement) is { Success: true } create)
                {
                    foreach (var definition in SplitTopLevel(create.Groups["body"].Value))
                    {
                        if (Definition().Match(definition) is { Success: true } column)
                        {
                            columns[$"{create.Groups["table"].Value}.{column.Groups["name"].Value}"] =
                                int.Parse(column.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }

                    continue;
                }

                foreach (Match alter in AlterColumn().Matches(statement))
                {
                    columns[$"{alter.Groups["table"].Value}.{alter.Groups["name"].Value}"] =
                        int.Parse(alter.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                if (AddColumns().Match(statement) is { Success: true } add)
                {
                    foreach (var definition in SplitTopLevel(add.Groups["body"].Value))
                    {
                        if (Definition().Match(definition) is { Success: true } column)
                        {
                            columns[$"{add.Groups["table"].Value}.{column.Groups["name"].Value}"] =
                                int.Parse(column.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                }

                foreach (Match drop in DropColumn().Matches(statement))
                {
                    foreach (var name in drop.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        columns.Remove($"{drop.Groups["table"].Value}.{name.Trim('[', ']')}");
                    }
                }
            }
        }

        return columns;
    }

    private static List<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var index = 0; index < body.Length; index++)
        {
            switch (body[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(body[start..index]);
                    start = index + 1;
                    break;
            }
        }

        if (body[start..].Trim().Length > 0)
        {
            parts.Add(body[start..]);
        }

        return parts;
    }

    private static string FindMigrations()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Tracon.SqlServer", "Migrations");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The SQL Server migrations were not found above the test binary.");
    }

    [GeneratedRegex(@"--[^\n]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"CREATE TABLE\s+\{schema\}\.\[?(?<table>\w+)\]?\s*\((?<body>.*)\)\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"^\s*\[?(?<name>\w+)\]?\s+n?varchar\((?<width>\d+)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Definition();

    [GeneratedRegex(@"ALTER TABLE\s+\{schema\}\.\[?(?<table>\w+)\]?\s+ALTER COLUMN\s+\[?(?<name>\w+)\]?\s+n?varchar\((?<width>\d+)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AlterColumn();

    [GeneratedRegex(@"ALTER TABLE\s+\{schema\}\.\[?(?<table>\w+)\]?\s+ADD\s+(?!CONSTRAINT|PRIMARY|FOREIGN|UNIQUE|CHECK|DEFAULT)(?<body>.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AddColumns();

    [GeneratedRegex(@"ALTER TABLE\s+\{schema\}\.\[?(?<table>\w+)\]?\s+DROP COLUMN\s+(?:IF EXISTS\s+)?(?<names>[\w, \[\]]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DropColumn();
}
