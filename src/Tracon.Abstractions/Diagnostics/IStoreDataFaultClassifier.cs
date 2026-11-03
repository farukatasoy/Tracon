namespace Tracon;

/// <summary>
/// The two ways a SQL store can refuse a value it was handed, as opposed to being unable to answer.
/// </summary>
internal enum StoreDataFault
{
    /// <summary>A text did not fit the column that holds it.</summary>
    TextTooLong,

    /// <summary>A number did not fit the precision of the column that holds it.</summary>
    NumberOutOfRange,
}

/// <summary>
/// Tells a store that rejected a value apart from a store that did not answer.
/// </summary>
/// <remarks>
/// <para>
/// <c>StoreFailure</c> asks whether a failure came from the store at all, and the answer for a refused value is
/// yes: SQL Server answered, and what it said was "this text does not fit". The host used to turn that into the same
/// <c>503</c> as a database that was down, so a client retried a request that could never succeed and an operator saw a
/// "database down" alarm for a caller's input. This seam asks the second question — did the store reject the value — and
/// the host answers a <c>400</c> when it did.
/// </para>
/// <para>
/// Each SQL provider registers one; persistence in memory registers none, because it has no column to overflow. The
/// classifier looks only at the exception it is given, not at the exceptions inside it, the same rule as
/// <c>StoreFailure</c>. Cancellation, a timeout and a deadlock are not data faults.
/// </para>
/// </remarks>
internal interface IStoreDataFaultClassifier
{
    /// <summary>Says whether <paramref name="exception"/> is the store rejecting a value.</summary>
    /// <param name="exception">The exception that was caught.</param>
    /// <param name="fault">The kind of rejection when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the store rejected a value.</returns>
    bool TryClassify(Exception exception, out StoreDataFault fault);
}
