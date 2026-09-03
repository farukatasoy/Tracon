# Keşif Turu — 2026-08-21 · İkinci tüketici gömme turu (ProdigyEnabler)

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni [`ADAYLAR.md`](../ADAYLAR.md) içinde
> yaşar; bu dosya yalnız oraya işaret eder.

**Tetikleyen:** Kullanıcı — "iki tüketici raporunu derinlemesine analiz et ve
`aday-kesfi` protokolünü koş".
**Girdi:** [`2026-08-21-tuketici-raporu.md`](2026-08-21-tuketici-raporu.md)
(Y1…Y9 + §4 ek öneriler + §5 açık sorular) ·
[`2026-08-21-uygulanabilirlik-raporu.md`](2026-08-21-uygulanabilirlik-raporu.md)
(§7 çatışma noktaları, §10 eksik yetenekler, §12 açık sorular).
Kaynak: ProdigyEnabler (ABP 10.5 · .NET 10 · PostgreSQL · Hangfire),
`0.0.0-preview.0.291` referanslı gerçek bir gömme denemesi. **Önceki tur aynı
tüketiciden geldi** ([2026-08-18](2026-08-18-tuketici-raporu.md)) ve
F-110…F-119'u doğurdu; Faz 69, 70, 72 ondan çıktı.
**Zemin:** Faz 78 kapalı, 79–83 planlandı · aday dosyasında 31 seçilmemiş kalem
· en büyük numara **F-139**.
**Ekosistem taraması:** ⚠️ **Bu turda taze web taraması YAPILMADI.** 2026-08-20
turu MAF/SK/MEAI paket yüzeyini tarar, rakip kontrol düzlemlerini **taramaz**.
Kalemlerin ekosistem satırları aday dosyasının kendi tarih damgalı kayıtlarından
devralındı ve **"ekosistem doğrulanmadı"** işaretiyle yazıldı.

🚨 **Turun tek cümlelik dersi:** *tüketici raporu bir spec değil, bir girdidir.*
Raporun 19 iddiasından **8'i doğrulandı, 5'i yanlış çıktı**, 6'sı zaten kapalıydı.
Ölçülmeden yazılsaydı beş kapatılmış iş yeniden açılırdı.

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| `grep -rho "F-[0-9]\{2,3\}" docs/` | En büyük numara **F-139** → yeni kalemler **F-140**'tan başlar |
| Son fazların devir notu | Faz 79–83 **planlandı ama uygulanmadı** — devir notları boştur, okunmaz. Son dolu not Faz 78'dir |
| Faz 78 devir notu §6 | Harita bütçesi 10 240 B, dolu 8 391 B → **~22 yetenek satırı** boşluk. F-140'ın maliyet satırı buna dayanır |
| Faz 78 "Bugün ne çalışmıyor" | Ölçüm **aynı tüketicinin** deposunda yapılmış (`prodigy-enabler-backend`) — bu tur onun devamıdır |
| Ekosistem boşluk tablosu | 15 satırın 14'ü plana girdi; kalan iki: F-34, F-72 ⏸ |
| `docs/ADAYLAR.md` boyutu | Tur başında **81 615 B / 80 000 B** — bütçe **aşılmıştı** (bkz. §8) |

---

## 2. Ham fikir listesi (Aşama 2)

Rapordan gelenler (**R**) ile kendi fikirlerim (**Ö**) kaynağı izlenebilsin diye
ayrı tutuldu.

