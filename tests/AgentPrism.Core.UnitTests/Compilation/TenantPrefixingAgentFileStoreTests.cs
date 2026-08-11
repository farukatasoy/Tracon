namespace AgentPrism.Core.UnitTests.Compilation;

#pragma warning disable MAAI001 // InMemoryAgentFileStore "evaluation purposes only" — yalniz test kurulumu icin.

/// <summary>
/// <see cref="TenantPrefixingAgentFileStore"/>'un tek bir paylasilan
/// <see cref="Microsoft.Agents.AI.AgentFileStore"/> uzerinde kiracilari
/// gercekten yalitip yalitmadigini dogrular.
/// </summary>
/// <remarks>
/// Bu testlerin korudugu kural: bir kiracinin <c>EnableFileMemory</c> ile
/// yazdigi dosya, BASKA bir kiracinin <c>EnableTextSearch</c> aramasinda asla
/// GORUNMEMELIDIR. Bkz. 00-INDEKS.md'nin "KRITIK SUPHE" notu (2026-08-10).
/// </remarks>
public sealed class TenantPrefixingAgentFileStoreTests
{
    [Fact]
    public async Task Bir_kiracinin_yazdigi_dosya_baska_kiraciya_gorunmez()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenantA = new TenantPrefixingAgentFileStore(shared, "kiraci-a");
        var tenantB = new TenantPrefixingAgentFileStore(shared, "kiraci-b");

        await tenantA.WriteAsync("/gizli.txt", "kiraci-a'nin sirri", CancellationToken.None);

        (await tenantA.ReadAsync("/gizli.txt", CancellationToken.None)).ShouldBe("kiraci-a'nin sirri");
        (await tenantA.FileExistsAsync("/gizli.txt", CancellationToken.None)).ShouldBeTrue();

        (await tenantB.FileExistsAsync("/gizli.txt", CancellationToken.None)).ShouldBeFalse();
        (await tenantB.ListChildrenAsync("/", CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ayni_kiraci_kok_dizinden_kendi_dosyasini_gorur()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenant = new TenantPrefixingAgentFileStore(shared, "kiraci-a");

        await tenant.WriteAsync("/notlar/gunluk.txt", "merhaba", CancellationToken.None);

        var children = await tenant.ListChildrenAsync("/notlar", CancellationToken.None);

        children.ShouldHaveSingleItem().Name.ShouldBe("gunluk.txt");
    }

    [Fact]
    public async Task Ic_depodaki_gercek_onek_disariya_sizmaz()
    {
        var shared = new Microsoft.Agents.AI.InMemoryAgentFileStore();
        var tenant = new TenantPrefixingAgentFileStore(shared, "kiraci-a");

        await tenant.WriteAsync("/notlar.txt", "icerik", CancellationToken.None);

        var children = await tenant.ListChildrenAsync("/", CancellationToken.None);

        // Donen ad "kiraci-a/notlar.txt" DEGIL, sanki tenant kendi ozel kok
        // dizininde calisiyormus gibi duz "notlar.txt" olmalidir.
        children.ShouldHaveSingleItem().Name.ShouldBe("notlar.txt");

        // Ic depoda gercek yol kiraci onekini TASIR.
        (await shared.FileExistsAsync("kiraci-a/notlar.txt", CancellationToken.None)).ShouldBeTrue();
    }
}
#pragma warning restore MAAI001
