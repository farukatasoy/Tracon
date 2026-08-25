namespace AgentPrism;

/// <summary>Represents a normalized failure from an <see cref="IRunJudge"/>.</summary>
public sealed class AgentPrismJudgeException : AgentPrismException
{
    /// <summary>The stable code for an ordinary judge failure.</summary>
    public const string JudgeFailedErrorType = "judge_failed";

    /// <summary>The stable code for a judge call timeout.</summary>
    public const string JudgeTimeoutErrorType = "judge_timeout";

    /// <summary>The stable code for a judge contract violation.</summary>
    public const string JudgeContractErrorType = "judge_contract";

    /// <summary>Creates a normalized judge exception.</summary>
    public AgentPrismJudgeException(string judgeName, string errorType, string message, Exception? innerException = null)
        : base(message, innerException ?? new InvalidOperationException(message))
    {
        JudgeName = judgeName;
        ErrorType = errorType;
    }

    /// <summary>The stable name of the judge that failed.</summary>
    public string JudgeName { get; }

    /// <inheritdoc />
    public override string ErrorType { get; }
}
