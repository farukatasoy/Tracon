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

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-421\|K-602\|K-603" docs/KARARLAR.md
   grep -n "UR-003\|KG-016" docs/YAYIN-HAZIRLIK.md
   ```
   **K-421** (public API takibi açık), **UR-003/KG-016** (freeze taraması GA
   turuna ertelendi — bu faz freeze DEĞİLDİR, freeze'i ucuzlatan daraltmadır)
3. [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §2 ve §13 — sürüm politikası ve
   GA'ya ertelenenler
4. Sözleşme dokümantasyon kapısı: `SeamContractDocumentationTests` (küçülen
   taban 174 satır) — daraltılan her arayüz bu tabanı da küçültür
5. Faz 181 devri (önkoşul tamamlandı, 2026-09-22) — `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/181-PROVIDER-ORTAK-KATMANI.md`.
   Özet: provider iç tipleri inceldi, **public yüzey değişmedi** (OpenAI 61 ·
   Anthropic 33 · Google 39 · Azure 33 satır `PublicAPI.Unshipped.txt`, net
   değişim 0). 🚨 `src/Tracon.Providers.Shared/` altındaki her tip `internal`
   kalmak **zorundadır** — oraya bir public tip taşımak dört derlemede aynı adlı
   tip üretir (`CS0433`). Provider paketlerinde daraltma adayı ararken kabuk
   sınıflar (`*ModelProvider`, `*ProviderHealthCheck`) zaten `internal`'dır;
   public olanlar `*ProviderExtensions`, `*ProviderOptions`, `*ProviderNames`,
   `*ChatClientFactory`, `*ModelCatalog`, `*ImageBuilderExtensions` ve OpenAI'nin
   fazlasıdır (live, compatible). `*ChatClientFactory` ile `*ModelCatalog`'un
   dış kanıtı envanterde ilk sorulacak soru olmalı — kabuk bunları yalnız
   içeriden kullanır.

---

## Amaç

Yüzey bugün dondurulamayacak kadar büyük: 17 pakette **9.771 satır**
`PublicAPI.Unshipped.txt`, tamamı boş `Shipped` dosyaları. GA freeze (UR-003)
bu yüzeyin her satırını ömür boyu taşınacak sözleşmeye çevirecek. Preview
penceresi kırıcı değişikliğin **ucuz olduğu son dönemdir**; bu faz dış kanıtı
olmayan public üyeleri `internal`'a çeker ve GA freeze'in faturasını küçültür.

- **F-259** — public yüzey envanteri + dış-kanıt sınıflandırması + daraltma
  dalgası; `Shipped` doldurma **kapsam dışı** (o UR-003'tür).

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `wc -l src/*/PublicAPI.Unshipped.txt` | **9.771 toplam**: Abstractions 5.764 · Core 1.475 · AspNetCore 1.236 · Testing.Contracts.Xunit 899 |
| `wc -l src/*/PublicAPI.Shipped.txt` | Hepsi boş (`#nullable enable` başlığı hariç) — taban çizgisi yok; kapı yalnız *kayıtsız* değişikliği yakalıyor, *kırıcıyı* değil |
| [`Tracon.Client.csproj`](../../../src/Tracon.Client/Tracon.Client.csproj) | `TraconPublicApiTrackingEnabled=false` — ~250 üretilmiş DTO takip dışı (OpenAPI drift kapısı gerekçesiyle; bu fazda yeniden değerlendirilir) |
| [`README.md`](../../../README.md) durum bloğu | "The public API is **not frozen** … the surface may still be reduced before 1.0" — vaat verilmiş, iş planlanmamıştı |

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 182.1 — Envanter: her public üyenin dış kanıtı

