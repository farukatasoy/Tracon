using AgentPrism.StoreContracts;

namespace AgentPrism.SqlServer.IntegrationTests.Infrastructure;

/// <summary>
/// Bir sozlesme test sinifinin paylastigi tek sema. Sema sinif basina bir kez
/// olusturulur; her test kendi verisini <see cref="SqlServerTestContext.ResetDataAsync"/>
/// ile sifirlar.
/// </summary>
/// <param name="container">Calisan SQL Server container'i.</param>
public sealed class SqlServerSchemaFixture(SqlServerFixture container) : IAsyncLifetime
{
    /// <summary>Sinifin butun testlerinin paylastigi degistirilebilir kiraci baglami.</summary>
    public MutableTenantContext Tenant { get; } = new("tenant-a");

    /// <summary>Sinifin sema baglami.</summary>
    internal SqlServerTestContext Context { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
       => Context = await SqlServerTestContext.CreateAsync(container, Tenant);

    /// <summary>Semadaki tum verileri sifirlar; sema ve migration defteri kalir.</summary>
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
