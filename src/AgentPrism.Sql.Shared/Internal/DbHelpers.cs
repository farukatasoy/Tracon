using System.Data.Common;

namespace AgentPrism;

/// <summary>
/// Komut calistirma kaliplarini tek yerde toplayan, saglayicidan bagimsiz yardimcilar.
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
/// <para>
/// Tipler ADO.NET tabanidir (<see cref="DbCommand"/>, <see cref="DbDataReader"/>);
/// boylece ayni depo kodu PostgreSQL ve SQL Server uzerinde calisir. Saglayiciya
/// ozgu her sey <see cref="SqlDialect"/> uzerinden gecer.
/// </para>
/// </remarks>
internal static class DbHelpers
{
    /// <summary>Komutu calistirir ve etkilenen satir sayisini dondurur.</summary>
    /// <param name="command">Calistirilacak komut. Cagri sonunda birakilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Etkilenen satir sayisi.</returns>
    public static async ValueTask<int> ExecuteAsync(DbCommand command, CancellationToken cancellationToken)
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
        DbCommand command,
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
        DbCommand command,
        Func<DbDataReader, T> map,
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
        DbCommand command,
        Func<DbDataReader, T> map,
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
    /// Tipi degerden cikarilan bir parametre ekler.
    /// </summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger. <see langword="null"/> <strong>olamaz</strong>.</param>
    /// <remarks>
    /// <para>
    /// Yalnizca tipi degerden guvenle cikarilabilen, <em>bos olmayan</em> skaler
    /// degerler icindir: <see cref="Guid"/>, <see cref="string"/>,
    /// <see cref="short"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="bool"/>.
    /// </para>
    /// <para>
    /// 🚨 Zaman damgasi, ondalik ve <c>NULL</c> olabilen degerler icin
    /// <strong>kullanilmaz</strong>; onlar <see cref="SqlDialect"/> uzerinden
    /// acikca tiplenir. Sebep iki tuzaktir: SQL Server bir <see cref="decimal"/>
    /// parametresini tip verilmediginde <c>decimal(18,0)</c> sayar ve ondalik
    /// kismi <em>sessizce keser</em>; tipsiz bir <c>NULL</c> ise PostgreSQL'de
    /// <c>42P08</c> verir. Ikisi de yalnizca calisma aninda gorunur.
    /// </para>
    /// </remarks>
    public static void Add(DbCommand command, string name, object value)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(value);

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Zaman damgasi sutununu UTC damgali olarak okur.
    /// </summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Zaman damgasi.</returns>
    /// <remarks>
    /// PostgreSQL <c>timestamptz</c> ve SQL Server <c>datetimeoffset</c> sutunlarinin
    /// ikisi de <see cref="DateTimeOffset"/> olarak okunur. Yazma her zaman UTC
    /// yaptigi icin okunan degerin ofseti sifirdir.
    /// </remarks>
    public static DateTimeOffset GetTimestamp(DbDataReader reader, int ordinal)
        => reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Bos olabilen bir metin sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static string? GetNullableString(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Bos olabilen bir zaman damgasi sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Zaman damgasi; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static DateTimeOffset? GetNullableTimestamp(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Bos olabilen bir ondalik sutunu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static decimal? GetNullableDecimal(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    /// <summary>Bos olabilen bir kimlik sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static Guid? GetNullableGuid(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    /// <summary>Bos olabilen bir <c>integer</c> sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static int? GetNullableInt32(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    /// <summary>Bos olabilen bir <c>bigint</c> sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Deger; <c>NULL</c> ise <see langword="null"/>.</returns>
    public static long? GetNullableInt64(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