Unshipped satırları paket başına dökülür ve her public TİP üç sınıftan birine
düşer (üye düzeyi değil — tip düzeyi; 9.771 satır üye listesidir, tip sayısı
site ölçümünde 764'tü):

| Sınıf | Kanıt | Kader |
|---|---|---|
| Tüketici yüzeyi | site sayfası · sample kullanımı · şablon çıktısı · XML `<example>` | Kalır |
| Genişleme noktası | seam envanteri (YAYIN-HAZIRLIK §15 kaydı) · `Testing.Contracts` sözleşmesi | Kalır |
| Kanıtsız | hiçbiri | **`internal` adayı** — varsayılan kader budur; kalması gerekçe ister, gitmesi istemez |

Envanter mekanik üretilir (Unshipped ayrıştırma + site/sample grep), el ile
sınıflandırılır. Ölçüm scripti `scripts/` altına girer ve kalıcıdır — GA
freeze turu aynı scripti kullanır.

## 182.2 — Daraltma dalgası

`internal` adayları paket paket çekilir; her dalganın kapısı: dört doğrulama
kapısı + packed-consumer sample'ların (6 dış sample, `kapi.py yayin` yolu)
derlenmeye devam etmesi. Derlemesi bozulan sample = o tip aslında tüketici
yüzeyiymiş; envanter güncellenir, tip kalır.

## 182.3 — `Tracon.Client` istisnasının yeniden değerlendirilmesi

Üretilmiş ~250 DTO'nun takip dışılığı bu fazda karara bağlanır: ya takip
açılır (üretilen yüzey de sözleşmedir) ya da istisna gerekçesi K-defterine
yazılır. Öneri Açık Sorular tablosundadır.

---

## Planlanan Public API

Yeni üye yok; net değişim **negatiftir**. Taslak imza yazılmaz — bu fazın
çıktısı imza değil, silinen satırdır.

### HTTP `endpoint`'leri

Yok — HTTP yüzeyi bu fazın kapsamı dışında (o yüzeyin sözleşmesi OpenAPI
dokümanı ve ayrı kapılarladır).

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
scripts/public-yuzey-envanteri.py       (envanter üretici; kalıcı)
scripts/public_yuzey_envanteri_test.py
src/*/PublicAPI.Unshipped.txt           (küçülür)
src/*/*.cs                              (public → internal çekimleri)
docs/182-PUBLIC-API-YUZEY-DARALTMA.md   (envanter özeti kapanışta buraya)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Tüketicinin gerçekten kullandığı tip `internal` olur | Paket | 6 dış sample packed sürüme karşı derlenir (`kapi.py yayin` yolu; envanter kapısı BL-052) |
| `InternalsVisibleTo` daraltmayı görünmez kılar (test hâlâ geçer, tüketici kırılır) | Paket | aynı packed-consumer koşumu — IVT paket tüketicisine uygulanmaz |
| Envanter scripti tip sınıfını yanlış sayar | Script birimi | `public_yuzey_envanteri_test.py` — bilinen küçük örnek küme üzerinde |
| Site API referansı silinen tipe bağlanır | Site | `docs-site` build + link kapısı (`check-links`) |
| README/paket README tip sayıları bayatlar | Kapı | mevcut A-53/A-54 operation/type sayacı kapıları |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Daraltma dalgası bitti | `python3 scripts/kapi.py yayin --kuru` | 6 dış sample packed sürüme karşı derlenir ve koşar |
| 2 | Envanter üretildi | `python3 scripts/public-yuzey-envanteri.py` | Paket başına: toplam / tüketici / seam / kanıtsız sayıları |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `Tracon.Client` DTO takibi | A: takibi aç, Unshipped'e üret · B: istisnayı K-defterine yaz | **B** — DTO'lar OpenAPI dokümanından üretiliyor ve drift kapısı zaten var; ikinci taban çizgisi aynı gerçeğin kopyası olur. Karar kapanışta K-NNN alır |
| 2 | `Testing.Contracts.Xunit`'in 899 satırı bu dalgaya girer mi? | A: evet · B: sözleşme paketi, GA'da ele alınır | **B** — sözleşme sınıfları tüketicinin türeteceği yüzeydir; kanıt tanımı gereği "kalır" sınıfına yakın, kazanç düşük |

---

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

## Riskler

| Risk | Önlem |
|------|-------|
| Preview tüketicisi kırılır | Sürüm politikası zaten "not frozen" diyor; değişiklik `CHANGELOG.md`'ye paket paket yazılır ve bir sonraki preview notunda duyurulur |
| Daraltma iştahı seam'leri de yutar | Seam envanteri (§15 kaydı) "kalır" sınıfının kanıtıdır; seam listesindeki hiçbir arayüz daraltılamaz |
| Envanter bir kerelik kalır, GA'da yeniden el işi olur | Script kalıcıdır ve test taşır; GA turu (UR-003) aynı scripti çalıştırır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

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

## Gerçekleşen Public API

**Net değişim negatif.** Yeni public üye: yalnız iki sözleşme case'i
(`ToolApprovalRuleStoreContract` — test metotları public API'dir).

| Paket | Tip önce → sonra | `Unshipped` satırı önce → sonra |
|---|---:|---:|
| `Tracon.Abstractions` | 418 → 407 | 5.764 → 5.689 |
| `Tracon.Core` | 164 → 95 | 1.475 → 942 |
| `Tracon.OpenAI` | 11 → 8 | 62 → 52 |
| `Tracon.Anthropic` · `.Azure` · `.Google` | 5·6·6 → 3·4·4 | 34·34·40 → 27·27·32 |
| `Tracon.PostgreSql` · `.SqlServer` · `.Sqlite` | 3·3·3 → 2·2·2 | 26·24·24 → 22·20·20 |
| `Tracon.Voice` | 3 → 2 | 32 → 30 |
| `Tracon.Testing.Contracts.Xunit` | 49 → 49 | 899 → 901 |
| Diğer 6 paket | değişmedi | değişmedi |
| **Toplam** | **766 → 673** | **9.771 → 9.119** |

`internal` olan 91 tip adının (93 paket×tip) tam listesi `CHANGELOG.md` `[Unreleased]` → `Removed`
bölümündedir. Envanter (2026-09-22, kapanış):

| Paket | Toplam | Tüketici | Seam | Gerekçeli | Kanıtsız |
|---|---:|---:|---:|---:|---:|
| `Tracon.Abstractions` | 407 | 355 | 41 | 11 | 0 |
| `Tracon.AspNetCore` | 81 | 79 | 1 | 1 | 0 |
| `Tracon.Core` | 95 | 92 | 0 | 3 | 0 |
| `Tracon.Testing.Contracts.Xunit` | 49 | 0 | 49 | 0 | 0 |
| Diğer 12 paket | 41 | 41 | 0 | 0 | 0 |
| **Toplam** | **673** | **567** | **91** | **15** | **0** |

Kalan 15 gerekçeli tip: `AgentParameterValidator` (+ `…Result`/`…Error` imza
kapanışıyla), `ApiKeyGenerator` (+ `GeneratedApiKey`), `EvalRunDiffBuilder`,
`JobLanes`, `RetentionTargets`, `RunEventCustomTypes`,
`TraconGeneratedToolArguments` (kaynak üreteci tüketicinin derlemesine kod
yazar — IVT orada çalışmaz), `TraconRunBudgetExceededException`,
`TraconStructuredResponseException`, `TraconToolTimeoutException`,
`WebhookEvents`, `TraconAgentSessionStore`.

## Dosya Listesi (gerçekleşen)

```
scripts/public-yuzey-envanteri.py            (yeni; kalıcı envanter)
scripts/public_yuzey_envanteri_test.py       (yeni; 29 test)
scripts/public-yuzey-gerekceleri.tsv         (yeni; 12 kök gerekçe)
src/*/PublicAPI.Unshipped.txt                (11 paket küçüldü; Contracts +2)
src/**/*.cs                                  (74 dosyada public → internal)
src/Tracon.Abstractions/Tracon.Abstractions.csproj   (IVT; RS0041 NoWarn kalktı)
src/Tracon.Core/Properties/AssemblyInfo.cs   (IVT)
src/Tracon.Core/Approvals/ToolArgumentConditionFingerprint.cs  (yeni, K-851)
src/Tracon.Sql.Shared/Stores/SqlApprovalAndMcpStores.cs        (K-851)
src/Tracon.Core/Storage/InMemoryApprovalAndMcpStores.cs        (K-851; MCP anahtarı)
src/Tracon.Testing.Contracts.Xunit/Contracts/{ToolApprovalRule,JobSchedule,WorkflowCheckpoint}StoreContract.cs
tests/Tracon.Core.UnitTests/Architecture/PublicSurfaceBaselineTests.cs  (+ ad tekilliği kapısı)
tests/Tracon.Package.Tests/ConsumerSurfaceTests.cs            (MigrationRunner çifti)
docs/openapi/tracon.json · src/Tracon.Client/Generated/TraconApiClient.g.cs ·
packages/tracon-client/src/schema.ts         (AuditEntry açıklamaları)
docs-site/.../guides/{embedding,production}.md · src/*/README.md · README.md · CHANGELOG.md
docs-site/scripts/build-api-reference.mjs    (ölü özel durum silindi)
docs/manuel-test/{00-INDEKS,01-KURULUM-VE-PAKETLEME,13-KIRACI-VE-GUVENLIK}.md
docs/hafiza/{analyzer-tanilari,dokumantasyon,http-uc-guvenlik-ve-sozlesme,tool-onay-ve-yetkilendirme,nswag-istemci-uretimi,altyapi-haritasi}.md
docs/KARARLAR.md                             (K-850, K-851 + beş not)
```

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
