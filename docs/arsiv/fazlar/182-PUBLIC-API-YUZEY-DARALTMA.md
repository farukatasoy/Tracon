# Faz 182 — Public API Yüzey Daraltma

> **Durum:** ✅ Tamamlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-259**
> **Önkoşul:** [Faz 181](181-PROVIDER-ORTAK-KATMANI.md) **önerilir** (zorunlu değil) — provider iç tipleri incelmeden envanter iki kez yapılmasın
> **Paketler:** ölçüm tüm paketler; daraltma beklenen ağırlık `Tracon.Abstractions`, `.Core`, `.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Küçülüyor** — preview'de kırıcı değişiklik kabul edilir; README zaten "surface may still be reduced before 1.0" vaadini taşıyor
> **Tüketici yüzeyi:** `docs-site/` API referansı üretilir (etkilenir); sevk edilen: paket README'lerindeki tip/arayüz sayıları (A-53/A-54 kapıları)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` — packed-consumer case'leri regresyon görevi görür

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 2e4e2022:docs/arsiv/fazlar/182-PUBLIC-API-YUZEY-DARALTMA.md
> ```
>
> Damıtıldı 2026-09-22 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Yüzey bugün dondurulamayacak kadar büyük: 17 pakette **9.771 satır** `PublicAPI.Unshipped.txt`, tamamı boş `Shipped` dosyaları. GA freeze (UR-003) bu yüzeyin her satırını ömür boyu taşınacak sözleşmeye çevirecek.

## Bitiş Ölçütleri (DoD)

- [x] Envanter üretildi; paket başına sınıf sayıları bu dokümana yazıldı — "Gerçekleşen Public API" tablosu (dört sınıf: plan üç diyordu, `gerekçeli` eklendi — Sapma 1)
- [x] "Kanıtsız" sınıfı işlendi: 108 adaydan (paket×tip) 93'ü — 91 ayrı ad — `internal` oldu, 15'i kaldı: 12 kök gerekçe + 3 imza kapanışı (`scripts/public-yuzey-gerekceleri.tsv`); `python3 scripts/public-yuzey-envanteri.py --denetle` → çıkış 0, kanıtsız **0**
- [x] `wc -l src/*/PublicAPI.Unshipped.txt` toplamı: **9.771 → 9.119** (tip: 766 → 673)
- [x] 6 dış sample packed sürüme karşı derleniyor ve koşuyor — `kapi.py yayin --kuru`, `e284868b`, sürüm `1.0.0-preview.2.10`: 101 · 38 · 11 · 15 · 18 · 10 test, AOT smoke geçti
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban 4afe41f7`: build 0 uyarı · 22 test projesi (714 sn) · pack · `dotnet format --verify-no-changes` · performans · site; ilk koşumda yalnız `llms-full.txt` bayattı (site metni düzenlendikten sonra yeniden üretilmemişti), yeniden üretildi ve kapanış yeniden koşuldu
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — "Örnek Uygulama Koşumu" bölümü
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ (17 işaretli sentetik credential atlandı)
- [x] Manuel kabul case'leri koşuldu — `MT-PKG-123` (envanter) ✅ · `MT-PKG-124` (yayın provası) ✅ · `MT-PKG-125` ➜ CI · `MT-SEC-199` ✅ (canlı SQLite)
- [x] `faz-denetim` koşuldu; 🔴 bulgu yok, üç 🟡 aynı fazda kapandı ("Denetim Bulguları")
- [x] `docs-site/` API referansı yeniden üretildi (**673 tip**, 16 paket); `npm run check`: 1060 sayfa, kırık bağ yok, SEO 0 hata

### Doğrulama komutları

```bash
wc -l src/*/PublicAPI.Unshipped.txt | tail -1
python3 scripts/kapi.py yayin --kuru
```

---

## Plandan Sapmalar

1. **Kanıt tanımı genişledi — dört mekanik kök ve iki kapanış eklendi.** Plan
   tüketici kanıtını site · sample · şablon · XML `<example>` olarak sayıyordu.
   Ölçüm dört boşluk gösterdi: (a) **kayıt giriş noktası** (K-509) — tüketici
   `AddX()` çağırır, uzantı sınıfının adını yazmaz; (b) **OpenAPI şeması** —
   `XmlCommentGenerator` yalnız public tipin dokümanını belgeye taşır (ölçüldü:
   önbellekte 604 tip, sıfırı internal), şema tipini daraltmak sevk edilen HTTP
   sözleşmesinin açıklamasını siler; (c) **imza kapanışı** — kalan bir tipin
   public imzasında geçen tip kalmak zorundadır (`CS0050`/`CS0051`), aksi hâlde
   envanter derlenmeyen bir aday listesi üretir; (d) **`<exception cref>`
   kapanışı** — `IEvalStore.DiffRunsAsync` ve `AgentSessionManager` belgeli
   istisnalarını adlandırır, derleyici bunu zorlamaz. Dördüncü sınıf
   `gerekçeli` eklendi: kanıtı olmayan ama bilinçli kalan tip, gerekçesiyle
   `scripts/public-yuzey-gerekceleri.tsv`'ye yazılır (planda dosya yoktu).
2. **Başlangıç sayısı 764 değil 766 tip.** Site ölçümü `Tracon.MigrationRunner`'ı
   bir kez sayıyordu; aynı tam ad üç SQL paketinde public'ti (üç `Unshipped`
   satırı). Bu bir kusurdu — iki sağlayıcıya bağlı tüketici `CS0433` alıyordu
   (repro: iki `ProjectReference`'lı scratch proje) — ve daraltma dalgasına girdi.
3. **Açık Soru 1 (`Tracon.Client`) = B — ama yeni K açılmadı.** İstisna zaten
   K-566 olarak kayıtlıydı; satıra "yeniden değerlendirildi, korundu" notu düştü.
4. **Açık Soru 2 (`Testing.Contracts.Xunit`) = B.** Paketin 49 tipi bütünüyle
   `seam` sayıldı. Yine de iki sözleşme gövdesi iç yardımcıya dayanıyordu
   (`JobPayload.ExtractItems`, `WorkflowCheckpointState.IsOmitted`); sevk edilen
   bir sözleşmeye `InternalsVisibleTo` açmak yerine iki assert yapısal kontrole
   çevrildi. Sözleşmeye dokunulduğu için yayın provası zorunlu oldu.
5. **Birinci taraf gövde kullanımı `InternalsVisibleTo` ile çözüldü (K-850).**
   Plan bunu söylemiyordu. Yeni IVT: Abstractions → AspNetCore · Mcp · 3 SQL
   paketi; Core → Voice · Workflows · Cli; ve sekiz test projesi.
6. **Adaylar tek kişi tarafından değil, 4 yargıç + 4 bağımsız şüpheci ile
   elendi.** Şüpheciler iki kararı çevirdi: `AgentParameterValidator` kaldı
   (public `CompileParameterizedAsync` dokümanı çağırana onu önce çağırmasını
   söylüyor), `WorkflowCheckpointState` internal oldu (değer `{}`'dir; seam
   dokümanı artık bunu düz metinle söylüyor).
7. **Plan dışı kusurlar kapatıldı** — ayrıntı "Faz Dışı Bulunan ve Kapatılan
   Kusur" bölümündedir (K-851 dahil).

## Bu Fazda Verilen Kararlar

- **K-850** — Public yüzey dış kanıt ölçütüyle daraltıldı: 91 tip adı (93 paket×tip) `internal`;
  birinci taraf gövde kullanımı `InternalsVisibleTo` ile çözülür; OpenAPI şema
  tipi HTTP sözleşmesi olarak kalır; gerekçeler `scripts/public-yuzey-gerekceleri.tsv`.
- **K-851** — Koşul kümesi parmak izi tek iç fonksiyonda; ayırıcı taşıyan yol
  uzunluk önekli biçime geçer, mevcut `conditions_hash` satırları geçerli kalır.
- Notlar: K-422 (kısmen geçersiz) · K-423 (yerine geçildi) · K-566 (korundu) ·
  K-568 (tamamlandı) · K-596 (görünürlük değişti).

## Tüketici Yüzeyi Envanteri

`tuketici-dokuman-senkronu` Adım 1 · 5. Üç kova:

1. **`docs-site/`** — elle: `guides/embedding.md` · `guides/production.md`
   (`MigrationRunner.ApplyAsync()` → `IMigrationApplier` / `tracon migrate`),
   `getting-started/persistence.md` (süreç içi `IMigrationApplier` yolu eklendi),
   `concepts/governance.md` (kuralın kimliği: koşul sırası ve değer içi boşluk
   sayılmaz, tekrar `409`). Üretilen: `api/` (**673 tip**), `http-api/`
   (`AuditEntry` açıklamaları).
2. **Sevk edilen metin** — `README.md` (673) · `src/Tracon.Abstractions/README.md`
   (407 tip / 85 arayüz) · `src/Tracon.SqlServer/README.md` · `CHANGELOG.md`
   (`Removed` + iki `Fixed`) · `///` dokümanı: internal tipe giden `cref`'ler ve
   "public because" cümleleri düz metne çevrildi; `IApiKeyStore`, `IJobStore`,
   `IWorkflowCheckpointStore`, `IMigrationApplier`, `IStatePreflightReader`,
   `AuditEntry`, `JobSchedule.NextRunAt` yükümlülükleri yazıldı ·
   `docs/openapi/tracon.json` + `Tracon.Client` + `@tracon/client` yeniden üretildi
   (yalnız `AuditEntry`'nin üç açıklaması değişti).
3. **Yerel referans ve agent haritası** — yeni yetenek yok, `capabilities.md`
   değişmedi; `llms-full.txt` site metninden yeniden üretildi.

Kapılar: `ShippedDocumentationSelfContainmentTests` · `CapabilityExampleTests` ·
`SourceLanguageTests` (Core.UnitTests ✅) · `LocalReferenceTests` (Package.Tests ✅) ·
`build-agent-map.mjs --check` ✅ · `npm run check` ✅ (1060 sayfa, kırık bağ yok,
SEO 0 hata) · `dokuman-bakim.py --site-denetle`: 2 kural sayfayla karşılandı, 3
kural gerekçeyle geçti:

- `model-saglayici` — daraltılan `*ChatClientFactory`/`*ModelCatalog` hiçbir site
  sayfasında adlandırılmıyordu (envanter kanıtı); `Use*` kaydı ve seçenekler aynı.
- `paket-tanimi` — csproj değişikliği yalnız `InternalsVisibleTo` ve kalkan RS0041
  `NoWarn`'ıdır; paket kimliği, açıklaması ve bağımlılığı değişmedi.
- `paket-readme` — README'de değişen yalnız tip sayısıdır; `packages.md` sayı taşımaz.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, `ASPNETCORE_ENVIRONMENT=Staging` (user-secrets yüklenmesin
diye), `Tracon__Sqlite__ConnectionString` ve atılabilir bir
`Tracon__ContentProtection__RawKeys__sample` ile (2026-09-22):

