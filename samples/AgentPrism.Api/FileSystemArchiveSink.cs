using System.IO.Compression;
using System.Text;

namespace AgentPrism.Api;

/// <summary>
/// Sample implementation of <see cref="IArchiveSink"/> that writes to the file system.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism takes no dependency on any cloud SDK (decision K-007), so there is
/// NO default <see cref="IArchiveSink"/>. This class is a template — derive your
/// own sink that writes to S3/Blob/GCS from it in a real deployment.
/// </para>
/// <para>
/// Format: <c>{root}/{target}/{yyyy-MM-dd}.jsonl.gz</c> — one JSON object per
/// line, gzip-compressed. Multiple batches for the same day are APPENDED to the
/// SAME file: each <see cref="WriteAsync"/> call writes its own gzip "member";
/// the gzip format allows consecutive members to be concatenated, and standard
/// readers (<c>gzip -d</c>, <see cref="GZipStream"/>) open the file as a single
/// stream.
/// </para>
/// </remarks>
public sealed class FileSystemArchiveSink(string rootPath) : IArchiveSink
{
    /// <inheritdoc />
    public async ValueTask WriteAsync(
        string target,
        DateTimeOffset partitionDate,
        IReadOnlyList<ArchiveRow> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var directory = Path.Combine(rootPath, target);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{partitionDate:yyyy-MM-dd}.jsonl.gz");

        var fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

        await using (fileStream.ConfigureAwait(false))
        {
            var gzip = new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: true);

            await using (gzip.ConfigureAwait(false))
            {
                foreach (var row in rows)
                {
                    var line = Encoding.UTF8.GetBytes(row.Json + "\n");

                    await gzip.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