| # | Fikir | Kim için | Sonuç |
|---|---|---|---|
| R1 | Talimat parametre şeması (Y1) | Ölçme–iyileştirme yapan tüketici | ✅ → **F-34** yeniden yargılandı |
| R2 | Kesilen agent turunun devamı (Y2) | Gece 03:00 nöbetçisi | ✅ → **F-141** |
| R3 | Görsel üretim tool'u (Y3) | FinOps | ✅ → **F-142** |
| R4 | Gömme örneği — beş nokta (Y4) | İlk agent'ını kuran | ✅ → **F-140** içinde |
| R5 | Workflow düğüm retry (Y5) | Nöbetçi | ✅ → **F-141** içinde 👤 |
| R6 | Sink tampon örneği/sarmalayıcı (Y6) | Köprü yazan tüketici | ✅ → **F-140** içinde, **yalnız örnek** 👤 |
| R7 | Tool çıktısı boyut sınırı (Y7) | FinOps | ✅ → **F-143** |
| R8 | Belge kanalı — veri ≠ talimat (Y8) | Kurumsal satın alma | ✅ → **F-34** içinde 👤 |
| R9 | TR/bölgesel PII ailesi (Y9) | — | ❌ **iddia yanlış** (§3) |
| R10 | Agent tanımı `Deprecated` durumu (§4.1) | Kurumsal platform | ⏸ bu turda değil |
| R11 | `Metadata` şema desteği (§4.3) | — | ⏸ ölçülmemiş ihtiyaç |
| R12 | Kota eşik bildirimi (§4.2) | — | ❌ **zaten var** (§3) |
| R13 | Koşu ağacı maliyet toplamı (§4.4) | — | ❌ **zaten var** (§3) |
| R14 | Kütüphane içi tanım doğrulayıcı (§4.5) | — | ❌ **zaten var** (§3) |
| Ö1 | Yetenek haritasına gömme ekseni | Tüketicinin kod agent'ı | ✅ → **F-140** |
| Ö2 | `AgentPrismRunContext` anlatıya girsin | Tool yazan tüketici | ✅ → **F-140** içinde |
| Ö3 | Bağlanmamış genişleme noktası tanısı (`APG`) | Gömen ekip | ✅ → **F-140** içinde |
| Ö4 | `/api/diagnostics` bağlı noktaları raporlasın | Nöbetçi | ✅ → **F-140** içinde |
| Ö5 | Kiracı senkronizasyonu **sözleşmesi** (kanca) | Çok kiracılı gömme | ⏸ bu turda değil — F-140 yalnız **belgeliyor** |
| Ö6 | Zarif kapanış (`ApplicationStopping` drain) | Nöbetçi | ✅ → **F-141** içinde |
| Ö7 | `IContentGuard` kayıt-modu | Guard açan tüketici | ⏸ **İddia** — ölçülmedi (§6) |
| Ö8 | Gömme kontrol listesi (havuz · migration sırası) | Platform ekibi | ✅ → **F-140** site sayfasında |
| Ö9 | Çok-instance iptal ve idempotency (§10.11) | — | ⏸ yatay ölçekleme gündemde değil; `SqlIdempotencyStore` zaten var |

**Önerilen üç kalem ve gerekçesi:** Küme K (tek ölçülmüş **kök sebep**; diğer
kalemler onun yaprağı) · F-34 yeniden yargı (aday dosyasının kendi cümlesi
ölçümle yanlışlandı) · Küme D (boşluk tablosunun birinci satırının motor tarafı).

**Kullanıcının elemesi (👤):** dört kümenin **tamamı** seçildi (K · F-34 · D · Ö).
R8 F-34'e katıldı; R2+R5 tek kalem oldu; R6 yalnız örnek olarak alındı.

---

## 3. 🚨 Yanlışlanan iddialar — kalem açılmadı

Bu turun en değerli çıktısı budur. Beş iddia ölçüldü ve **yanlış** çıktı.

