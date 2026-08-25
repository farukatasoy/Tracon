namespace AgentPrism;

/// <summary>Base class for every error AgentPrism raises.</summary>
public class AgentPrismException : Exception
{
    /// <summary>Creates a new error.</summary>
    public AgentPrismException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">
    /// The underlying error, or <see langword="null"/> when there is none to carry.
    /// </param>
    public AgentPrismException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the stable error type name written to the run record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to the full name of the exception type, so today's behaviour does
    /// not change. A derived type overrides this member when the record has to be
    /// machine readable — for example <see cref="AgentPrismContentFilteredException"/>
    /// writes <c>content_filtered</c>. Reports and alert rules can rely on this
    /// stable name instead of an assembly name.
    /// </para>
    /// <para>
    /// The value is written to <c>RunError.Type</c> and <strong>carries no secret</strong>.
    /// </para>
    /// </remarks>
    public virtual string ErrorType => GetType().FullName ?? GetType().Name;
}

/// <summary>
/// Thrown when a model provider cuts the response short with its safety or
/// content filter.
/// </summary>
/// <remarks>
/// <para>
/// A filtered response usually arrives <strong>empty</strong>. Treating that as
/// "succeeded but empty" produces the hardest case to debug: the user sees a blank
/// answer and the record holds no trace. AgentPrism records it as an explicit
/// failure instead — <c>RunError.Type</c> becomes <c>content_filtered</c>.
/// </para>
/// <para>
/// Detection lives in a shared <c>IChatClient</c> decorator inside
/// <c>AgentPrism.Core</c>, not inside the provider packages, so every provider
/// gets the same behaviour.
/// </para>
/// </remarks>
public sealed class AgentPrismContentFilteredException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string ContentFilteredErrorType = "content_filtered";

    /// <summary>Creates a new error.</summary>
    public AgentPrismContentFilteredException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismContentFilteredException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismContentFilteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the provider that filtered the response, or <see langword="null"/> when unknown.</summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Gets the finish reason the provider reported. For example Anthropic
    /// <c>refusal</c> or Gemini <c>SAFETY</c>. <strong>Carries no secret.</strong>
    /// </summary>
    public string? FinishReason { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ContentFilteredErrorType;
}

/// <summary>
/// Thrown when an <see cref="IContentGuard"/> blocks content.
/// </summary>
/// <remarks>
/// <para>
/// This is separate from <see cref="AgentPrismContentFilteredException"/> and must
/// stay separate: that one reports that the <em>provider</em> cut the response,
/// this one reports that AgentPrism's own policy did not let the content through.
/// Writing both to the same stable identity would cost the operator the difference
/// between "the model refused" and "we refused".
/// </para>
/// <para>
/// Neither the message nor the fields <strong>carry the blocked content</strong>.
/// The message can reach the client in a <c>422</c> body; putting sensitive text
/// there would spread the problem.
/// </para>
/// </remarks>
public sealed class AgentPrismContentBlockedException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string ContentBlockedErrorType = "content_blocked";

    /// <summary>Creates a new error.</summary>
    public AgentPrismContentBlockedException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message. MUST NOT carry the blocked text.</param>
    public AgentPrismContentBlockedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message. MUST NOT carry the blocked text.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismContentBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the name of the guard that made the decision.</summary>
    public string? GuardName { get; init; }

    /// <summary>Gets the matched rule name, or <see langword="null"/> when the guard reports none.</summary>
    public string? RuleName { get; init; }

    /// <summary>Gets the direction that was inspected.</summary>
    public ContentGuardDirection Direction { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ContentBlockedErrorType;
}

/// <summary>
/// Thrown when an agent definition cannot be turned into a runnable agent.
/// </summary>
/// <remarks>
/// The most common cause is a tool name in the definition that is not registered
/// in code. That case is never skipped silently — an agent running with an unknown
/// tool is an agent that does not do what the user expects.
/// </remarks>
public sealed class AgentPrismCompilationException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string CompilationFailedErrorType = "compilation_failed";

    /// <summary>Creates a new compilation error.</summary>
    public AgentPrismCompilationException()
    {
    }

    /// <summary>Creates a new compilation error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismCompilationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new compilation error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismCompilationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the name of the agent that failed to compile.</summary>
    public string? AgentName { get; init; }

    /// <inheritdoc />
    public override string ErrorType => CompilationFailedErrorType;
}

