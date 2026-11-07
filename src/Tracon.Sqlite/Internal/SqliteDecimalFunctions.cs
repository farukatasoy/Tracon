using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using SQLitePCL;

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
/// <para>
/// They are registered on the NATIVE connection (<c>sqlite3_create_function</c>),
/// once, and not through <c>SqliteConnection.CreateFunction</c>. The driver takes every function it registered off again when
/// a connection closes, and the pool hands the same native connection to the next open: registered the driver's way, every
/// open paid about 1.9 KB to put the two functions back, and every store call opens a connection. A function registered
/// natively stays while the pool keeps the connection; a connection the pool let go is a new native connection, and
/// <see cref="Register"/> finds it unknown and registers again.
/// </para>
/// </remarks>
internal static class SqliteDecimalFunctions
{
    /// <summary>The name of the aggregate.</summary>
    internal const string SumName = "tracon_decimal_sum";

    /// <summary>The name of the scalar.</summary>
    internal const string AddName = "tracon_decimal_add";

    // The text of a decimal the way the driver writes one: a sign, digits, a point, an exponent. A thousands separator, a
    // trailing sign or blanks are not part of it, and read leniently they would turn a hand-written '1,5' into 15.
    private const NumberStyles Style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    // The native connections that already carry both functions. Keyed on the handle OBJECT: the driver's pool hands the same
    // handle object to every open of one native connection, and a connection the pool drops takes its entry with it.
    private static readonly ConditionalWeakTable<sqlite3, object> Registered = new();

    private static readonly object RegisteredMarker = new();

    // Held in static fields so the delegates the native connection calls stay alive for as long as the process does.
    private static readonly delegate_function_aggregate_step SumStepDelegate = SumStep;

    private static readonly delegate_function_aggregate_final SumFinalDelegate = SumFinal;

    private static readonly delegate_function_scalar AddDelegate = AddScalar;

    /// <summary>
    /// Registers both functions on the native connection behind an open connection, unless that native connection has them
    /// already, which is every open after the first of a pooled connection.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    internal static void Register(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var handle = connection.Handle
            ?? throw new InvalidOperationException("The decimal functions can only be registered on an open connection.");

        if (Registered.TryGetValue(handle, out _))
        {
            return;
        }

        // SQLite replaces a function of the same name and arity, so a half-done registration (the second call failed) is
        // simply done again by the next open: the handle is added only after both calls went through.
        var flags = raw.SQLITE_UTF8 | raw.SQLITE_DETERMINISTIC;

        SqliteException.ThrowExceptionForRC(
            raw.sqlite3_create_function(handle, SumName, -1, flags, null, SumStepDelegate, SumFinalDelegate),
            handle);
        SqliteException.ThrowExceptionForRC(
            raw.sqlite3_create_function(handle, AddName, 2, flags, null, AddDelegate),
            handle);

        Registered.TryAdd(handle, RegisteredMarker);
    }

    private static void SumStep(sqlite3_context context, object? userData, sqlite3_value[] arguments)
    {
        // The running total is per call of the aggregate (one per group, one per use in a statement), so it lives on the
        // call's own context and not on the registration.
        var state = context.state as SumState ?? (SumState)(context.state = new SumState());

        // The first failure ends the sum: the rows after it are not read, and the final step reports the failure.
        if (state.Failure is not null)
        {
            return;
        }

        try
        {
            foreach (var argument in arguments)
            {
                if (TryRead(argument, out var value))
                {
                    state.Total += value;
                    state.Any = true;
                }
            }
        }
        catch (Exception ex)
        {
            // Nothing may leave a native callback (see AddScalar); the final step reports the failure.
            state.Failure = Describe(ex);
        }
    }

    private static void SumFinal(sqlite3_context context, object? userData)
    {
        if (context.state is not SumState state)
        {
            // No row reached the step: a set with nothing to add yields NULL, as the built-in SUM does.
            raw.sqlite3_result_null(context);

            return;
        }

        if (state.Failure is { } failure)
        {
            raw.sqlite3_result_error(context, failure);

            return;
        }

        if (state.Any)
        {
            raw.sqlite3_result_text(context, Format(state.Total));
        }
        else
        {
            raw.sqlite3_result_null(context);
        }
    }

    private static void AddScalar(sqlite3_context context, object? userData, sqlite3_value[] arguments)
    {
        try
        {
            // A NULL on either side makes the result NULL before the other side is read, as `+` does: 'abc' + NULL is NULL.
            if (raw.sqlite3_value_type(arguments[0]) == raw.SQLITE_NULL || raw.sqlite3_value_type(arguments[1]) == raw.SQLITE_NULL)
            {
                raw.sqlite3_result_null(context);

                return;
            }

            TryRead(arguments[0], out var left);
            TryRead(arguments[1], out var right);

            raw.sqlite3_result_text(context, Format(left + right));
        }
        catch (Exception ex)
        {
            // Nothing may leave a native callback: an exception that did would cross the native frame. It becomes the
            // error of the statement.
            raw.sqlite3_result_error(context, Describe(ex));
        }
    }

    /// <summary>Says what went wrong in a text of our own: the text of the exception is the framework's and can hold the value.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The text of the statement's error.</returns>
    private static string Describe(Exception exception)
        => exception switch
        {
            FormatException => "A decimal function was given text that is not a decimal number.",
            OverflowException => "A decimal function produced a number outside the range of a decimal.",
            ArgumentException => "A decimal function was given a value that is neither text, an integer nor a real.",
            _ => "A decimal function failed.",
        };

    /// <summary>Reads one SQL value as a <see cref="decimal"/>; <see langword="false"/> for a <c>NULL</c>.</summary>
    /// <param name="value">The value SQLite hands to the function.</param>
    /// <param name="result">The decimal, when the value is not <c>NULL</c>.</param>
    /// <returns><see langword="true"/> when the value is a number; <see langword="false"/> for <c>NULL</c>.</returns>
    /// <exception cref="ArgumentException">The value is neither text, an integer nor a real.</exception>
    /// <exception cref="FormatException">The text is not a decimal number.</exception>
    /// <exception cref="OverflowException">The number does not fit a <see cref="decimal"/>.</exception>
    private static bool TryRead(sqlite3_value value, out decimal result)
    {
        switch (raw.sqlite3_value_type(value))
        {
            case raw.SQLITE_NULL:
                result = default;

                return false;

            case raw.SQLITE_INTEGER:
                result = raw.sqlite3_value_int64(value);

                return true;

            case raw.SQLITE_FLOAT:
                result = (decimal)raw.sqlite3_value_double(value);

                return true;

            case raw.SQLITE_TEXT:
                result = decimal.Parse(raw.sqlite3_value_text(value).utf8_to_string(), Style, CultureInfo.InvariantCulture);

                return true;

            default:
                throw new ArgumentException("The value is not text, an integer or a real.", nameof(value));
        }
    }

    /// <summary>Writes a decimal the way the driver writes one: invariant, no exponent.</summary>
    /// <param name="value">The decimal.</param>
    /// <returns>The text.</returns>
    internal static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The running total of one call of the aggregate.</summary>
    private sealed class SumState
    {
        /// <summary>Gets or sets the sum so far.</summary>
        internal decimal Total { get; set; }

        /// <summary>Gets or sets a value indicating whether a value other than <c>NULL</c> has been added.</summary>
        internal bool Any { get; set; }

        /// <summary>Gets or sets the text of the first failure; once set, the sum reads no more rows.</summary>
        internal string? Failure { get; set; }
    }
}
