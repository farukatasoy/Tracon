namespace Tracon.Testing;

/// <summary>
/// Thrown when a <see cref="RunAssertions"/> assertion is not met.
/// </summary>
/// <remarks>
/// Every test framework (xunit, NUnit, MSTest) counts a thrown exception as a
/// test failure; the package binds to no other framework.
/// </remarks>
public sealed class TraconAssertionException : TraconException
{
    /// <summary>Creates a new assertion failure.</summary>
    public TraconAssertionException()
    {
    }

    /// <summary>Creates a new assertion failure.</summary>
    /// <param name="message">Message describing the expected and actual value.</param>
    public TraconAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new assertion failure.</summary>
    /// <param name="message">Message describing the expected and actual value.</param>
    /// <param name="innerException">The underlying error.</param>
    public TraconAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
