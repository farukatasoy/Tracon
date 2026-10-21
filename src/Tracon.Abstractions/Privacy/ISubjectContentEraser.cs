using System.Text.Json;

namespace Tracon;

/// <summary>
/// Content a data subject owns that no SQL table holds: a copy kept in a store of the host's own,
/// keyed by the session it came from.
/// </summary>
/// <remarks>
/// Internal: the SQL stores of the three providers and the session manager reach it through
/// <c>InternalsVisibleTo</c>, and <c>Tracon.Core</c> implements it. A copy of a session's content
/// is erased with the session, whichever way the session goes: an erasure of its data subject,
/// <c>DELETE /api/sessions/{id}</c>, or its retention period.
/// </remarks>
internal interface ISubjectContentEraser
{
    /// <summary>Gets the name the erasure reports its row count under.</summary>
    string Target { get; }

    /// <summary>Counts the records the sessions own in <paramref name="tenantId"/>; nothing is deleted.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="sessionIds">The sessions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records.</returns>
    ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken);

    /// <summary>Deletes the records the sessions own in <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="sessionIds">The sessions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records deleted. A failure propagates; a repeat call finishes the erasure.</returns>
    ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken);

    /// <summary>Writes the records the sessions own as one JSON array named <see cref="Target"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="sessionIds">The sessions.</param>
    /// <param name="writer">The export document; the array is a property of its open object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask WriteExportAsync(string tenantId, IReadOnlyList<string> sessionIds, Utf8JsonWriter writer, CancellationToken cancellationToken);
}
