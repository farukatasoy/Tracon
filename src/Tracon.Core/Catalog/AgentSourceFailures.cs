using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Records a failure of an <see cref="IAgentSource"/> and turns it into the normalized error a caller
/// sees. One place, so the catalog and the definition lookup treat a failing source the same way.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TraconException"/> (any subtype — <see cref="TraconAgentSourceException"/>
/// itself, or a compilation error such as <c>TraconCompilationException</c> that
/// <c>AgentDefinitionCompiler.CompileCachedAsync</c> raised while the source built the
/// agent) is already a safe, normalized Tracon error: it carries no raw
/// third-party text, and a caller further up may be matching its SPECIFIC type (the
/// run endpoint's <c>400</c>/<c>502</c> split reads <see cref="TraconException.ErrorType"/>).
/// Re-wrapping it would both lose that type and misreport an ordinary
/// compilation problem as an "agent source failure" it is not. Only a genuinely raw,
/// unexpected exception — the actual case source isolation exists to contain — gets wrapped.
/// </para>
/// <para>
/// A store failure (<see cref="StoreFailure.Is"/>: a provider's <see cref="DbException"/>, a pool that ran
/// dry, a connect that timed out) is not wrapped either. It says the persistence store could not
/// answer (a tenant's provider binding or egress policy is read while the agent is compiled),
/// not that the source broke its contract; wrapped, the run endpoint reported it as <c>400 Agent
/// compilation failed</c> and the caller went to fix an agent that was fine. It is recorded like
/// any source failure and then reaches the caller as it is, where the host answers <c>503</c>.
/// The host asks the same question, so the two cannot disagree about which failures those are.
/// </para>
/// </remarks>
internal sealed class AgentSourceFailures
{
    private readonly ILogger _logger;
    private readonly TraconMetrics? _metrics;

    /// <summary>Creates the recorder.</summary>
    /// <param name="logger">The logger the failures are written to.</param>
    /// <param name="metrics">The source-failure metrics recorder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public AgentSourceFailures(ILogger logger, TraconMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>
    /// Records a source failure and re-throws it — normalized, unless it was already a
    /// normalized or otherwise-Tracon error.
    /// </summary>
    /// <param name="source">The source that failed.</param>
    /// <param name="operation">What the source was doing: <c>list</c>, <c>resolve</c> or <c>decorate</c>.</param>
    /// <param name="exception">The failure.</param>
    /// <returns>The normalized error to throw; a Tracon or store failure is rethrown here and never returned.</returns>
    public TraconAgentSourceException Handle(IAgentSource source, string operation, Exception exception)
    {
        Record(source, operation, exception, LogLevel.Error);

        if (exception is TraconException || StoreFailure.Is(exception))
        {
            // Always throws; the exception this method appears to return is never
            // actually reached in that case. Callers still write `throw
            // Handle(...)` — a return type of Exception, not void, is what
            // lets the compiler see the "always throws" invariant `agent`/`descriptor`
            // definite-assignment checks further down each caller need.
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return new TraconAgentSourceException(
            source.Name,
            TraconAgentSourceException.SourceFailedErrorType,
            $"Agent source '{source.Name}' failed during {operation} ({TraconAgentSourceException.SourceFailedErrorType}).",
            exception);
    }

    /// <summary>Records that a source broke its contract.</summary>
    /// <param name="source">The source.</param>
    /// <param name="operation">What the source was doing.</param>
    /// <param name="detail">What it did wrong.</param>
    /// <param name="level">The log level.</param>
    public void RecordContractViolation(IAgentSource source, string operation, string detail, LogLevel level = LogLevel.Error)
    {
        var exception = new TraconAgentSourceException(
            source.Name,
            TraconAgentSourceException.SourceContractErrorType,
            $"Agent source '{source.Name}' violated its contract during {operation}: {detail}");
        Record(source, operation, exception, level);
    }

    /// <summary>Records a source failure on the failure counter and in the log.</summary>
    /// <param name="source">The source that failed.</param>
    /// <param name="operation">What the source was doing.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="level">The log level.</param>
    public void Record(IAgentSource source, string operation, Exception exception, LogLevel level)
    {
        _metrics?.RecordAgentSourceFailure(source.Name, operation);

        if (_logger.IsEnabled(level))
        {
            _logger.Log(level, exception, "Agent source '{SourceName}' failed during {Operation}.", source.Name, operation);
        }
    }
}
