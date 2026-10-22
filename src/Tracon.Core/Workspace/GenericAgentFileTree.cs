using System.Text;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Reads, measures and deletes a directory of any <see cref="AgentFileStore"/> by walking it file by file.
/// </summary>
/// <remarks>
/// The fallback for a store that does not implement <see cref="IAgentFileTree"/>: the in-memory store, and a
/// store the host brings. It reads every file to measure it, so a store that holds many large files
/// pays for it; the SQL stores answer with one query instead. The tenant is part of every path, so the
/// walk does not need the tenant argument.
/// </remarks>
// MAAI001: AgentFileStore is marked "evaluation purposes only" - the rationale is the same as in
// AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001
internal sealed class GenericAgentFileTree(AgentFileStore store) : IAgentFileTree
{
    /// <inheritdoc />
    public async ValueTask<AgentFileTreeUsage> MeasureAsync(string tenantId, string directory, CancellationToken cancellationToken)
    {
        var files = 0;
        var bytes = 0L;

        foreach (var entry in await ReadAsync(tenantId, directory, cancellationToken).ConfigureAwait(false))
        {
            files++;
            bytes += Encoding.UTF8.GetByteCount(entry.Content);
        }

        return new AgentFileTreeUsage(files, bytes);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentFileTreeEntry>> ReadAsync(string tenantId, string directory, CancellationToken cancellationToken)
    {
        var entries = new List<AgentFileTreeEntry>();

        foreach (var relative in await ListFilesAsync(directory, string.Empty, cancellationToken).ConfigureAwait(false))
        {
            // A file can go between the listing and the read; a missing file has nothing to report.
            if (await store.ReadAsync(directory + "/" + relative, cancellationToken).ConfigureAwait(false) is { } content)
            {
                entries.Add(new AgentFileTreeEntry(relative, content));
            }
        }

        return entries;
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteAsync(string tenantId, string directory, CancellationToken cancellationToken)
    {
        var deleted = 0;

        foreach (var relative in await ListFilesAsync(directory, string.Empty, cancellationToken).ConfigureAwait(false))
        {
            if (await store.DeleteAsync(directory + "/" + relative, cancellationToken).ConfigureAwait(false))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>Lists the path of every file below a directory, relative to <paramref name="root"/>.</summary>
    private async Task<List<string>> ListFilesAsync(string root, string below, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var current = below.Length == 0 ? root : root + "/" + below;

        foreach (var entry in await store.ListChildrenAsync(current, cancellationToken).ConfigureAwait(false))
        {
            var relative = below.Length == 0 ? entry.Name : below + "/" + entry.Name;

            if (string.Equals(entry.Type, "directory", StringComparison.Ordinal))
            {
                files.AddRange(await ListFilesAsync(root, relative, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                files.Add(relative);
            }
        }

        return files;
    }
}
#pragma warning restore MAAI001
