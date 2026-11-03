namespace Tracon;

/// <summary>
/// Gives the host the SQL provider's answer to "did the store reject this value", read from the provider's own
/// <see cref="SqlDialect"/>.
/// </summary>
/// <remarks>
/// The error numbers belong to the dialect, as every other classifier of a provider's failure does
/// (<see cref="SqlDialect.IsUniqueViolation"/>, <see cref="SqlDialect.IsDeadlock"/>); this type only turns two of them
/// into the seam the host resolves. The host cannot name a provider's exception type, and the linked source cannot be
/// resolved by its own name when a consumer references more than one provider.
/// </remarks>
internal sealed class SqlStoreDataFaultClassifier(SqlDialect dialect) : IStoreDataFaultClassifier
{
    /// <inheritdoc />
    public bool TryClassify(Exception exception, out StoreDataFault fault)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (dialect.IsTextTooLong(exception))
        {
            fault = StoreDataFault.TextTooLong;
            return true;
        }

        if (dialect.IsNumberOutOfRange(exception))
        {
            fault = StoreDataFault.NumberOutOfRange;
            return true;
        }

        fault = default;
        return false;
    }
}
