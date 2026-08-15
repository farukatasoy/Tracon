namespace AgentPrism;

/// <summary>The result of one skill script execution.</summary>
public sealed record SkillScriptExecutionResult
{
    /// <summary>The process exit code, or <see langword="null"/> on timeout.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Standard output. It is truncated when it exceeds its limit.</summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>Standard error. It is truncated when it exceeds its limit.</summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>Gets a value that indicates whether output reached its limit.</summary>
    public bool Truncated { get; init; }

    /// <summary>Gets a value that indicates whether the timeout elapsed. The process tree is killed then.</summary>
    public bool TimedOut { get; init; }

    /// <summary>The execution duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets a value that indicates whether execution completed successfully.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
