using Npgsql;

namespace AgentPrism;

/// <summary>
/// Komut calistirma kaliplarini tek yerde toplayan yardimcilar.
/// </summary>
/// <remarks>
/// <para>
/// Kutuphane kodunda her <c>await</c> ve her <c>await using</c> icin
/// <c>ConfigureAwait(false)</c> gerekir (kural MA0004). Bunu her cagri yerinde
/// tekrarlamak depolari okunmaz hale getirirdi; kalip burada bir kez yazilir.
/// </para>
/// <para>
/// Her yardimci verilen komutun sahipligini alir ve isi bitince onu birakir.
/// </para>
/// </remarks>
internal static class NpgsqlHelpers
{
    /// <summary>Komutu calistirir ve etkilenen satir sayisini dondurur.</summary>
    /// <param name="command">Calistirilacak komut. Cagri sonunda birakilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Etkilenen satir sayisi.</returns>
    public static async ValueTask<int> ExecuteAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Komutu calistirir ve ilk satirin ilk sutununu dondurur.</summary>
    /// <param name="command">Calistirilacak komut. Cagri sonunda birakilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tek deger; sonuc yoksa <see langword="null"/>.</returns>
    public static async ValueTask<object?> ExecuteScalarAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Komutu calistirir ve ilk satiri esler.</summary>
    /// <typeparam name="T">Eslenen tip.</typeparam>
    /// <param name="command">Calistirilacak komut. Cagri sonunda birakilir.</param>
    /// <param name="map">Satiri nesneye ceviren esleyici.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ilk satir; sonuc bos ise <see langword="null"/>.</returns>
    public static async ValueTask<T?> ReadSingleAsync<T>(
        NpgsqlCommand command,
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken)
        where T : class
    {
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? map(reader)
                    : null;
            }
        }
    }

    /// <summary>Komutu calistirir ve tum satirlari esler.</summary>
    /// <typeparam name="T">Eslenen tip.</typeparam>
    /// <param name="command">Calistirilacak komut. Cagri sonunda birakilir.</param>
    /// <param name="map">Satiri nesneye ceviren esleyici.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sirali sonuc listesi.</returns>
    public static async ValueTask<List<T>> ReadListAsync<T>(
        NpgsqlCommand command,
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(map(reader));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// timestamptz sutununu UTC damgali olarak okur.
    /// </summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Zaman damgasi.</returns>
    public static DateTimeOffset GetTimestamp(NpgsqlDataReader reader, int ordinal)
        => reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Bos olabilen bir metin sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Bos olabilen bir timestamptz sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Zaman damgasi; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static DateTimeOffset? GetNullableTimestamp(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Bos olabilen bir numeric sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static decimal? GetNullableDecimal(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}
