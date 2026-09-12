using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The extension point that builds the <see cref="IAgentEvaluator"/> an eval
/// suite is graded with.
/// </summary>
/// <remarks>
/// <para>
/// Tracon grades a suite with MAF's <see cref="LocalEvaluator"/>, built
/// from the checks the suite declares. This interface is the seam around that
/// choice: a consumer registers its own factory to grade with a different
/// evaluator, or to wrap the built-in one.
/// </para>
/// <para>
/// The factory receives the suite's compiled checks rather than being a bare
/// <see cref="IAgentEvaluator"/> singleton. A suite declares its checks and
/// Tracon refuses to run a suite that declares none; an evaluator that
/// never saw them would silently grade something else. A factory that ignores
/// the argument takes over grading completely, and that is then a deliberate
/// choice rather than an accident.
/// </para>
/// <para>
/// The factory is a singleton and is called once per eval run. It must be
/// thread-safe. Registering none keeps today's behaviour exactly.
/// </para>
/// <para>
/// <strong>Tenant mode:</strong> tenant-independent. The factory sees only the
/// suite's checks, never a tenant identifier or tenant data; the run it grades
/// carries the tenant, and the eval store applies it.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. This is a
/// synchronous factory call on the eval job's own thread, not a dispatch
/// mechanism. It is called again when a job's lease expires and the job is
/// retried, so it must be cheap and free of side effects.
/// </para>
/// </remarks>
public interface IEvalEvaluatorFactory
{
    /// <summary>Creates the evaluator that grades one eval run.</summary>
    /// <param name="checks">The suite's compiled checks. Never empty.</param>
    /// <returns>The evaluator. Must not be <see langword="null"/>.</returns>
    IAgentEvaluator Create(IReadOnlyList<EvalCheck> checks);
}

/// <summary>Builds MAF's <see cref="LocalEvaluator"/> over the suite's checks.</summary>
/// <remarks>
/// The default registration. It is replaced, never wrapped, when a consumer
/// registers its own <see cref="IEvalEvaluatorFactory"/>.
/// </remarks>
internal sealed class LocalEvalEvaluatorFactory : IEvalEvaluatorFactory
{
    /// <inheritdoc />
    public IAgentEvaluator Create(IReadOnlyList<EvalCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        return new LocalEvaluator([.. checks]);
    }
}
