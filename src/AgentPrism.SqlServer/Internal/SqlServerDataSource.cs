using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace AgentPrism;

/// <summary>
/// <see cref="Microsoft.Data.SqlClient"/> icin bir <see cref="DbDataSource"/> uyarlayicisi.
/// </summary>
/// <remarks>
/// <para>
/// Npgsql <c>NpgsqlDataSource</c> tipini kendisi saglar; <c>Microsoft.Data.SqlClient</c>
/// bir <see cref="DbDataSource"/> uygulamasi <strong>sunmaz</strong>. Paylasilan depo
/// katmani veri kaynagini bu taban tip uzerinden tanidigi icin ince bir uyarlayici
/// yazilir.
/// </para>
/// <para>
/// Baglanti havuzu <c>SqlClient</c>'in kendi havuzudur; burada ek bir havuz katmani
/// yoktur. Taban sinifin <see cref="DbDataSource.CreateCommand(string)"/> uygulamasi
/// komut calistirildiginda havuzdan bir baglanti alir ve komut birakildiginda geri
/// verir — <c>NpgsqlDataSource</c> ile ayni sozlesme.
/// </para>
/// </remarks>
internal sealed class SqlServerDataSource : DbDataSource
{
    private readonly string _connectionString;

    /// <summary>Yeni bir veri kaynagi olusturur.</summary>
    /// <param name="connectionString">SQL Server baglanti dizesi.</param>
    public SqlServerDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public override string ConnectionString => _connectionString;

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection() => new SqlConnection(_connectionString);
}
