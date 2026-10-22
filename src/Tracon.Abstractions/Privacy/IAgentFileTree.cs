using System.Runtime.InteropServices;

namespace Tracon;

/// <summary>
/// Reads, measures and deletes a whole directory of an agent file store in one step, with no run
/// in scope.
/// </summary>
/// <remarks>
/// <para>
/// Internal: the SQL file store of the three providers implements it through
/// <c>InternalsVisibleTo</c>, and <c>Tracon.Core</c> uses it for the session workspace. The
/// Microsoft Agent Framework file store has no such operation, and the SQL one needs the agent
/// of the running run for every call, so an erasure of a session (which runs outside any run) could
/// not reach the files through it. A store that does not implement this interface is walked
/// file by file through its own methods.
/// </para>
/// <para>
/// A directory is written the way the store is given paths: <c>tenant/~workspace/key</c>, with no
/// leading <c>/</c>. Every method names the tenant as well as the directory; the SQL store
/// filters by both.
/// </para>
/// </remarks>
internal interface IAgentFileTree
{
    /// <summary>Counts the files under a directory and adds up their size.</summary>
    /// <param name="tenantId">The tenant that owns the files.</param>
    /// <param name="directory">The directory, with every directory below it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of files and their size in bytes.</returns>
    ValueTask<AgentFileTreeUsage> MeasureAsync(string tenantId, string directory, CancellationToken cancellationToken);

    /// <summary>Reads every file under a directory.</summary>
    /// <param name="tenantId">The tenant that owns the files.</param>
    /// <param name="directory">The directory, with every directory below it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The files, each with its path relative to <paramref name="directory"/>.</returns>
    ValueTask<IReadOnlyList<AgentFileTreeEntry>> ReadAsync(string tenantId, string directory, CancellationToken cancellationToken);

    /// <summary>Deletes every file under a directory.</summary>
    /// <param name="tenantId">The tenant that owns the files.</param>
    /// <param name="directory">The directory, with every directory below it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of files deleted. A failure propagates; a repeat call finishes the deletion.</returns>
    ValueTask<int> DeleteAsync(string tenantId, string directory, CancellationToken cancellationToken);
}

/// <summary>The files under a directory and their size.</summary>
/// <param name="Files">The number of files.</param>
/// <param name="Bytes">The size of their content in bytes.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AgentFileTreeUsage(int Files, long Bytes);

/// <summary>A file under a directory.</summary>
/// <param name="Path">The path relative to the directory that was read, with no leading <c>/</c>.</param>
/// <param name="Content">The text of the file.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AgentFileTreeEntry(string Path, string Content);