- `GET /tracon/api/diagnostics` → `persistenceProvider: SQLite`, `canConnect: true`,
  `migrationsUpToDate: true` — migration runner artık `internal` ve DI'dan
  `ISqlPersistenceDiagnostics` üzerinden çözülüyor.
- `POST /tracon/api/agents/support/run` → SSE: `run` · 8 `update` · `done`;
  `GET /tracon/api/runs/{id}` → `Completed`, SQLite'ta kalıcı.
- `MT-SEC-199` canlı: ayırıcı taşıyan yol `201`, iki koşullu kural `201` (iki
  koşul, birleşmedi), `region In ["eu", "us"]` `201`, `["eu","us"]` `409`; liste 3 kural.
- 🚨 İlk deneme içerik koruma anahtarı olmadan koştu: `run` `event: error` ile
  bitti ve kayıt `Running`'de kaldı — `RunRecordingAgent` kaydı devre dışı bırakıp
  uyarı logladı (tasarım: gözlemlenebilirlik işlevi bozmaz; öksüz `run`'ı
  uzlaştırma kapatır). Bu faza ait değil; yapılandırma eksikliğidir.

## Faz Dışı Bulunan ve Kapatılan Kusur

Hepsi `kusur-giderme` ile: önce düşen test, sonra düzeltme, sonra sınıf taraması.

