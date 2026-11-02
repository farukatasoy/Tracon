using System.Data.Common;

namespace Tracon;

/// <summary>
/// Tells a persistence store that could not answer apart from every other failure.
/// </summary>
/// <remarks>
/// <para>
/// Two places have to agree on the question. <see cref="CompositeAgentCatalog"/> wraps what an agent source throws as "the
/// agent could not be compiled" (a <c>400</c>), except a store that did not answer: the caller would go and fix an agent
/// that was fine. The host turns what reaches it into a <c>503</c> that says the store did not answer. Each used to ask for
/// itself, and only a provider's <see cref="DbException"/> counted, so a pool that ran dry or a connect that timed out was a
/// <c>400</c> from the catalog and a bodyless <c>500</c> from everything else.
/// </para>
/// <para>
/// A store fails in three ways that reach the caller in a different type:
/// </para>
/// <list type="bullet">
///   <item><description>
///   The provider's own exception, a <see cref="DbException"/>: a command that failed, a refused connection, and the pool
///   that ran dry on PostgreSQL (Npgsql throws it with a <see cref="TimeoutException"/> inside).
///   </description></item>
///   <item><description>
///   A <see cref="TimeoutException"/>: SqlClient throws it when a connection is not opened within the connect timeout and
///   the pool is not in use.
///   </description></item>
///   <item><description>
///   An <see cref="InvalidOperationException"/> that names the connection pool: SqlClient throws it when every pooled
///   connection is in use for the whole connect timeout. The type says nothing (a closed connection is the same type and is
///   a bug of the caller), so the text is read. It is the driver's English text; a host that runs the driver with another UI
///   culture gets the old answer for this one case, not a wrong one.
///   </description></item>
/// </list>
/// <para>
/// An <see cref="IOException"/> is not on the list although a dead network ends in one. The request side raises the same
/// type for a client that went away and for a malformed request, and both providers wrap their own network fault in their
/// <see cref="DbException"/>. Counting the type would turn a client's disconnect into a store alarm.
/// </para>
/// <para>
/// Only the exception that was caught is looked at, not the exceptions inside it: a <see cref="TraconException"/> is
/// already Tracon's normalized error, and reading through it would turn a compilation error into a <c>503</c>.
/// </para>
/// </remarks>
internal static class StoreFailure
{
    /// <summary>Says whether <paramref name="exception"/> is a store that did not answer.</summary>
    /// <param name="exception">The exception that was caught.</param>
    /// <returns><see langword="true"/> for a store failure.</returns>
    public static bool Is(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            DbException => true,
            TimeoutException => true,
            InvalidOperationException => NamesConnectionPool(exception.Message),
            _ => false,
        };
    }

    // SqlClient: "...prior to obtaining a connection from the pool. This may have occurred because all pooled connections
    // were in use and max pool size was reached." Older Npgsql: "The connection pool has been exhausted, ...".
    private static bool NamesConnectionPool(string message)
        => message.Contains("connection from the pool", StringComparison.OrdinalIgnoreCase)
           || message.Contains("pool has been exhausted", StringComparison.OrdinalIgnoreCase);
}
