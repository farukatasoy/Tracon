namespace AgentPrism;

/// <summary>The result of an <see cref="IRunErrorClassifier"/> classification.</summary>
public sealed record RunErrorClassification
{
    /// <summary>The class the error falls into.</summary>
    public required RunErrorClass Class { get; init; }

    /// <summary>
    /// A digest of the normalized error message. Used to cluster repeats of
    /// the same failure; computed after variable parts such as identifiers,
    /// numbers, dates, and quoted text are removed.
    /// </summary>
    public required string Fingerprint { get; init; }
}
