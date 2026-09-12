using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>How tools are handled during replay.</summary>
/// <remarks>
/// Written and read <strong>as a name</strong> in JSON. Without the
/// converter, the minimal API cannot resolve the body, and the request fails
/// with an <em>empty-bodied</em> <c>400</c> — the error message does not say
/// why. The same note applies to <see cref="RunScoreKind"/>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ReplayToolMode>))]
public enum ReplayToolMode
{
    /// <summary>
    /// Tools are never bound; only the model response is produced. Skills and
    /// callable child agents are also disabled — both appear to the model as
    /// a tool.
    /// </summary>
    NoTools = 0,

    /// <summary>
    /// Recorded tool results are played back; no tool body runs. A call with
    /// no match <strong>stops</strong> the replay.
    /// </summary>
    ReplayTools = 1,

    /// <summary>
    /// Tools actually run and produce side effects. If any tool requires
    /// approval, the request is rejected; the endpoint also requires the
    /// <c>Admin</c> role.
    /// </summary>
    LiveTools = 2,
}

/// <summary>A replay request.</summary>
/// <remarks>
/// Replay <strong>keeps the input, changes the conditions</strong>. The input
/// messages and tenant cannot be changed: a different input is a new run, and
/// crossing the tenant boundary is a security violation.
/// </remarks>
public sealed record RunReplayRequest
{
    /// <summary>
    /// The agent definition version to use. The currently active version if not given.
    /// </summary>
    /// <remarks>
    /// Code-sourced agents have no version history; the request is rejected
    /// if a value is given.
    /// </remarks>
    public int? AgentVersion { get; init; }

    /// <summary>
    /// The model name to override with. The definition's own model is used if not given.
    /// </summary>
    /// <remarks>
    /// Only the model <em>name</em> is overridden; the provider and
    /// credentials come from the definition. Changing the provider is a new definition.
    /// </remarks>
    public string? ModelId { get; init; }

    /// <summary>The tool behavior. Defaults to <see cref="ReplayToolMode.ReplayTools"/>.</summary>
    public ReplayToolMode ToolMode { get; init; } = ReplayToolMode.ReplayTools;
}

/// <summary>
/// Replay could not find a recorded tool result.
/// </summary>
/// <remarks>
/// Silently skipping or running live is <strong>rejected</strong>: the
/// first produces a gap the model cannot see and silently corrupts the
/// result; the second produces a side effect the user did not ask for. The
/// endpoint turns this exception into a <c>422</c> and writes which tool
/// failed to match with which arguments.
/// </remarks>
public sealed class ReplayToolMismatchException : TraconException
{
    /// <summary>
    /// The stable value written for <see cref="TraconException.ErrorType"/>.
    /// </summary>
    public const string ReplayToolMismatchErrorType = "replay_tool_mismatch";

    /// <summary>Creates a new mismatch error.</summary>
    public ReplayToolMismatchException()
    {
    }

    /// <summary>Creates a new mismatch error.</summary>
    /// <param name="message">The error message.</param>
    public ReplayToolMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new mismatch error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public ReplayToolMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The name of the unmatched tool.</summary>
    public string? ToolName { get; init; }

    /// <summary>The unmatched call's arguments.</summary>
    public string? Arguments { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ReplayToolMismatchErrorType;

    /// <summary>Produces an error with a standard message for an unmatched call.</summary>
    /// <param name="toolName">The tool called by the model.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The error.</returns>
    public static ReplayToolMismatchException For(string toolName, string? arguments)
        => new(
            $"Replay stopped: tool '{toolName}' was called with arguments '{arguments ?? "(no arguments)"}' " +
            "but the source run has no recorded result for this call. " +
            "This means the new version calls a different tool; this is an expected " +
            "outcome and shows that the behavior genuinely changed. To actually run the tool, " +
            "set 'toolMode' to 'LiveTools'.")
        {
            ToolName = toolName,
            Arguments = arguments,
        };
}
