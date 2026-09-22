# Faz 182 — Public API Yüzey Daraltma

> **Durum:** 📋 Planlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-259**
> **Önkoşul:** [Faz 181](arsiv/fazlar/181-PROVIDER-ORTAK-KATMANI.md) **önerilir** (zorunlu değil) — provider iç tipleri incelmeden envanter iki kez yapılmasın
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
3. [`YAYIN-HAZIRLIK.md`](YAYIN-HAZIRLIK.md) §2 ve §13 — sürüm politikası ve
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
| [`Tracon.Client.csproj`](../src/Tracon.Client/Tracon.Client.csproj) | `TraconPublicApiTrackingEnabled=false` — ~250 üretilmiş DTO takip dışı (OpenAPI drift kapısı gerekçesiyle; bu fazda yeniden değerlendirilir) |
| [`README.md`](../README.md) durum bloğu | "The public API is **not frozen** … the surface may still be reduced before 1.0" — vaat verilmiş, iş planlanmamıştı |

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

- [ ] Envanter üretildi; paket başına üç sınıfın sayıları bu dokümana yazıldı
- [ ] "Kanıtsız" sınıfı işlendi: her tip ya `internal` oldu ya kalma gerekçesi envantere yazıldı
- [ ] `wc -l src/*/PublicAPI.Unshipped.txt` toplamı ölçüldü ve başlangıç (9.771) ile birlikte kapanışa yazıldı — sayı **düşmüş** olmalı
- [ ] 6 dış sample packed sürüme karşı derleniyor ve koşuyor
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri koşuldu (yukarıdaki iki case)
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` API referansı yeniden üretildi; link kapısı temiz

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
7. **Plan dışı kusurlar kapatıldı** (kusur-giderme protokolü, düşen test önce):
   `ToolArgumentConditionFingerprint` (K-851 — SQL store'ları iki koşul kümesini
   birleştiriyordu, bellek içi store boşluk farkını iki kural sayıyordu);
   bellek içi MCP store'unun anahtarındaki ham U+001F → tuple; `IApiKeyStore`,
   `IJobStore`, `IWorkflowCheckpointStore`, `AuditEntry`, `JobSchedule.NextRunAt`
   dokümanı eksik/yanlış yükümlülükleri söylüyordu; public dokümandan internal
   `FreeFormJson`'a giden üç `cref`; bir Türkçe XML açıklaması; artık gereksiz
   RS0041 `NoWarn`'ı (K-423); site betiğinde `MigrationRunner` için ölü özel durum.

## Bu Fazda Verilen Kararlar

- **K-850** — Public yüzey dış kanıt ölçütüyle daraltıldı: 91 tip `internal`;
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

`internal` olan 91 tipin tam listesi `CHANGELOG.md` `[Unreleased]` → `Removed`
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
scripts/public-yuzey-gerekceleri.tsv         (yeni; 13 kök gerekçe)
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

## Süreç Ölçümü

> Kapanışta doldurulur.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