/// <summary>
/// Thrown when the circuit breaker has temporarily taken a model provider out of
/// service.
/// </summary>
/// <remarks>
/// Thrown while the circuit is <c>Open</c>; <strong>no request reaches the
/// provider</strong>. The circuit opens once consecutive failures pass
/// <c>FailureThreshold</c>, and moves to half-open for a single trial after
/// <c>BreakDuration</c>.
/// </remarks>
public sealed class AgentPrismProviderUnavailableException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string ProviderUnavailableErrorType = "provider_unavailable";

    /// <summary>Creates a new error.</summary>
    public AgentPrismProviderUnavailableException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismProviderUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the name of the provider whose circuit is open.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Gets the time at which the circuit goes half-open and is retried.</summary>
    public DateTimeOffset? RetryAfter { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ProviderUnavailableErrorType;
}

/// <summary>
/// Thrown when a tool call does not settle within its configured timeout.
/// </summary>
/// <remarks>
/// <para>
/// Thrown by the wrapper installed in the tool registry, never by the tool body
/// itself. Microsoft Agent Framework's function-invoking client catches it and
/// turns it into a tool result carrying the error — the run is
/// <strong>not</strong> dropped, and the model sees a tool failure and can
/// continue the turn.
/// </para>
/// <para>
/// <see cref="CancellationToken"/> is cooperative. A tool body that never
/// reads its token is not forcibly stopped by this timeout — only the
/// <em>wait</em> for it is cut short. The underlying work keeps running in the
/// background until it finishes on its own; this is a documented limit, not a
/// bug.
/// </para>
/// </remarks>
public sealed class AgentPrismToolTimeoutException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string ToolTimeoutErrorType = "tool_timeout";

    /// <summary>Creates a new error.</summary>
    public AgentPrismToolTimeoutException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismToolTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismToolTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the name of the tool that timed out.</summary>
    public string? ToolName { get; init; }

    /// <summary>Gets the timeout that elapsed.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ToolTimeoutErrorType;
}

/// <summary>
/// Thrown for the losing request when two concurrent first requests arrive for the
/// same NEW session id.
/// </summary>
/// <remarks>
/// <para>
/// while creating a new session,
/// <c>AgentSessionManager.GetOrCreateSessionAsync</c> attempts an atomic insert
/// through <c>ISessionStore.TryCreateAsync</c>. When a concurrent second request
/// for the same id loses that attempt, it cannot know whether the winner's chat
/// history provider has produced its conversation id yet — that id is produced
/// only while the winner's FIRST turn runs. Had the loser run its own turn anyway,
/// it would have produced its OWN conversation id and the following save would have
/// silently overwritten the winner's state; that was the actual defect. So the loser
/// gets an explicit conflict error instead; a retry takes the normal (race-free)
/// path and finds the winner's record ALREADY in place.
/// </para>
/// </remarks>
public sealed class AgentPrismSessionConflictException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string SessionConflictErrorType = "session_conflict";

    /// <summary>Creates a new error.</summary>
    public AgentPrismSessionConflictException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismSessionConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismSessionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the id of the conflicting session.</summary>
    public string? SessionId { get; init; }

    /// <inheritdoc />
    public override string ErrorType => SessionConflictErrorType;
}

/// <summary>
/// Thrown when an external caller (over MCP or A2A) asked to invoke an agent from
/// the catalog and the call was refused because it crosses a boundary.
/// </summary>
/// <remarks>
/// The most common cause is an external caller trying to open an agent that carries
/// a tool requiring approval. An external caller is not an agent and cannot answer
/// an approval request — the same rule, applied a second time.
/// </remarks>
public sealed class AgentPrismExternalCallException : AgentPrismException
{
    /// <summary>
    /// The stable value written to <see cref="AgentPrismException.ErrorType"/>.
    /// </summary>
    public const string ExternalCallRejectedErrorType = "external_call_rejected";

    /// <summary>Creates a new error.</summary>
    public AgentPrismExternalCallException()
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    public AgentPrismExternalCallException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public AgentPrismExternalCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the agent the caller asked for.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the protocol the call arrived on: <c>mcp</c> or <c>a2a</c>.</summary>
    public required string Protocol { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ExternalCallRejectedErrorType;
}
