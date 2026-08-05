using System.IO.Compression;
using System.Text;

namespace AgentPrism.Api;

/// <summary>
/// <see cref="IArchiveSink"/>'in dosya sistemine yazan ornek uygulamasi (Faz 25).
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism hicbir bulut SDK'sina bagimlilik almaz (karar K-007); bu yuzden
/// varsayilan bir <see cref="IArchiveSink"/> YOKTUR. Bu sinif bir sablondur —
/// gercek bir kurulumda S3/Blob/GCS'ye yazan kendi sink'inizi buradan turetin.
/// </para>
/// <para>
/// Bicim: <c>{kok}/{hedef}/{yyyy-MM-dd}.jsonl.gz</c> — satir basina bir JSON
/// nesnesi, gzip ile sikistirilmis. Ayni gune ait birden fazla parti AYNI
/// dosyaya EKLENIR: her <see cref="WriteAsync"/> cagrisi kendi gzip "uyesini"
/// yazar; gzip biçimi ardisik uyelerin birlestirilmesine izin verir ve
/// standart okuyucular (<c>gzip -d</c>, <see cref="GZipStream"/>) dosyayi
/// tek bir akis gibi acar.
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
