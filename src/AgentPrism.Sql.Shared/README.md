# AgentPrism.Sql.Shared — paylasilan kaynak

> Bu bir NuGet paketi **degildir** ve kendi `.csproj` dosyasi **yoktur**.
> Buradaki `.cs` dosyalari her SQL kalicilik paketine `<Compile Include="..." />`
> ile dogrudan derlenir.

## Neden paket degil

Ucuncu bir paket yayinlamak, tuketicinin asla dogrudan kullanmayacagi bir
bagimlilik uretirdi ve her surumde ayrica yayin yuku getirirdi. Kaynak
paylasimi paket sayisini artirmadan kod tekrarini onler.

Gerekce: `docs/KARARLAR.md`, karar K-176.

## Kimler derler

| Paket | Nasil |
|-------|-------|
| `AgentPrism.PostgreSql` | `<Compile Include="../AgentPrism.Sql.Shared/**/*.cs" />` |
| `AgentPrism.SqlServer` | ayni |

Her iki derlemede de tipler `AgentPrism` ad alanindadir ve `internal`'dir;
ayni ada sahip iki tip iki **ayri** derlemede yasadigi icin catisma olmaz.

## Ne buraya girer, ne girmez

| Girer | Girmez |
|-------|--------|
| Saglayicidan bagimsiz depo uygulamalari (`Stores/Sql*Store.cs`) | SQL metinleri (`SqlQueriesBase` alt siniflari) |
| `DbCommand` / `DbDataReader` uzerine yardimcilar | Gomulu `.sql` migration dosyalari |
| Migration calistirici iskeleti ve checksum hesabi | Baglanti dizesi / veri kaynagi kurulumu |
| Tanim yuku DTO'lari, JSON kaynak ureteci baglami | `Options` siniflari ve `Use*` uzantilari |

**Kural:** buradaki hicbir dosya `Npgsql` veya `Microsoft.Data.SqlClient`
ad alanina referans veremez. Saglayiciya ozgu her sey `SqlDialect` uzerinden
gecer.