| Kusur | Kanıt (önce kırmızı) | Düzeltme | Sınıf taraması |
|---|---|---|---|
| `Tracon.MigrationRunner` üç SQL paketinde aynı tam adla public — iki sağlayıcılı tüketici `CS0433`, site ise onu çağırmayı öğretiyordu | iki `ProjectReference`'lı scratch proje `CS0433`; `PublicSurfaceBaselineTests.A_public_type_name_is_declared_by_only_one_package` kırmızı | `internal` + dokümanlar `IMigrationApplier`'a | yeni kapı her paket çiftini tarar; tek vaka buydu |
| SQL onay kuralı store'ları ayırıcı taşıyan yolda iki farklı koşul kümesini birleştiriyordu (`ON CONFLICT` birinciyi döndürüyordu) | `ToolApprovalRuleStoreContract.A_path_carrying_separator_characters_…` SQLite'ta kırmızı | `ToolArgumentConditionFingerprint` (K-851), eski biçim korunur | `u001F`/`u001E`/ham kontrol baytı taraması: bir vaka daha (aşağıda) |
| Bellek içi store `["eu", "us"]` ile `["eu","us"]`'yi iki kural sayıyordu | `…differs_only_in_whitespace…` bellek içinde kırmızı | aynı fonksiyon | — |
| Bellek içi MCP store anahtarı ham U+001F ile birleştirilmiş dizeydi (kaynakta görünmez) | ham bayt taraması (`b'\x1f'`) | tuple anahtar | `src/` tamamında başka ham kontrol baytı yok |
| Public dokümandan internal `FreeFormJson`'a üç `cref`; `AuditEntry` her kaydın süzgeçten geçtiğini söylüyordu (doğrudan `IAuditLog` yazımı geçmez); `JobSchedule.NextRunAt`, `IApiKeyStore`, `IJobStore` uygulayıcı yükümlülüğünü yazmıyordu | yargıç/şüpheci bulguları, kodla doğrulandı | doküman | daraltılan her ad için `cref`/`<c>` taraması |
| `InMemoryTenantStore`'un Türkçe XML açıklaması (Faz 96'dan beri biliniyordu) | — | İngilizce | ASCII-Türkçe `///` taraması: başka vaka yok |
| Gereksiz RS0041 `NoWarn`'ı (K-423) ve sitede `MigrationRunner` için ölü özel durum | — | kaldırıldı | — |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan değişmedi; yedi sapma "Plandan Sapmalar"da |
| Düzeltme turu sayısı | 1 — denetimin 3 🟡 bulgusu tek turda kapandı |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 — `ic-dongu` (782 sn tam test) ve yayın provası ilk koşumda yeşil; ilk prova yalnız yerel feed'deki eski paketler yüzünden durdu (ortam, kod değil) |
| Faz kapandıktan sonra bulunan kusur | 0 |

