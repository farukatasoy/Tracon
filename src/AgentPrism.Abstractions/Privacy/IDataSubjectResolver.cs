namespace AgentPrism;

/// <summary>
/// Maps a data subject (an end user, in the consumer's own identity system) to
/// the sessions, runs, and conversations that belong to them (phase 64).
/// </summary>
/// <remarks>
/// <para>
/// There is <strong>no default implementation</strong>. AgentPrism does not store
/// personal identity: <c>sessions.id</c> is a caller-supplied value and only the
/// consumer's own system knows which session, run, or conversation belongs to
/// which end user. Registering this interface is what turns on the export and
/// erasure endpoints; without a registration, both return <c>409</c> — never a
/// silent "nothing found" that could be read as "already erased".
/// </para>
/// <para>
/// The rejected alternative was storing the subject id ON AgentPrism's own rows.
/// That would add a NEW personal-data field whose only purpose is making erasure
/// easier — the field created to help delete data would itself become the first
/// thing that needs deleting.
/// </para>
/// </remarks>
public interface IDataSubjectResolver
{
    /// <summary>Resolves a data subject's scope.</summary>
    /// <param name="subjectId">The subject id, in the consumer's own identity system.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scope; an empty scope when the subject has no known data.</returns>
    ValueTask<DataSubjectScope> ResolveAsync(
        string subjectId,
        string tenantId,
        CancellationToken cancellationToken = default);
}
