using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace AgentPrism;

/// <summary>
/// <see cref="Microsoft.Data.Sqlite"/> icin bir <see cref="DbDataSource"/> uyarlayicisi.
/// </summary>
/// <remarks>
/// <para>
/// <c>Npgsql</c> <c>NpgsqlDataSource</c> tipini kendisi saglar; <c>Microsoft.Data.Sqlite</c>
/// bir <see cref="DbDataSource"/> uygulamasi <strong>sunmaz</strong>. Paylasilan
/// depo katmani veri kaynagini bu taban tip uzerinden tanidigi icin ince bir
/// uyarlayici yazilir (SQL Server ile ayni desen).
/// </para>
/// <para>
/// 🚨 <strong>WAL, <c>busy_timeout</c> ve yabanci anahtar zorlamasi HER YENI
/// baglantida</strong> baglantinin durum degisikligi olayi ile ayarlanir.
/// Bu ayarlar baglanti dizesi anahtar kelimeleriyle degil (WAL ve
/// <c>busy_timeout</c> icin boyle bir anahtar kelime yoktur), acik <c>PRAGMA</c>
/// komutlariyla yapilir; tuketicinin baglanti dizesine bagli degildir.
/// </para>
/// </remarks>
internal sealed class SqliteDataSource : DbDataSource
{
    private readonly string _connectionString;

    /// <summary>Yeni bir veri kaynagi olusturur.</summary>
    /// <param name="connectionString">SQLite baglanti dizesi.</param>
    public SqliteDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public override string ConnectionString => _connectionString;

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.StateChange += OnStateChange;

        return connection;
    }

    /// <summary>
    /// Baglanti acildiginda WAL, <c>busy_timeout</c> ve yabanci anahtar
    /// zorlamasini ayarlar.
    /// </summary>
    /// <remarks>
    /// <c>:memory:</c> veritabanlarinda <c>journal_mode=WAL</c> istegi sessizce
    /// <c>memory</c> moduna duser (hata vermez); pragma yine de kosulsuz calistirilir.
    /// </remarks>
    private static void OnStateChange(object? sender, StateChangeEventArgs eventArgs)
    {
        if (eventArgs.CurrentState != ConnectionState.Open || sender is not SqliteConnection connection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
    }
}
