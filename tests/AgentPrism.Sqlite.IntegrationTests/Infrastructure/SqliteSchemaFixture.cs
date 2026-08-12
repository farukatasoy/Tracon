using AgentPrism.StoreContracts;

namespace AgentPrism.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// Bir sozlesme test sinifinin paylastigi tek tablo oneki. Onek sinif basina
/// bir kez olusturulur; her test kendi verisini
/// <see cref="SqliteTestContext.ResetDataAsync"/> ile sifirlar.
/// </summary>
/// <param name="database">Calisan SQLite veritabani dosyasi.</param>
/// <remarks>
/// 🚨 Onek sinif basina tek oldugu icin migration'lar SQLite'in TEK dosyasina
/// paralel yazar; <see cref="SqliteDialect"/>'in oneke kapsanan kilit dosyasi
/// (K-389) bu es zamanli migration'lari sirayla gecirir — ayri bir kilitleme
/// mekanizmasi burada gerekmez.
/// </remarks>
public sealed class SqliteSchemaFixture(SqliteFixture database) : IAsyncLifetime
{
    /// <summary>Sinifin butun testlerinin paylastigi degistirilebilir kiraci baglami.</summary>
    public MutableTenantContext Tenant { get; } = new("tenant-a");

    /// <summary>Sinifin tablo oneki baglami.</summary>
    internal SqliteTestContext Context { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
        => Context = await SqliteTestContext.CreateAsync(database, Tenant);

    /// <summary>Onekteki tum verileri sifirlar; tablolar ve migration defteri kalir.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    public ValueTask ResetAsync() => Context.ResetDataAsync();

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="InitializeAsync"/> basarisiz olursa <see cref="Context"/> hic
    /// atanmaz; bu durumda gercek hatayi bir <see cref="NullReferenceException"/>
    /// ile gizlememek icin dispose sessizce atlanir.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Context is not null)
        {
            await Context.DisposeAsync();
        }
    }
}