## Denetim Bulguları

`faz-denetim` taze bağlamlı `faz-denetcisi` ile koşuldu (taban `4afe41f7`,
aralık `…e284868b`). **🔴 yok · 🟡 3 · 🟢 4.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Sevk edilen sözleşme yeni bir kimlik kuralı zorluyor (liste değerinde boşluk sayılmaz) ama seam dokümanı söylemiyor; kuralı uygulayan fonksiyon `internal`; `CHANGELOG` sözleşme uygulayıcısına bir şey demiyor. Site cümlesi fazla genişti ("whitespace inside a value") | **Düzeltildi** — kural `IToolApprovalRuleStore.AddAsync` `<remarks>`'ına yazıldı (sıra bağımsız · liste eleman eleman · diğer her değer tam JSON metni, `100` ≠ `100.0`); `CHANGELOG` `Changed`'e uygulayıcı satırı; `governance.md` daraltıldı (`"new york"` ≠ `"newyork"`) |
| 2 | 🟡 | K-851'in "mevcut `conditions_hash` geçerli kalır" iddiasını commit'teki hiçbir test sabitlemiyordu | **Düzeltildi** — `ToolArgumentConditionFingerprintTests` (5 test) commit'e girdi; eski ön görüntünün SHA-256'sı sabitlendi (koddan bağımsız), denetçi de aynı değeri bağımsız üretti |
| 3 | 🟡 | Sayılar çelişiyordu: "91 tip" (ad) ile 766 − 673 = 93 (paket×tip); "13 kök gerekçe" ama TSV'de 12 | **Düzeltildi** — her yerde "91 ad (93 paket×tip)" ve "12 kök + 3 imza kapanışı"; K-851 cümlesi de daraltıldı ("yalnız yolunda ayırıcı taşıyan kümelerin izi değişir") |
| 4 | 🟢 | IVT ayrı sürümlenen paketler arasında sürümsüz bir sözleşmedir (çalışma anı `MissingMethodException`) | **Aday F-265** — paketleme kararı |
| 5 | 🟢 | `ToolArgumentValidationContract` dokümanı hâlâ internal `ValidatingAIFunction`'ı adlandırıyordu | **Düzeltildi** |
| 6 | 🟢 | Bellek içi MCP anahtarı düzeltmesinin regresyon testi yoktu | **Düzeltildi** — `InMemoryMcpServerStoreTests` (eski anahtarla kırmızı olduğu ölçüldü) |
| 7 | 🟢 | `JobSchedule.NextRunAt` dokümanı ilk çalışma zamanını istiyor ama cron hesaplayan public yol (`CronExpression`) internal oldu | **Düzeltildi** — "şimdi ya da öncesi bir zaman sonraki geçişte çalıştırır" cümlesi eklendi |

