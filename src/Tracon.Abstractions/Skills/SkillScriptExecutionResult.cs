namespace Tracon;

/// <summary>What one skill script run did, as an <see cref="ISkillScriptExecutor"/> reports it.</summary>
/// <remarks>
/// <para>
/// A consistent result has an exit code when <see cref="TimedOut"/> is
/// <see langword="false"/> and no exit code when it is <see langword="true"/>.
/// Tracon refuses any other shape: the tool call fails and is recorded as an
/// executor failure.
/// </para>
/// <para>
/// The duration is not part of the result: Tracon measures it around the call.
/// </para>
/// </remarks>
public sealed record SkillScriptExecutionResult
{
    /// <summary>The script's exit code, or <see langword="null"/> when it timed out.</summary>
    public int? ExitCode { get; init; }

    /// <summary>The script's standard output, at most the request's byte limit.</summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>The script's standard error, at most the request's byte limit.</summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>Gets a value that indicates whether an output stream was cut at its limit.</summary>
    /// <remarks>Do not append a notice to the text; Tracon adds it.</remarks>
    public bool Truncated { get; init; }

    /// <summary>Gets a value that indicates whether the script ran past its timeout and was stopped.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Gets a value that indicates whether the script finished in time with exit code zero.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
