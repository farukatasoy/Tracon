namespace AgentPrism;

/// <summary>Represents a normalized failure from an <see cref="IAgentSource"/>.</summary>
public sealed class AgentPrismAgentSourceException : AgentPrismException
{
    /// <summary>The stable code for an ordinary source failure.</summary>
    public const string SourceFailedErrorType = "agent_source_failed";

    /// <summary>The stable code for a source contract violation.</summary>
    public const string SourceContractErrorType = "agent_source_contract";

    /// <summary>Creates a normalized agent-source exception.</summary>
    public AgentPrismAgentSourceException(string sourceName, string errorType, string message, Exception? innerException = null)
        : base(message, innerException ?? new InvalidOperationException(message))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorType);
        SourceName = sourceName;
        ErrorType = errorType;
    }

    /// <summary>Gets the stable name of the source that failed.</summary>
    public string SourceName { get; }

    /// <inheritdoc />
    public override string ErrorType { get; }
}
