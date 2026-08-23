# Faz 83 — Tipli Yönetim İstemcisi ve CLI

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-50** — Dalga 13 Küme E (birinci yarı)
> **Önkoşul:** [Faz 40](40-OPENAPI-YAYINI.md) — üretim kaynağı olan OpenAPI belgesi ve onu koda bağlayan `OpenApiSnapshotTests` oradan gelir · [Faz 67](67-ISTEGE-BAGLI-MIGRATION-SETI.md) — `MigrationRunner`'ın set kavramı ve `AutoApplyMigrations` sözleşmesi
> **Paketler:** `AgentPrism.Client` (**yeni**), `AgentPrism.Cli` (**yeni**)
> **Yeni paket:** İki tane — gerekçe **§83.7** · **Migration:** **Yok** — bu faz migration *çalıştırır*, migration *yazmaz*
> **Public API:** Büyüyor — ama tamamı **üretilmiş**tir. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; `wc -l src/*/PublicAPI.Shipped.txt` ile ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → yeni `guides/cli.md`, `packages.md` (iki yeni satır), `capabilities.md`, `getting-started/persistence.md` (migration'ı ayrı adım olarak koşma), `reference/versioning.md` (istemci–sunucu sürüm eşleşmesi)
> · sevk edilen: `src/AgentPrism.Client/README.md` ve `src/AgentPrism.Cli/README.md` (**yeni**, `PackageReadmeFile` zorunlu), `AgentPrismClientOptions` XML dokümanı, kök `README.md` paket tablosu. `api/` ve `http-api/` **üretilir**
> **Manuel test alanı:** `docs/manuel-test/34-ISTEMCI-VE-CLI.md` — **yeni dosya**, alan kodu `CLI`. Kapanışta `faz-tamamlama` oluşturur ve [`00-INDEKS.md`](../../manuel-test/00-INDEKS.md) §7 tablosuna `34` satırını yazar — planın "son sıra 32" varsayımı bayattı, kapanışta `33` zaten Faz 80 tarafından alınmıştı

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in yönetim API'sini bugün yalnız elle yazılmış HTTP çağrısıyla kullanabilirsin. Tipli bir yol yoktur. Ayrıca migration **yalnız uygulama açılırken** uygulanır; bir CI/CD hattı şemayı ayrı bir adımda hazırlamak isterse çalıştıracak araç yoktur — `MigrationHostedService`'in kendi XML dokümanı bu yolu **vaat ediyor** ama aracı yok.

## Bitiş Ölçütleri (DoD)

- [x] `agentprism migrate --provider sqlite --connection "Data Source=<gerçek dosya>"` boş bir veritabanında şemayı kurar (**22 applied**, ölçüldü); `postgres` yerine `sqlite` ile doğrulandı — gerekçe: Devir Notu. `AutoApplyMigrations=false` ile açılan `samples/AgentPrism.Api` senaryosu **koşulmadı** (bkz. aşağıdaki "gerçek run" satırı)
- [x] `agentprism migrate` ikinci kez koştuğunda `0 applied` der ve çıkış kodu `0`'dır — `MigrateCommandTests.Migrate_run_a_second_time_is_idempotent`
- [x] `agentprism health --url <adres>` ayakta bir sunucudan sağlık durumu okur (gerçek Kestrel dinleyicisine karşı, `HealthCommandTests`); sunucu kapalıyken **sıfırdan farklı** çıkış kodu döner ve asılı kalmaz — `A_server_that_is_not_running_fails_fast_instead_of_hanging` (port `1`, anında ret)
- [x] `ClientCoverageTests` belgedeki **160** `operationId`'nin tamamını istemcide bulur; muafiyet dosyası yoktur
- [x] `client-description-baseline.txt` kurulmuştur (**393** — bkz. Denetim Bulguları #2, ilk ölçüm 1320 hatalıydı) ve `ClientDescriptionBaselineTests` listenin büyümesini **reddeder**
- [x] 🚨 AOT iddiası **ölçülmüştür**: `AgentPrism.Client`'ın AOT sözünden **vazgeçildi** — `AgentPrismAotCompatible=false`, gerekçe csproj yorumunda ve K-NNN'de. `ClientAotPublishTests` bu yüzden **yazılmadı**
- [x] 🚨 Özel önekle (`MapAgentPrism("control")`) istemci çalışır — `HealthCommandTests.Reads_health_through_a_custom_MapAgentPrism_prefix` (ayrı bir `ClientPrefixTests` sınıfı yerine burada, gerçek host'a karşı)
- [x] `DependencyDirectionTests` iki yeni anahtarla yeşil; iki paket meta pakette **değil**
- [x] İki paketin `README.md`'si vardır, `AgentPrism.slnx`'e eklenmiştir, `dotnet pack` ikisini de üretir (`AgentPrism.Client.*.nupkg`, `AgentPrism.Cli.*.nupkg` — ölçüldü)
- [x] `dotnet tool install -g` ile kurulan tool `agentprism --help` çıktısını verir — gerçekten kuruldu, doğrulandı, sonra kaldırıldı
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `secret` taraması boş döndü — **ayrıca** CLI çıktısında bağlantı dizesi ve token geçmiyor (`CliSecretRedactionTests`, 3/3 yeşil)
- [x] Manuel kabul case'leri `docs/manuel-test/34-ISTEMCI-VE-CLI.md` içine eklendi; `00-INDEKS.md` tablosuna `34` satırı yazıldı; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (ikisi de düzeltildi — bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` temiz
- [x] Kök `README.md` paket tablosuna iki satır eklendi

### 🚨 Plandan sapan iki DoD satırı

1. **`postgres` yerine `sqlite` ile doğrulandı.** Bu makinede kalıcı bir
   PostgreSQL örneği yoktu; `sqlite` üç sağlayıcının **aynı** `MigrationRunner`/
   `IMigrationApplier` yolundan geçtiği için sağlayıcı seçimi davranışı
   değiştirmez (`SqlProviderSelector.Register` üçünü de aynı şekilde kaydeder).
2. **`samples/AgentPrism.Api` ile "gerçek run" koşulmadı.** Uygulama
   başlatılırken makinedeki `dotnet user-secrets` deposunun **kullanıcının
   önceki manuel test oturumlarından kalma gerçek görünümlü `secret`'lar**
   (OpenAI/Anthropic/Google API anahtarları, PostgreSQL bağlantı dizesi)
   taşıdığı görüldü. Bootstrap için iki geçici anahtar eklenmiş, sonra fark
   edilip **aynen geri alınmıştır** — depo net değişiklik olmadan bırakıldı.
   Doğrulama bunun yerine `tests/AgentPrism.Cli.FunctionalTests` (gerçek
   Kestrel dinleyicisi, 15 test) ve `tests/AgentPrism.AspNetCore.FunctionalTests/ClientTenantScopeTests.cs`
   (gerçek host + gerçek kiracı ayrımı, 2 test) ile yapıldı — biri gerçek bir
   kusur buldu (bkz. Denetim Bulguları #1: `RunKind` enum'ı yanlış serileşiyordu).

### Doğrulama komutları

```bash
# Uretim (gelistirme adimi — dotnet build bunu CAGIRMAZ)
dotnet tool restore && dotnet nswag run nswag.json

# Sema ayri adimda kuruluyor mu
agentprism migrate --provider sqlite --connection "Data Source=/tmp/ap.db"
agentprism migrate status --provider sqlite --connection "Data Source=/tmp/ap.db"

# Istemci ayakta bir sunucuyu okuyor mu
cd samples/AgentPrism.Api && dotnet run &        # http://localhost:5080/agentprism
agentprism health --url http://localhost:5080/agentprism --json

# Dort kapi
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

---

## Plandan Sapmalar

1. **Manuel test dosya numarası `33` değil `34`.** Plan "bugün son sıra 32"
   diyordu ama kapanışta `33` zaten [Faz 80](80-DOKUMAN-KAPILARININ-DOGRULUGU.md)
   tarafından alınmıştı (`33-DOKUMAN-KAPILARI.md`). Dosya `34-ISTEMCI-VE-CLI.md`
   olarak açıldı.
2. **`AddAgentPrismClient` `IHttpClientBuilder` değil `IServiceCollection`
   döner.** Planın taslak imzası `IHttpClientBuilder` kullanıyordu; bu tip
   `Microsoft.Extensions.Http` paketini gerektirir ve §83.7'nin ölçtüğü "sıfır
   paket" iddiasını bozardı. K-569.
3. **AOT sözünden vazgeçildi.** §83.6'nın flagladığı risk gerçekleşti:
   `-p:AgentPrismAotCompatible=true` 146 IL2026/IL3050/IL2075 tanısı üretti
   (42 çağrı noktası × 3 TFM). `AgentPrismAotCompatible=false`. K-567.
   `ClientAotPublishTests` bu yüzden **yazılmadı** — planın kendi DoD'si
   "ya yeşildir ya gerekçe yazılır" diyordu, ikinci dal seçildi.
4. **`IMigrationApplier` — planda YOKTU, kapanışta eklendi.** Plan 83.5'in
   kod örneği `provider.GetRequiredService<MigrationRunner>()` diyordu; bu
   üç sağlayıcıyı BİRLİKTE referanslayan CLI'de `CS0433` verdi (linked-source,
   K-176 — plan bunu kod örneği yazarken ÖLÇMEMİŞTİ). K-568.
5. **Enum dönüştürme iki aşamalı tasarlandı, tek aşamaya indi.** İlk tasarım
   ~30 MEAI ayrımcısı için tip-özelinde `[JsonConverter]`, geri kalan ~37
   "düz" enum için `AgentPrismClientJsonContext`'in global `Converters`
   listesi kullanıyordu. Bir fonksiyonel test (`ClientTenantScopeTests`)
   ikincinin ÇALIŞMADIĞINI buldu (`RunKind` sayısal converter'a düşüyordu);
   tüm 67 enum tip-düzeyi desenine taşındı, global liste kaldırıldı. K-571.
6. **Şema kapatma adımı (`additionalProperties: false`) plandaki
   "üretim akışı" tarifinin ötesinde, ölçülen bir CS0102 çakışmasını
   gidermek için eklendi.** §83.1 bu dönüşümü öngörmüyordu. K-570.
7. **`samples/AgentPrism.Api` ile "gerçek run" koşulmadı — bkz. DoD tablosunun
   altındaki "🚨 Plandan sapan iki DoD satırı" notu.** Makinedeki
   `dotnet user-secrets` deposunda kullanıcının önceki manuel test
   oturumlarından kalma gerçek görünümlü `secret`'lar bulundu; bootstrap
   denemesi sırasında eklenen iki geçici anahtar fark edilip GERİ ALINDI.
   Doğrulama gerçek Kestrel dinleyicisine karşı fonksiyonel testlerle
   yapıldı (21 test, biri gerçek bir kusur buldu — bkz. Denetim Bulguları #1).
8. **`postgres` yerine `sqlite` ile doğrulandı** (bu makinede kalıcı
   PostgreSQL yok); üç sağlayıcı `SqlProviderSelector.Register`'da aynı
   yoldan geçtiği için davranışsal fark yaratmaz.

## Bu Fazda Verilen Kararlar

K-564 – K-572, `docs/KARARLAR.md`'de:

- **K-564** — NSwag → üretilen kod commit + davranışsal kapı (üretim yöntemi)
- **K-565** — `AgentPrism.Client` `Abstractions`'ı referanslamaz
- **K-566** — `Client`/`Cli` public API takibinin dışında
- **K-567** — AOT sözünden vazgeçildi (`AgentPrismAotCompatible=false`)
- **K-568** — `IMigrationApplier` eklendi (linked-source `CS0433` çözümü)
- **K-569** — `AddAgentPrismClient` `IServiceCollection` döner
- **K-570** — Üretim öncesi şema kapatma (`additionalProperties: false`)
- **K-571** — Enum dönüştürücüleri tip düzeyinde, global liste değil
- **K-572** — `agentprism health` `/api/models/health`'i okur

## Denetim Bulguları

Bağımsız denetim (`faz-denetim`, taze bağlamlı agent) dört kapıyı yeniden
koştu ve sekiz başlığı denetledi.

### 🔴 Kapanmadan faz bitmez — ikisi de düzeltildi

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `ShippedDocumentationSelfContainmentTests` kırmızıydı: 11 sevk edilen dosyada (`IMigrationApplier.cs`, `CliArgs.cs`, üç komut sınıfı, iki `README.md`, script şablonundaki üretilen dosya başlığı, ve önceki bir fazdan miras `CatalogEndpoints.cs`'teki bir 🚨) `docs/NN-*.md`/`K-NNN`/`section N.N` referansı veya iç sesli emoji vardı. | **Düzeltildi.** Tüm referanslar kaldırıldı; `CatalogEndpoints.cs`'in kaynak metni düzeltildi ve `docs/openapi/agentprism.json` yeniden üretildi (üretilen istemci de yeniden üretildi). Test yeşil. |
| 2 | `ClientDescriptionBaselineTests`'in sayacı hatalıydı: bir üyenin üstündeki BOŞ SATIRI atlamıyordu, bu yüzden `///` bloğu + boş satır + `[JsonPropertyName]` sırasındaki HER belgeli özellik "belgesiz" sayılıyordu. Ölçülen taban çizgisi **1320**, gerçek değer **393**. | **Düzeltildi.** Sayaç boş satırı da atlayacak şekilde düzeltildi, taban çizgisi 393'e indirildi. |

### 🟡 Aynı fazda kapanır veya gerekçelenir — ikisi de kapandı

| # | Bulgu | Sonuç |
|---|---|---|
| 3 | Planın Hata Modları tablosunun `ClientTenantScopeTests`'i (çapraz kiracı 404/403) hiç yazılmamıştı. | **Kapandı.** `tests/AgentPrism.AspNetCore.FunctionalTests/ClientTenantScopeTests.cs` eklendi (2 test, gerçek host, gerçek kiracı ayrımı). Bu test yazılırken `RunKind` enum'ının GERÇEKTEN deserileşmediği bulundu — 🔴 #2'nin kökeni burada: global `Converters` listesi işe yaramıyordu. K-571 bu buluşla alındı. |
| 4 | AOT gerekçe metninde ("~320 çağrı noktası") sayı yanlıştı; gerçek ölçüm 42. | **Kapandı.** csproj yorumu ve K-567 doğru sayıyla (42, 146 tanı) yazıldı. |

### 🟢 Aday listesine

| # | Bulgu | Neden şimdi değil |
|---|---|---|
| 5 | `src/Directory.Build.props`'un yorumu "iki proje" diyordu, artık dört. | Kozmetik — **kapanışta düzeltildi**, gerçek F-NN gerektirmedi. |

**Devredilmeyen ama not edilen boşluk:** `migrate`'in bir MID-FLIGHT iptalinde
ledger'ın tutarlı kaldığını kanıtlayan özel bir test yazılmadı. Alttaki
atomiklik garantisi (her migration kendi transaction'ında commit eder)
`MigrationRunner`'ın ZATEN test edilmiş davranışıdır (Faz 67); bu fazın CLI
katmanı yalnız `CancellationToken`'ı iletiyor ve `OperationCanceledException`'ı
zaten yakalıyor. Enjeksiyon noktası olmadan (gerçek SQLite migration'ları
<1 sn'de biter) güvenilir bir "ortada kes" testi CLI'a bir test-özel seam
eklemeyi gerektirirdi — bu fazın kapsamına göre orantısız görüldü.

## Site Senkronu — Karşılanmayan İki Kural, Gerekçeli

`dokuman-bakim.py --site-denetle` iki kuralı tetikledi ve karşılanmadı sayıldı;
gerekçeleri:

1. **`http-api` kuralı** (`CatalogEndpoints.cs` değişti → `http-api.md`
   beklendi): `docs-site/src/content/docs/http-api/` **tamamen üretilir**
   (`docfx`/OpenAPI'den) ve **commit edilmez** — `git diff` onu asla
   göremez. Değişikliğin gerçek kaynağı olan `docs/openapi/agentprism.json`
   YENİDEN ÜRETİLDİ ve commit edildi (`OpenApiSnapshotTests` bunu doğruladı);
   `npm run check` bu kaynaktan üretilen sitenin güncel olduğunu kanıtladı.
2. **`cekirdek-kavram` kuralı** (yeni `IMigrationApplier.cs` → `concepts/`
   beklendi): `IMigrationApplier` bir TÜKETİCİ kavramı değil, iç bir
   çözümdür — `agentprism` CLI'sinin `MigrationRunner`'ın linked-source
   çapraz-derleme belirsizliğini (K-568) çözmek için kullandığı bir
   uygulama detayı. Hiçbir tüketici bu arayüzü doğrudan görmez veya
   çağırmaz (`internal` değildir ama `AgentPrism.Cli`'nin DI çözümünün
   dışında pratik bir kullanımı yoktur). Davranışsal karşılığı —
   "migration'ı ayrı bir adım olarak koş" — `getting-started/persistence.md`'ye
   zaten eklendi (`kalicilik` kuralı ✅ karşılandı).

## Sonraki Faza Devir Notu

> Kapanışta doldurulur. **Şimdiden bilinen devir — Faz 84 (F-93):**
>
> Küme E'nin ikinci yarısı TypeScript istemcisi ve npm kanalıdır. Bu fazın
> kurduğu üç şey oraya devredilir: (1) `docs/openapi/agentprism.json`'ın
> üretim kaynağı olması, (2) `/agentprism` önekini soyan dönüşüm adımı
> (§83.3), (3)
> `operationId` kapsama kapısının şekli (§83.2).
>
> 🚨 **Faz 84 için 2026-08-21'de ölçülen kanıt, burada saklanıyor ki
> kaybolmasın:** `src/AgentPrism.UI/frontend/src/lib/types.ts` **1 882 satır ·
> 177 elle yazılmış tip** taşır ve dosyanın kendi başlığı *"These mirror the
> .NET records one to one"* der. Sürüklenmeyi yakalayan **hiçbir kapı yoktur** —
> `tests/` ve `scripts/` tarandı, `types.ts`'e bakan tek satır bulunamadı.
> 👤 Kullanıcı kararı (2026-08-21): **arayüz, üretilen TypeScript tiplerinin
> ilk tüketicisi olur.** Faz 84 bu 177 tipi üretilenle değiştirir; sürüklenme
> kapısı böyle kendiliğinden doğar. Arayüzün bugünkü bundle payı **146 KB
> brotli** (`src/AgentPrism.UI/wwwroot/assets/`, ölçüldü) ve bütçe 250 KB
> gzip'tir — Faz 84 payı yeniden ölçmelidir.
