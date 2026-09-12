# EF Core Uyum Ölçümü — 2026-08-26

> **Tur tipi:** kullanıcı sorusu → ölçüm. Soru: *"EF Core entegrasyonu paketi
> tüketici tarafında daha cazip hale getirir mi?"*
> **Sonuç:** iki faz — [Faz 110](../arsiv/fazlar/110-TUKETICI-BAGLANTI-DUZLEMI.md) ·
> [Faz 111](../arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md).

---

## Soru ikiye ayrıldı

| Soru | Cevap |
|---|---|
| Tracon içeride EF Core kullansın mı? | **Hayır.** L16 · L29 geçerli, üstüne iki yeni kanıt eklendi |
| EF Core kullanan tüketiciye temas yüzeyi yeterli mi? | **Hayır.** Beş delik ölçüldü |

## İçeride EF Core — reddi güçlendiren yeni kanıt

`docs/KARARLAR.md:16` (L16, kullanıcı kararı) ve `:29` bağımlılık sayısıyla
gerekçelendirilmişti. 2026-08-26 ölçümü iki kanıt daha ekledi:

| Kanıt | Sonuç |
|---|---|
| `grep -l "AotCompatible>false" src/*/*.csproj` çıktısında `Tracon.PostgreSql` **yok** | Paket AOT uyumlu. EF Core AOT uyumlu değildir; bir EF store paketi bayrak sağlayıcının duruşunu bozar |
| [`SqlStoreContext.cs:24`](../../src/Tracon.Sql.Shared/Internal/SqlStoreContext.cs) | Store'lar zaten yalnız `DbDataSource` · `DbConnection` · `DbTransaction` görüyor. EF'in getireceği soyutlama ADO.NET seviyesinde **mevcut** |

`Tracon.EntityFrameworkCore` paketi **önerilmedi**. 33 store arayüzü ve
[`Tracon.Testing.Contracts.Xunit`](../../src/Tracon.Testing.Contracts.Xunit/README.md)
sayesinde tüketici kendi `DbContext` store'unu bugün yazabilir. S3/Azure Blob
emsaliyle aynı çizgi: genişleme noktası bizde, somut uygulama tüketicide.

## Ölçülen beş delik

| # | Delik | Kanıt | Faz |
|---|---|---|---|
| P1 | Havuz paylaşımı iddiası çelişkili | [`embedding.md:165`](../../docs-site/src/content/docs/guides/embedding.md) "share one Npgsql connection pool" ↔ [uygulanabilirlik raporu §7.1](../arsiv/kesif/2026-08-21-uygulanabilirlik-raporu.md) "havuz iki katına çıkar" | 110 |
| P2 | Dış `DbDataSource` yüzeyi yok; PostgreSQL'de kazara var, sıraya bağlı | `TraconPostgreSqlBuilderExtensions.cs:94` · `TraconSqlServerBuilderExtensions.cs:91` · `TraconSqliteBuilderExtensions.cs:80` | 110 |
| P3 | "Ortak transaction yok" cevabı yalnız Türkçe keşif notunda | `docs-site/` içinde karşılığı yok | 110 |
| P4 | Okuma tarafı sözleşme değil | `grep -rli "CREATE VIEW" src/*/Migrations/*.sql` **boş** | 111 |
| P5 | `tracon migrate` ile `dotnet ef database update` sıralaması belgesiz | [CLI README](../../src/Tracon.Cli/README.md) tek başına duruyor | 110 |
| P6 | Gömme örneğinde veritabanı ve EF yok | `ls samples/Tracon.Embedded/` | 110 |

## P2'nin neden yetenek kalemi olduğu

`TraconPostgreSqlOptions` beş alan taşır: `ConnectionString`, `SchemaName`,
`AutoApplyMigrations`, `CommandTimeoutSeconds`, `EnableKnowledge`. Connection
string ile ifade **edilemeyen** her şey bugün erişilemez:

- Süresi dolan token'ı yenileyen parola sağlayıcı geri çağrımı — Entra ID / IAM
  kimliğine geçmiş bir kurum `Tracon.PostgreSql`'i kural dışına çıkmadan
  kullanamaz
- İstemci sertifikası ve özel TLS doğrulama geri çağrımı
- Özel tip eşlemeleri

Bunlar ergonomi değil, kapıdır.

## P4'ün neden ayrı faz olduğu

[`0034_run_attribution.sql:36-40`](../../src/Tracon.PostgreSql/Migrations/0034_run_attribution.sql):
`cached_input_cost`, bir koşu toplamının **üçüncü terimidir**. `runs` tablosuna
doğrudan bağlanan bir tüketici `input_cost + output_cost` yazar ve **eksik
hesaplar**. Aynı kusur K-483'te bizim içimizde doğdu; 4241 test yakalamadı.
Görünüm, bu kusur sınıfını tüketici için imkânsız kılar — ama kalıcı bir
uyumluluk taahhüdüdür ve kendi DoD'sini hak eder.

## Ölçüm dışı bırakılanlar

| İstek | Neden çıktı |
|---|---|
| Ortak transaction / ambient enrollment | Store'lar singleton; "gözlemlenebilirlik işlevselliği bozmaz" kuralını kırar. Doğru cevap seam değil, `RunId` referans desenidir |
| EF global query filter'ın Tracon satırlarına işlemesi | İki veri düzlemi; kiracı izolasyonu Tracon'in kendi `tenant_id` sütunuyla |
| `dotnet ef migrations` ile Tracon şeması | L29. İki ayrı adım kalır, yalnız sırası belgelenir |
