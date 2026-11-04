using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Tracon;

/// <summary>
/// The exact decimal arithmetic every connection of the SQLite data source carries.
/// </summary>
/// <remarks>
/// <para>
/// A cost column is <c>TEXT</c> and holds the text of a <see cref="decimal"/>, but SQLite adds text by turning it into a binary
/// double first: <c>SUM(input_cost)</c> over <c>0.1</c> and <c>0.2</c> is <c>0.30000000000000004</c>, and so is
/// <c>cost + excluded.cost</c> in the quota counter. PostgreSQL and SQL Server add <c>numeric</c> exactly. These two functions
/// give SQLite the same answer, with the same <see cref="decimal"/> the stores use in code:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>tracon_decimal_sum(x1 [, x2 ...])</c> is an aggregate. It adds every argument of every row; a <c>NULL</c> is skipped, and
/// a set with no value to add yields <c>NULL</c>, as the built-in <c>SUM</c> does. The result is decimal text.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>tracon_decimal_add(a, b)</c> adds two values; a <c>NULL</c> on either side makes the result <c>NULL</c>, as <c>+</c> does.
/// The result is decimal text. Because it is one expression, the quota counter keeps its single atomic upsert.
/// </description>
/// </item>
/// </list>
/// <para>
/// A value that is not a number is an error, never a zero: the built-in <c>SUM</c> reads <c>'abc'</c> as <c>0</c> and goes on,
/// and a cost total that did the same would under-report without a sign. An integer or a real is accepted (a real as the
/// 15 digits <see cref="decimal"/> keeps of a double), because an expression such as <c>COALESCE(x, 0)</c> hands one over.
/// </para>
/// <para>
/// The functions exist on the connections <see cref="SqliteDataSource"/> opens and on the <see cref="SqliteConnection"/>
/// a consumer's own data source hands out (Tracon registers them when it opens that connection). A query that names them
/// and runs on any other connection, such as a profiling wrapper that is not a <see cref="SqliteConnection"/>, fails with
/// <c>no such function</c>; it does not fall back to a float sum.
/// </para>
/// </remarks>
internal static class SqliteDecimalFunctions
{
    /// <summary>The name of the aggregate.</summary>
    internal const string SumName = "tracon_decimal_sum";

    /// <summary>The name of the scalar.</summary>
    internal const string AddName = "tracon_decimal_add";

    private const NumberStyles Style = NumberStyles.Number | NumberStyles.AllowExponent;

    /// <summary>Registers both functions on an open connection. Registering again replaces them, so it is safe on a pooled one.</summary>
    /// <param name="connection">The open connection.</param>
    internal static void Register(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.CreateAggregate<Sum, string?>(
            SumName,
            default,
            static (sum, arguments) => sum.Add(arguments),
            static sum => sum.Result,
            isDeterministic: true);

        connection.CreateFunction<object?, object?, string?>(
            AddName,
            static (left, right) => left is null or DBNull || right is null or DBNull
                ? null
                : Format(ToDecimal(left) + ToDecimal(right)),
            isDeterministic: true);
    }

    /// <summary>Reads one SQL value as a <see cref="decimal"/>.</summary>
    /// <param name="value">The value as <c>Microsoft.Data.Sqlite</c> hands it over.</param>
    /// <returns>The decimal.</returns>
    /// <exception cref="ArgumentException">The value is neither text, an integer nor a real.</exception>
    /// <exception cref="FormatException">The text is not a decimal number.</exception>
    internal static decimal ToDecimal(object value)
        => value switch
        {
            string text => decimal.Parse(text, Style, CultureInfo.InvariantCulture),
            long whole => whole,
            double real => (decimal)real,
            _ => throw new ArgumentException(
                $"A decimal function was given a {value.GetType().Name}; it takes text, an integer or a real.",
                nameof(value)),
        };

    /// <summary>Writes a decimal the way the driver writes one: invariant, no exponent.</summary>
    /// <param name="value">The decimal.</param>
    /// <returns>The text.</returns>
    internal static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The running total of one aggregate: the sum and whether any value was added.</summary>
    /// <param name="Total">The sum so far.</param>
    /// <param name="Any">Whether a value other than <c>NULL</c> has been added.</param>
    private readonly record struct Sum(decimal Total, bool Any)
    {
        internal string? Result => Any ? Format(Total) : null;

        internal Sum Add(object?[] arguments)
        {
            var total = Total;
            var any = Any;

            foreach (var argument in arguments)
            {
                if (argument is null or DBNull)
                {
                    continue;
                }

                total += ToDecimal(argument);
                any = true;
            }

            return new Sum(total, any);
        }
    }
}
