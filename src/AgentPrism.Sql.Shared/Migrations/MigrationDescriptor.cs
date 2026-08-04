using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>Gomulu bir SQL migration dosyasi.</summary>
/// <param name="Id">Sira numarasi. Dosya adinin basindaki dort haneli sayidir.</param>
/// <param name="Name">Migration adi. Ornek: <c>0001_initial</c>.</param>
/// <param name="Sql">Ham SQL metni. Sema yer tutucusu henuz degistirilmemistir.</param>
/// <param name="Checksum">Ham metnin SHA-256 ozeti (onaltilik).</param>
internal sealed record MigrationDescriptor(int Id, string Name, string Sql, string Checksum)
{
    /// <summary>Dosya adindaki sira numarasinin hane sayisi.</summary>
    private const int IdDigits = 4;

    /// <summary>
    /// Derlemedeki tum migration'lari sira numarasina gore siralanmis olarak okur.
    /// </summary>
    /// <param name="assembly">Migration'larin gomulu oldugu derleme.</param>
    /// <param name="resourcePrefix">
    /// Gomulu kaynak ad oneki. Ornek: <c>AgentPrism.PostgreSql.Migrations.</c>.
    /// Her saglayicinin kendi seti vardir; onek <see cref="SqlDialect"/> tarafindan verilir.
    /// </param>
    /// <returns>Sirali migration listesi.</returns>
    /// <exception cref="AgentPrismException">
    /// Bir dosya adi <c>NNNN_ad.sql</c> bicimine uymuyorsa veya iki dosya ayni sira numarasini tasiyorsa.
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
                    $"Gomulu migration adi '{fileName}' beklenen bicimde degil. " +
                    "Dosya adlari 'NNNN_ad.sql' bicimindedir; ornek: '0001_initial.sql'.");
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
                    $"Iki migration ayni sira numarasini tasiyor: '{descriptors[index - 1].Name}' ve " +
                    $"'{descriptors[index].Name}'. Sira numaralari benzersiz olmalidir.");
            }
        }

        return descriptors;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new AgentPrismException($"Gomulu migration kaynagi okunamadi: '{resourceName}'.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Metnin SHA-256 ozetini hesaplar.
    /// </summary>
    /// <remarks>
    /// Ozet, sema yer tutucusu degistirilmeden ONCE hesaplanir. Boylece
    /// <c>SchemaName</c> ayarini degistirmek uygulanmis migration'lari
    /// gecersiz kilmaz.
    /// </remarks>
    private static string ComputeChecksum(string sql)
    {
        // Satir sonu farki (CRLF / LF) ozeti degistirmemelidir; depo checkout
        // ayarina gore ayni dosya iki farkli ozet uretirse checksum dogrulamasi
        // yanlis alarm verir.
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(normalized), hash);

        // Convert.ToHexStringLower yalnizca .NET 9+ icindedir; paket net8.0'i da hedefler.
        return Convert.ToHexString(hash);
    }
}
