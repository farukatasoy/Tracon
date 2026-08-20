namespace AgentPrism.Testing;

/// <summary>
/// Thrown when a <see cref="RunAssertions"/> assertion is not met.
/// </summary>
/// <remarks>
/// Every test framework (xunit, NUnit, MSTest) counts a thrown exception as a
/// test failure; the package binds to no other framework.
/// </remarks>
public sealed class AgentPrismAssertionException : AgentPrismException
{
    /// <summary>Creates a new assertion failure.</summary>
    public AgentPrismAssertionException()
    {
    }

    /// <summary>Creates a new assertion failure.</summary>
    /// <param name="message">Message describing the expected and actual value.</param>
    public AgentPrismAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new assertion failure.</summary>
    /// <param name="message">Message describing the expected and actual value.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
