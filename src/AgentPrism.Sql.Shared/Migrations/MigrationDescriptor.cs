using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>An embedded SQL migration file.</summary>
/// <param name="Id">The sequence number. The four-digit number at the start of the file name.</param>
/// <param name="Name">The migration name. Example: <c>0001_initial</c>.</param>
/// <param name="Sql">The raw SQL text. The schema placeholder has not been substituted yet.</param>
/// <param name="Checksum">The SHA-256 digest of the raw text (hexadecimal).</param>
internal sealed record MigrationDescriptor(int Id, string Name, string Sql, string Checksum)
{
    /// <summary>The digit count of the sequence number in the file name.</summary>
    private const int IdDigits = 4;

    /// <summary>
    /// Reads all migrations in the assembly, sorted by sequence number.
    /// </summary>
    /// <param name="assembly">The assembly the migrations are embedded in.</param>
    /// <param name="resourcePrefix">
    /// The embedded resource name prefix. Example: <c>AgentPrism.PostgreSql.Migrations.</c>.
    /// Each provider has its own set; the prefix is supplied by <see cref="SqlDialect"/>.
    /// </param>
    /// <returns>The sorted migration list.</returns>
    /// <exception cref="AgentPrismException">
    /// A file name does not match the <c>NNNN_name.sql</c> format, or two files share the same sequence number.
    /// </exception>
    public static IReadOnlyList<MigrationDescriptor> Discover(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(resourcePrefix);

        var descriptors = new List<MigrationDescriptor>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[resourcePrefix.Length..];
            var name = fileName[..^".sql".Length];

            if (name.Length <= IdDigits ||
                name[IdDigits] != '_' ||
                !int.TryParse(
                    name.AsSpan(0, IdDigits),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id))
            {
                throw new AgentPrismException(
                    $"Embedded migration name '{fileName}' is not in the expected format. " +
                    "File names follow the 'NNNN_name.sql' format; example: '0001_initial.sql'.");
            }

            var sql = ReadResource(assembly, resourceName);
            descriptors.Add(new MigrationDescriptor(id, name, sql, ComputeChecksum(sql)));
        }

        descriptors.Sort(static (left, right) => left.Id.CompareTo(right.Id));

        for (var index = 1; index < descriptors.Count; index++)
        {
            if (descriptors[index].Id == descriptors[index - 1].Id)
            {
                throw new AgentPrismException(
                    $"Two migrations share the same sequence number: '{descriptors[index - 1].Name}' and " +
                    $"'{descriptors[index].Name}'. Sequence numbers must be unique.");
            }
        }

        return descriptors;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new AgentPrismException($"Could not read embedded migration resource: '{resourceName}'.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Computes the SHA-256 digest of the text.
    /// </summary>
    /// <remarks>
    /// The digest is computed BEFORE the schema placeholder is substituted.
    /// This way changing the <c>SchemaName</c> setting does not invalidate
    /// already-applied migrations.
    /// </remarks>
    private static string ComputeChecksum(string sql)
    {
        // Line-ending differences (CRLF / LF) must not change the digest;
        // depending on the repository's checkout setting, if the same file
        // produced two different digests, checksum validation would raise a
        // false alarm.
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(normalized), hash);

        // Convert.ToHexStringLower is only available in .NET 9+; the package also targets net8.0.
        return Convert.ToHexString(hash);
    }
}
