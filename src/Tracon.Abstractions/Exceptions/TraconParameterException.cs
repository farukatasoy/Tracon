namespace Tracon;

/// <summary>
/// Thrown when parameter values do not satisfy an agent's parameter schema: a required value is
/// missing, a name is not declared, or a value is too long.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAgentCatalog.ResolveAsync"/> throws it before it compiles anything, so no provider
/// call happens and no run row is written. The three lists carry the same names the HTTP
/// <c>400</c> carries in <c>missingParameters</c>, <c>unknownParameters</c> and
/// <c>tooLongParameters</c>, so a caller branches on the names instead of parsing the message.
/// </para>
/// <para>
/// The message and the lists name parameters only, never values: a value is the caller's content,
/// and an exception message reaches logs and job failure reasons.
/// </para>
/// </remarks>
public sealed class TraconParameterException : TraconException
{
    /// <summary>
    /// The stable value written to <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string InvalidParametersErrorType = TraconErrorTypes.InvalidParameters;

    /// <summary>Creates a new parameter error.</summary>
    /// <param name="message">The error message. It names parameters and never repeats a value.</param>
    /// <param name="missingParameters">The required parameters that have no value and no default.</param>
    /// <param name="unknownParameters">The supplied names the schema does not declare.</param>
    /// <param name="tooLongParameters">The parameters whose value is over the length limit.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public TraconParameterException(
        string message,
        IReadOnlyList<string> missingParameters,
        IReadOnlyList<string> unknownParameters,
        IReadOnlyList<string> tooLongParameters)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(missingParameters);
        ArgumentNullException.ThrowIfNull(unknownParameters);
        ArgumentNullException.ThrowIfNull(tooLongParameters);

        MissingParameters = [.. missingParameters];
        UnknownParameters = [.. unknownParameters];
        TooLongParameters = [.. tooLongParameters];
    }

    /// <summary>Gets the required parameters that have no value and no default.</summary>
    public IReadOnlyList<string> MissingParameters { get; }

    /// <summary>Gets the supplied names the schema does not declare.</summary>
    public IReadOnlyList<string> UnknownParameters { get; }

    /// <summary>Gets the parameters whose value is over the length limit.</summary>
    public IReadOnlyList<string> TooLongParameters { get; }

    /// <inheritdoc />
    public override string ErrorType => InvalidParametersErrorType;
}