Denetçinin özellikle sorulan beş noktadaki cevabı: adıyla gerekli olup `internal`
olan tip yok; public dokümanda internal tipe `<see cref>` kalmadı; her yeni IVT
satırı gerçekten kullanılıyor, fazlası yok; K-851 ayırıcısız kümelerde eski
hash'i birebir üretiyor (eski dosyayla satır satır karşılaştırıldı); test
tiyatrosu yok.

## Sonraki Faza Devir Notu

**Public yüzey artık ölçülür; tahmin edilmez.** `python3 scripts/public-yuzey-envanteri.py`
her public tipi tüketici · seam · gerekçeli · kanıtsız diye sayar,
`--tip <ad>` bir tipin neden kaldığını köke kadar gösterir, `--denetle` kanıtsız
tip ya da bayat gerekçe varsa 1 döner. Bugün kanıtsız **0**. Üç kural kalıcıdır:

- 🚨 **Birinci taraf gövde kullanımı public kalma gerekçesi değildir** (K-850) —
  derleme `CS0122` ile hangi derlemenin IVT istediğini söyler; o satır gerekçeli
  yoruma yazılır. Sürüm karışması riski F-265'tir.
- 🚨 **OpenAPI şema tipi daraltılmaz** — `XmlCommentGenerator` yalnız public tipin
  dokümanını sevk edilen belgeye taşır. Envanter şema adını tüketici kanıtı sayar.
- 🚨 **Aynı tam ad iki pakette public olamaz** — `PublicSurfaceBaselineTests.A_public_type_name_is_declared_by_only_one_package`.
  `*.Shared` bağlı kaynak ağaçlarındaki her tip `internal` kalır (Sql.Shared
  README'si artık bunu yazıyor).

Yeni public tip ekleyen faz ya onu bir site sayfası/sample/`<example>` ile
tüketiciye gösterir ya da `scripts/public-yuzey-gerekceleri.tsv`'ye gerekçesini
yazar. Bu bugün bir kapı değil (`--denetle` CI'da koşmuyor); GA freeze turu
(UR-003) envanteri yeniden koşar ve kanıtsız sütunu o turun iş listesidir.
Kapıya çevirmek bir kullanıcı kararıdır.

**Faz 183'e:** bkz. `docs/183-COKLU-TFM-TEST-MATRISI.md` "Bu Faza Başlarken" 5.
madde — yeni test IVT'leri derleme adıyla eşleşir, TFM'den bağımsızdır.

**Yarım kalan iş:** Yok. Site **yayınlanmadı** (`site-deploy.sh` dış bir sunucuya
yazar; kullanıcı onayı bekler) — yerel `npm run check` yeşil.
