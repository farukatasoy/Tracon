namespace Tracon;

/// <summary>
/// Converts a raw run error into a class and a clustering fingerprint.
/// </summary>
/// <remarks>
/// <para>
/// Called only on the <strong>error</strong> path; it never triggers on a
/// successful run and must not allocate on the hot path.
/// </para>
/// <para>
/// Tracon's taxonomy is its own opinion; a consumer may want their own
/// class or clustering rule. Registered with <c>TryAddSingleton</c>, so the
/// consumer's registration wins.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> <see cref="Classify"/>
/// is a pure function of <see cref="RunError"/>, which carries no tenant field;
/// the same taxonomy applies identically to every tenant's errors.
/// </para>
/// </remarks>
public interface IRunErrorClassifier
{
    /// <summary>
    /// Classifies the error. Returns <see cref="RunErrorClass.Unknown"/> if no
    /// rule matches — it <strong>does not guess</strong>.
    /// </summary>
    /// <param name="runError">The raw error to classify.</param>
    /// <returns>The class and clustering fingerprint.</returns>
    RunErrorClassification Classify(RunError runError);
}
