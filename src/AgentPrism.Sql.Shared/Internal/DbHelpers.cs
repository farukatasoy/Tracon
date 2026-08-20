using System.Data.Common;

namespace AgentPrism;

/// <summary>
/// Provider-independent helpers that collect the command execution patterns in one place.
/// </summary>
/// <remarks>
/// <para>
/// Library code needs <c>ConfigureAwait(false)</c> on every <c>await</c> and every
/// <c>await using</c> (rule MA0004). Repeating that at every call site would make
/// the stores unreadable; the pattern is written once here.
/// </para>
/// <para>
/// Every helper takes ownership of the command it is given and disposes it when it
/// is done.
/// </para>
/// <para>
/// The types are the ADO.NET base types (<see cref="DbCommand"/>,
/// <see cref="DbDataReader"/>); the same store code therefore runs over PostgreSQL
/// and SQL Server. Everything provider-specific goes through
/// <see cref="SqlDialect"/>.
/// </para>
/// </remarks>
internal static class DbHelpers
{
    /// <summary>Runs the command and returns the number of affected rows.</summary>
    /// <param name="command">The command to run. It is disposed when the call ends.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of affected rows.</returns>
    public static async ValueTask<int> ExecuteAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs the command and returns the first column of the first row.</summary>
    /// <param name="command">The command to run. It is disposed when the call ends.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The single value; <see langword="null"/> when there is no result.</returns>
    /// <remarks>
    /// <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/> looks at the
    /// FIRST result set only. In the SQL Server <c>UPDATE ... OUTPUT</c> +
    /// <c>IF @@ROWCOUNT = 0 INSERT... OUTPUT</c> upsert pattern, when the
    /// UPDATE affects 0 rows the first set is EMPTY and the real value is in the
    /// second set; the same result-set walk as in <see cref="ReadSingleAsync{T}"/>
    /// is therefore done by hand here.
    /// </remarks>
    public static async ValueTask<object?> ExecuteScalarAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                do
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return reader.IsDBNull(0) ? null : reader.GetValue(0);
                    }
                }
                while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

                return null;
            }
        }
    }

    /// <summary>Runs the command and maps the first row.</summary>
    /// <typeparam name="T">The mapped type.</typeparam>
    /// <param name="command">The command to run. It is disposed when the call ends.</param>
    /// <param name="map">The mapper that turns a row into an object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The first row; <see langword="null"/> when the result is empty.</returns>
    /// <remarks>
    /// The SQL Server <c>UPDATE ... OUTPUT</c> + <c>IF @@ROWCOUNT = 0 INSERT
    /// ... OUTPUT</c> upsert pattern writes the row to a DIFFERENT result
    /// set depending on WHICH branch ran: when the UPDATE affects 0 rows the first
    /// result set is EMPTY and the real row is in the second set. When the first
    /// set is empty the following sets are therefore tried with
    /// <see cref="DbDataReader.NextResultAsync(CancellationToken)"/>. The
    /// single-statement PostgreSQL <c>ON CONFLICT ... RETURNING</c> pattern already
    /// produces one set, so the loop is harmless there.
    /// </remarks>
    public static async ValueTask<T?> ReadSingleAsync<T>(
        DbCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
        where T : class
    {
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                do
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return map(reader);
                    }
                }
                while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

                return null;
            }
        }
    }

    /// <summary>Runs the command and maps every row.</summary>
    /// <typeparam name="T">The mapped type.</typeparam>
    /// <param name="command">The command to run. It is disposed when the call ends.</param>
    /// <param name="map">The mapper that turns a row into an object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered result list.</returns>
    public static async ValueTask<List<T>> ReadListAsync<T>(
        DbCommand command,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(map(reader));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Adds a parameter whose type is inferred from the value.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value. It <strong>cannot</strong> be <see langword="null"/>.</param>
    /// <remarks>
    /// <para>
    /// It is only for <em>non-null</em> scalar values whose type can be inferred
    /// safely from the value: <see cref="Guid"/>, <see cref="string"/>,
    /// <see cref="short"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="bool"/>.
    /// </para>
    /// <para>
    /// It is <strong>not used</strong> for timestamps, decimals and values that
    /// can be <c>NULL</c>; those are typed explicitly through
    /// <see cref="SqlDialect"/>. The reason is two traps: when no type is given,
    /// SQL Server treats a <see cref="decimal"/> parameter as <c>decimal(18,0)</c>
    /// and <em>silently truncates</em> the fractional part; and an untyped
    /// <c>NULL</c> gives <c>42P08</c> on PostgreSQL. Both appear at run time only.
    /// </para>
    /// </remarks>
    public static void Add(DbCommand command, string name, object value)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(value);

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Reads a timestamp column as a UTC-stamped value.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The timestamp.</returns>
    /// <remarks>
    /// The PostgreSQL <c>timestamptz</c> and the SQL Server <c>datetimeoffset</c>
    /// columns are both read as <see cref="DateTimeOffset"/>. Because writing is
    /// always in UTC, the offset of the value read back is zero.
    /// </remarks>
    public static DateTimeOffset GetTimestamp(DbDataReader reader, int ordinal)
        => reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Reads a nullable text column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value; <see langword="null"/> when it is <c>NULL</c>.</returns>
    public static string? GetNullableString(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Reads a nullable timestamp column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The timestamp; <see langword="null"/> when it is <c>NULL</c>.</returns>
    public static DateTimeOffset? GetNullableTimestamp(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Reads a nullable decimal column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value; <see langword="null"/> when it is <c>NULL</c>.</returns>
    public static decimal? GetNullableDecimal(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    /// <summary>Reads a nullable identifier column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value; <see langword="null"/> when it is <c>NULL</c>.</returns>
    public static Guid? GetNullableGuid(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    /// <summary>Reads a nullable <c>integer</c> column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value; <see langword="null"/> when it is <c>NULL</c>.</returns>
    public static int? GetNullableInt32(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    /// <summary>Reads a nullable <c>bigint</c> column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The value; <see langword="null"/> when it is <c>NULL</c>.</returns>
    public static long? GetNullableInt64(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    /// <summary>
    /// Converts the raw value returned by <see cref="ExecuteScalarAsync"/> into a <see cref="Guid"/>.
    /// </summary>
    /// <param name="value">The raw scalar value.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// PostgreSQL (<c>uuid</c>) and SQL Server (<c>uniqueidentifier</c>) already
    /// return the scalar result as a boxed <see cref="Guid"/>; SQLite (<c>TEXT</c>)
    /// returns a <see cref="string"/>. A direct <c>(Guid)result</c> cast at the call
    /// site would throw <see cref="InvalidCastException"/> on SQLite. This helper
    /// accepts both.
    /// </remarks>
    public static Guid ToGuid(object value)
        => value is Guid guid ? guid : Guid.Parse((string)value);

    /// <summary>
    /// Converts the raw value returned by <see cref="ExecuteScalarAsync"/> into a
    /// boolean value.
    /// </summary>
    /// <param name="value">The raw scalar value.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// PostgreSQL (<c>boolean</c>) and SQL Server (<c>CAST(... AS bit)</c>)
    /// return the scalar result as a boxed <see cref="bool"/>; SQLite has no boolean
    /// type and a comparison in a <c>RETURNING</c> clause returns a boxed
    /// <see cref="long"/> (0/1). The <c>result is bool b &amp;&amp; b</c> pattern
    /// would ALWAYS give <see langword="false"/> on SQLite (the type never matches).
    /// </remarks>
    public static bool ToBoolean(object value)
        => value switch
        {
            bool boolean => boolean,
            long integer => integer != 0,
            _ => throw new ArgumentException($"'{value.GetType()}' cannot be converted to a boolean value.", nameof(value)),
        };
}