| İddia | Ölçüm | Sonuç |
|---|---|---|
| **Y9** — `PiiPatterns` Kuzey Amerika biçimlerine göre kurulmuş; TR kimlik/IBAN yok | [`PiiPatterns.cs:20`](../../src/AgentPrism.Core/Guards/PiiPatterns.cs#L20) `Iban`, [`:38`](../../src/AgentPrism.Core/Guards/PiiPatterns.cs#L38) `TurkishNationalId` — ikincisi **kontrol hanesi doğruluyor**, rastgele 11 haneli değer maskelenmiyor | ❌ Öncül yanlış. Eksik olan yalnız TR telefon biçimi; tek başına kalem değil |
| **§7.7** — tool gövdesi oturumu/koşuyu göremez, `_sessionId` enjeksiyonunun karşılığı planlanmalı | [`AgentPrismRunContext.cs:35`](../../src/AgentPrism.Core/Recording/AgentPrismRunContext.cs#L35) **public**; `AgentRunScope` `RunId`·`RootRunId`·`Depth`·`AgentName`·`TenantId`·`SessionId`·`Budget`·`AgentVersion`·`ExperimentId`·`Variant` taşır. XML dokümanı: *"A tool cannot access `AgentSession`, so this is the only place it can read the session identity from"* | ❌ Yarısı **bugün çözülü**. Kalan yarı (çalışma anı parametresi) F-34'tür |
| **§4.4** — koşu ağacı maliyet toplamı API'de var mı, ölçmedim | [`RunRecord.cs:121,137`](../../src/AgentPrism.Abstractions/Runs/RunRecord.cs) `TreeUsage`/`TreeCost` · [`RunSupportTypes.cs:155`](../../src/AgentPrism.Abstractions/Runs/RunSupportTypes.cs#L155) `RunTreeCost` — `InputCost`·`OutputCost`·`CachedInputCost`·`Currency` ve fiyatı bilinmeyen koşu sayısı | ❌ Zaten var |
| **§4.2** — kota %80 eşik olayı yayınlanıyor mu, ölçmedim | `AgentPrismQuotaOptions.ThresholdPercents` (varsayılan `[80, 100]`) · `WebhookEvents.QuotaThreshold`. F-100 bunu **2026-08-18'de kapattı** | ❌ Zaten var |
| **§4.5** — CI için kütüphane içinden çağrılabilir doğrulayıcı gerekir; `AgentDefinitionCompiler` public mi? | [`AgentDefinitionCompiler.cs:35`](../../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs#L35) `public sealed class` | ❌ Zaten public. Kalan iş F-50 → [Faz 83](../arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md) |

### 3.1 Raporun kendi düşürdüğü altı iddia — hepsi doğrulandı

Rapor §2'de altı iddiayı kendisi düşürmüştü. Altısı da bizim tarafımızda
ölçüldü ve **kapalı kalır**:

| § | Ölçüm |
|---|---|
| 2.1 `IRunEventSink` | [`IRunEventSink.cs`](../../src/AgentPrism.Abstractions/Runs/IRunEventSink.cs) — sıcak yol, sink hatası koşuyu düşürmez, tek örnek tüm koşulara hizmet eder, `RunEvent.TenantId` ile kiracı ayrımı |
| 2.2 `IToolAuthorizationHandler` | [`ToolAuthorizationTypes.cs`](../../src/AgentPrism.Abstractions/Tools/ToolAuthorizationTypes.cs) + [`AllowAllToolAuthorizationHandler.cs`](../../src/AgentPrism.Core/Tools/AllowAllToolAuthorizationHandler.cs) |
| 2.3 Örnekleme ayarları | [`ModelBinding.cs:18,21,24,36`](../../src/AgentPrism.Abstractions/Agents/ModelBinding.cs) `Temperature`·`MaxOutputTokens`·`TopP`·`ReasoningEffort`, sürümlenen tanımın içinde. **Koşu isteği bunları geçersiz kılamaz** — `AgentRunRequest` böyle bir alan taşımıyor. Yedek model devreye girerse `RunRecord.ModelId` gerçek modeli yazar. Ayrı kolon gereksiz |
| 2.4 Toplu üretim | [`JobHandlerKeys.cs`](../../src/AgentPrism.Abstractions/Scheduling/JobHandlerKeys.cs) `AgentBatch` (Faz 137 öncesi `JobKind.AgentBatch`) · [`JobRecord.cs:33-39`](../../src/AgentPrism.Abstractions/Scheduling/JobRecord.cs) `TotalItems`/`DoneItems`/`FailedItems` |
| 2.5 Fatura üretimi | *Bilerek Önerilmeyenler*'de kalır; tüketici de itiraz etmiyor |
| 2.6 EF Core köprü paketi | L16 · L29. Paket **önerilmedi** |

---

## 4. Derinleşen kalemler (Aşama 3)

Kalemlerin gövdeleri [`ADAYLAR.md`](../ADAYLAR.md) içindedir. Burada yalnız
kanıt seviyesi ve eleyici sınır kontrolü durur.

### F-140 · Gömme ekseni

**Kanıt seviyesi:** **Ölçüldü.** Sevk edilen `AgentPrism.AgentMap.md` içinde
`IRunEventSink`·`IToolAuthorizationHandler`·`ITenantContext`·`IRunAttributionContext`·
`ITenantStore`·`AmbientTenantScope`·`AgentPrismRunContext`·`IAttachmentStore`
adlarının **hepsi 0 kez** geçiyor. `docs-site` elle yazılan sayfalarında
`ITenantContext`, `ITenantStore`, `AmbientTenantScope`, `AgentPrismRunContext`
ve `IAttachmentStore` **0** sayfada geçiyor. `samples/` **tek proje** taşıyor.
`AgentPrismDiagnosticsReport` on alan taşıyor, hiçbiri bağlı genişleme noktası
değil. `APG` tanı ailesinde (`APG0001`…`APG0402`) karşılığı yok.
**Mercek:** 1, 3, 6.
**Eleyici sınır:** K2 ⟶ konusuz · K3 ⟶ konusuz · yeni paket **yok** · AOT
etkilenmez · bundle etkilenmez · public API **büyümüyor** (tanı ve rapor alanı
hariç).
**Karşı görüş:** Doküman/örnek işi bir yetenek değildir ve boşluk "yok" değil
"dağınık"tır; ayrıca gömme örneği ikinci bir bakım yüküdür. Karşı gerekçenin
zayıf yanı ölçümdedir — tüketicinin kod agent'ı üretilen referansı okudu ve yine
bulamadı, çünkü **hangi soruyu soracağını bilmiyordu**.
**Sonuç:** F-140 olarak yazıldı (§G).

### F-34 · Talimatın girdi yüzeyi (yeniden yargı)

**Kanıt seviyesi:** **Ölçüldü.** [`AgentDefinition.cs:33,41`](../../src/AgentPrism.Abstractions/Agents/AgentDefinition.cs)
· [`AgentContracts.cs:212-282`](../../src/AgentPrism.AspNetCore/Contracts/AgentContracts.cs)
— parametre alanı yok. [Faz 82](../arsiv/fazlar/82-ICERIK-KORUMASI.md) belge/talimat ayrımını
**kapsamıyor** (at-rest şifreleme).
**Mercek:** 1, 5, 7 (dolaylı 3).
**Eleyici sınır:** 🚨 **K2 sınırdadır** — bu yüzden yalnız değer yerleştirme;
Scriban gibi bir motor alınmaz (hem K2 hem AOT). Public API **büyür** → Faz
7'den önce ucuz, sonra sürüm kararı.
**Karşı görüş:** Tüketici bu işi bugün kendi tarafında yapabiliyor; engellenen
şey parametreleme değil, parametreli agent'ın AgentPrism **tanımında** yaşaması.
Kalem "yapamıyor" değil, "sürümleme kazanımını alamıyor" kalemidir.
**Sonuç:** Yeni numara **açılmadı**; F-34'ün gövdesi yeniden yazıldı, sınıfı
düzeltildi, R8 kapsamına katıldı.

### F-141 · Kesilen işin devamı

**Kanıt seviyesi:** **Ölçüldü.** [`SqlRunStore.cs:263`](../../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs#L263)
öksüz koşuyu `Failed` kapatır ve kuyruğa koymaz ·
[`RunReconciliationService.cs:91`](../../src/AgentPrism.Core/Recording/RunReconciliationService.cs#L91)
yalnız kapatır · `src/AgentPrism.Workflows` içinde retry/backoff **0 eşleşme** ·
[`AgentPrismWorkflowOptions.cs:25,49,60`](../../src/AgentPrism.Workflows/AgentPrismWorkflowOptions.cs)
· [`RecordedToolPlayback.cs`](../../src/AgentPrism.Core/Replay/RecordedToolPlayback.cs)
**internal**, `(tool adı, argümanlar)` çiftiyle eşleştiriyor ·
`AgentSessionManager.SaveSessionAsync` **açık** çağrıdır (kesilen tur oturumu
tur öncesi hâlinde bırakır).
**Mercek:** 2, 3, 6.
**Eleyici sınır:** 🚨 **K3 kontrol edildi ve geçildi** — tasarım MAF'a kanca
takmaz; F-95'in ölçümü (MAF agent düzeyinde kanca vermiyor) **doğrudur ve bu
tasarımı kesmez**. K-315 ihlal edilmez ama teğet geçer: iki işlem ayrı
adlandırılmalıdır. Yeni paket yok; public API 1–3 tip büyür; migration var.
**Karşı görüş:** Ölçülmüş acı tek instance'lı bir kurulumun **deploy
kesintisidir** ve onu zarif kapanış çok daha ucuza kapatır. Ayrıca playback
eşleşmesi argümana dayanır: argümanları zamana bağlı bir tool defterde
eşleşmez ve **sessizce** yeniden çalışır.
**Sonuç:** F-141 olarak yazıldı (§A). F-95 kapsam-dışı listesinde **kalır** —
gerekçesi hâlâ geçerlidir, çünkü F-141 farklı bir mekanizmadır.

### F-142 · Görsel üretim tool'u

**Kanıt seviyesi:** **Ölçüldü.** `src/` içinde görsel üretimi **0 eşleşme**.
Emsal: [`SpeakTool.cs`](../../src/AgentPrism.Voice/Tools/SpeakTool.cs) ·
[`VoicePricing.cs:62`](../../src/AgentPrism.Voice/Internal/VoicePricing.cs#L62)
`VoicePriceOverride` · [`IAttachmentStore.cs:69`](../../src/AgentPrism.Abstractions/Attachments/IAttachmentStore.cs#L69)
`IAttachmentStorage`.
**Mercek:** 1, 3, 8.
**Eleyici sınır:** 🚨 **Paket ağırlığı ÖLÇÜLMEDİ** — ayrı paket mi, sağlayıcı
paketlerine ek mi, planlamadan önce sayılmalı (K-212 emsali). AOT ve bundle
etkilenmez.
**Karşı görüş:** Ölçüm bütünlüğü argümanı yalnız görsel **üreten** tüketici için
geçerli ve bugün ölçülmüş tek tüketici var. Yanlış fiyat, fiyat olmamasından
kötüdür.
**Sonuç:** F-142 olarak yazıldı (§D).

### F-143 · Tool çıktısı boyut sınırı

**Kanıt seviyesi:** **Ölçüldü.** [`AgentPrismToolRegistration.cs:63-84`](../../src/AgentPrism.Abstractions/Tools/AgentPrismToolRegistration.cs)
altı alan taşır ve çıktı boyutu yoktur.
**Mercek:** 8, 2, 5.
**Eleyici sınır:** Hepsi konusuz. Public API 2 alan büyür; migration yok.
**Karşı görüş:** Tool gövdesi çıktısını zaten kısıtlayabilir ve orada kısıtlamak
daha iyidir — tool kendi verisini bilir, kütüphane yalnız bayt sayar. Bayt bazlı
kırpma bir JSON çıktısını ortasından kesebilir.
**Sonuç:** F-143 olarak yazıldı (§C).

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

| F-NN | Başlık | Aday dosyasına yazıldı mı |
|---|---|---|
| **F-140** | Gömme ekseni: bağlanacak sözleşmeler sevk edilen yüzeyde görünmüyor | ✅ §G |
| **F-141** | Kesilen işin devamı — agent turu ve workflow düğümü için tek sözleşme | ✅ §A |
| **F-142** | Görsel üretim tool'u | ✅ §D |
| **F-143** | Tool çıktısı için boyut sınırı | ✅ §C |
| ~~F-34~~ | Talimatın girdi yüzeyi — **yeni numara açılmadı**, gövde yeniden yazıldı | ✅ §D |

### Kanal 2 — kusur

**Bu turda yeni kusur bulunmadı.** Açık kusurlar önceki turlardan devreder ve
kullanıcıya **söylendi**:

| Bulgu | Kanıt | Söylendi mi | `kusur-giderme` |
|---|---|---|---|
| F-137 — yol/ortam bağımlı E2E düşüşü | `UiTests.cs:599` · dört satırlık ölçüm matrisi | ✅ | ⏸ koşulmadı |
| F-138 — `RespondStreamingAsync` devam eden akışta `WorkflowOutput` üretmiyor | `WorkflowAgentEntryRespondTests.cs:55` **ve** `WorkflowHumanInTheLoopTests.cs:88` — iki bağımsız kanıt | ✅ | ⏸ koşulmadı |
| F-122 · F-130 · F-139 — E2E kırılganları | İzolasyonda geçiyor; ortak kök tam koşumun paralelliği | ✅ | ⏸ koşulmadı |

### Kanal 3 — yeniden açılması önerilen karar

**Yeniden açılması önerilen karar yok.** Kapatılmış bir kararı geçersizleştiren
ekosistem değişimi bulunmadı. Temas eden dört karar ve durumları:

| Karar | Temas | Sonuç |
|---|---|---|
| **K-315** yeniden oynatma oturumsuzdur | F-141 aynı oturumun turunu devam ettirir | **İhlal yok.** İki işlem ayrı adlandırılmalı; kalemin gövdesine yazıldı |
| **K-498** checkpoint'ten devam idempotent işleyici ister | F-141'in workflow yarısı | Yeni karar gerekmez; risk satırını besler |
| **L16** EF Core kullanılmadı | ABP köprü paketi isteği | Paket **önerilmedi**; tüketici de vazgeçti |
| *Bilerek Önerilmeyenler* — fatura üretimi · S3/Blob uygulaması | Fatura · sink tampon sarmalayıcısı | Fatura yeniden açılmadı. Tampon 👤 kararıyla **yalnız örnek** oldu; S3 emsali korundu |

---

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| TR/bölgesel PII ailesi (Y9) | Öncül **yanlış**: `Iban` ve `TurkishNationalId` zaten var, ikincisi kontrol hanesi doğruluyor. Kalan yalnız TR telefon biçimi | Hayır — telefon kalıbı bir gün eklenebilir, tek başına faz değil | Yalnız burada |
| Koşu ağacı maliyet toplamı (§4.4) | `RunRecord.TreeCost` var | Evet — konusuz | Yalnız burada |
| Kota eşik bildirimi (§4.2) | F-100 kapattı (2026-08-18) | Evet — konusuz | Yalnız burada |
| Kütüphane içi tanım doğrulayıcı (§4.5) | `AgentDefinitionCompiler` public; kalanı F-50 → Faz 83 | Evet — konusuz | Yalnız burada |
| Tool bağlamı enjeksiyonu (§7.7) | `AgentPrismRunContext` public ve `SessionId` taşıyor | Kısmen — kalan yarı F-34 | Yalnız burada |
| ABP / EF Core köprü **paketi** (§10.1) | L16 + "genişleme noktası varsa somut uygulama tüketicinin işidir" emsali. `Volo.Abp` bağımlılığı paket ailesine girmemeli | Evet | Yalnız burada; ihtiyacın örnek+doküman hâli **F-140**'tır |
| Agent tanımı `Deprecated` durumu (§4.1) | Gerçek ama küçük; ölçülmüş bir engel yok | Hayır — zamanlama | Yalnız burada |
| `Metadata` şema desteği (§4.3) | Tüketici de "bugün gerek yok" diyor | Hayır — zamanlama | Yalnız burada |
| Kiracı senkronizasyonu **kancası** (Ö5) | F-140 önce **belgeleyecek**; kanca ihtiyacı belgeden sonra ölçülür | Hayır — zamanlama | Yalnız burada |
| `IContentGuard` kayıt-modu (Ö7) | 🚨 **Kanıt seviyesi: İddia.** Guard tarafında bir mod alanı arandı, bulunamadı ama tam ölçüm yapılmadı | Hayır — ölçüm eksik | Yalnız burada |
| Çok-instance iptal ve idempotency (§10.11) | `SqlIdempotencyStore` var; yatay ölçekleme gündemde değil | Hayır — koşullu | Yalnız burada |

🚨 **Tüketicinin §12'deki sekiz sorusu kalem üretmedi.** Onlar tüketicinin kendi
kararlarıdır (agent tanımının tek kaynağı · `ChatSession` korunacak mı ·
SignalR/SSE · ConvAI · konsol kapsamı · şema/veritabanı · pgvector ·
`UseScheduling()`). Bizim kararımız değildir.

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap 👤 |
|---|---|
| Hangi kalemler aday dosyasına girsin? | **Dördü de**: Küme K · F-34 yeniden yargı · Küme D · Küme Ö |
| R8 (belge kanalı) ne olsun? | **F-34 ile aynı kalemde** |
| R2 (agent) ve R5 (workflow) tek kalem mi? | **Tek kalem — ortak sözleşme** |
| R6 (sink tampon) paket sınırına girsin mi? | **Yalnız örnek — paket büyümesin** |

Sorulmayan ama karara bağlanan iki nokta, gerekçesiyle: **devam koşusu varsayılan
kapalıdır** (K1, sıfır sürpriz — kural zaten cevabı veriyor) · **görsel üretimin
paket kararı planlama turunda ölçülür** (bir kullanıcı tercihi değil, bir
ölçümdür; K-212 emsali).

---

## 8. Bütçe bakımı (bu turda yapıldı)

Aday dosyası tur başında **81 615 B / 80 000 B** ile bütçeyi **aşmıştı**.
İçerik **silinmedi**; kapanmış kayıtlar
[`arsiv/PLANA-DONUSEN-ADAYLAR.md`](../arsiv/PLANA-DONUSEN-ADAYLAR.md)'ye taşındı:

| Taşınan | Kazanç |
|---|---|
| Dalga 6–12 faz eşleme tabloları | ~7,1 KB |
| On kapanmış kapsam-dışı kalemin gerekçesi (F-87 · F-100 · F-102 · F-121 · F-124 · F-125 · F-129 · F-131 · F-133 · F-136) | ~4,3 KB |
| *Yeniden Yargı (2026-08-06)* anlatısı | ~2,7 KB |
| *Dalga 13 önerisi* turu (dört küme de çözüldü) | ~2,7 KB |
| Beş kusur kaleminin ölçüm matrisi (kalemler **açık kalır**, özet dosyada durur) | ~4,3 KB |

Kapanış: **76 969 B / 80 000 B**, `dokuman-bakim.py` çıkış kodu **0**.

🚨 **Taşımanın kendi tuzağı yeniden yaşandı ve kapatıldı:** `docs/X.md`'den
`docs/arsiv/Y.md`'ye taşınan bloğun **içindeki** göreli linkler arşiv dizininden
çözülmez. Bu koşumda **45 bağlantı** kırıldı ve düzeltildi. Script bunu her
koşumda sayıyor (Faz 77'de iki kez yaşanmış) — taşıma yapan her tur
`python3 scripts/dokuman-bakim.py` çıktısındaki *Kırık bağlantı* satırını
okumalıdır.
