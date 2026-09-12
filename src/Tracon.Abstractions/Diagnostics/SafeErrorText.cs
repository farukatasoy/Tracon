namespace Tracon;

/// <summary>
/// Produces text that is safe to write to a persistent field or an external
/// response, from an exception that may carry provider, network, or
/// third-party detail.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="TraconException"/> is ours: its message is part of the
/// contract (<see cref="TraconException.ErrorType"/> is a stable code), and is
/// preserved as is. Any other exception's message is never written to a
/// persistent field or an external response — a foreign message can carry a
/// request detail, an internal URL, a <c>host:port</c>, or a partial credential
/// that the exception's own producer (a provider SDK, a webhook target, an OAuth
/// endpoint) put there without Tracon's control. The safe text carries only
/// the exception's type name and a correlation id.
/// </para>
/// <para>
/// The full exception (<c>exception.ToString()</c>, inner chain included) still
/// goes to <c>ILogger</c> at the call site, tagged with the same correlation id
/// <see cref="NewCorrelationId"/> produced, so an operator can find it.
/// <see cref="OperationCanceledException"/> is not this method's concern — every
/// call site already filters it out before reaching here.
/// </para>
/// </remarks>
public static class SafeErrorText
{
    /// <summary>Produces text that is safe to write to a persistent field or an external response.</summary>
    /// <param name="exception">The caught exception. Never <see langword="null"/>.</param>
    /// <param name="correlationId">
    /// The id also written to the log entry that carries this exception's full detail, from
    /// <see cref="NewCorrelationId"/>.
    /// </param>
    public static string ForPersistence(Exception exception, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(correlationId);

        return exception is TraconException
            ? exception.Message
            : $"{exception.GetType().Name} failed. (ref: {correlationId})";
    }

    /// <summary>Produces a new correlation id to tag a log entry and the safe text derived from it.</summary>
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..8];
}
