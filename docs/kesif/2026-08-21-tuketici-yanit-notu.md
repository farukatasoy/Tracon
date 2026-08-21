# AgentPrism → ProdigyEnabler · Rapor Yanıtı

> **Kimden:** AgentPrism geliştirme tarafı · **Tarih:** 2026-08-21
> **Neye yanıt:** [`2026-08-21-tuketici-raporu.md`](2026-08-21-tuketici-raporu.md)
> (Y1…Y9 · §4 · §5) ve [`2026-08-21-uygulanabilirlik-raporu.md`](2026-08-21-uygulanabilirlik-raporu.md)
> (§7 · §10 · §12)
> **Tur kaydı:** [`2026-08-21-tuketici-turu-2.md`](2026-08-21-tuketici-turu-2.md)

---

## 0. Kısa cevap

Rapor **işe yaradı** ve dört kalem doğurdu. Ama raporun 19 iddiasının **beşi
ölçümle yanlış çıktı** ve bunların ikisi bugün geçişinizi doğrudan
kolaylaştırıyor — §3'ü okumadan Faz C'yi planlamayın.

Sizin tarafınızdaki en önemli tek cümle şudur: **`AgentPrismRunContext` public
bir tiptir ve tool gövdenize `SessionId` verir.** §7.7'de "planlanmalıdır"
dediğiniz iş bugün çözülüdür.

Rapor **paketin XML dokümanı ve OpenAPI çıktısı** okunarak yazılmıştı. Biz her
iddiayı **kaynak kodda** yeniden ölçtük; aşağıdaki `dosya:satır` referansları
kendi kopyanızda doğrulanabilir.

---

## 1. Doğrulanan iddialar — boşluk gerçek

| Kalem | Ölçüm |
|---|---|
| **Y1** parametre yok | `AgentDefinition.cs:33,41` · `AgentContracts.cs:212-282` — koşu isteğinde parametre alanı yok |
| **Y2** kesilen tur kurtarılmıyor | `SqlRunStore.cs:263` öksüz koşuyu `Failed` kapatır ve **kuyruğa koymaz** · `RunReconciliationService.cs:91` yalnız kapatır |
| **Y3** görsel üretimi yok | `src/` içinde **0 eşleşme** |
| **Y4** gömme örneği yok | `samples/` **tek proje** taşıyor ve o yeşil alan kurulumudur |
| **Y5** düğüm retry yok | `src/AgentPrism.Workflows` içinde retry/backoff **0 eşleşme** |
| **Y6** sink örneği yok | Kayıt düz `GetServices<IRunEventSink>()`; tampon sarmalayıcısı yok |
| **Y7** çıktı boyutu yok | `AgentPrismToolRegistration.cs:63-84` altı alan taşır, boyut yok |
| **Y8** belge kanalı yok | Yapısal "veri ≠ talimat" ayrımı yok |

§2'de kendi düşürdüğünüz **altı iddianın altısı da doğrudur**; kapalı kalıyorlar.
§2.3'ü ayrıca ölçtük ve gerekçeniz doğrulandı: `ModelBinding` sıcaklık/top-p/
reasoning taşır, sürümlenen tanımın içindedir ve **koşu isteği bunları geçersiz
kılamaz**; yedek model devreye girerse `RunRecord.ModelId` gerçek modeli yazar.

---

## 2. Yanlış çıkan iddialar — kalem açılmadı

Beş iddia ölçüldü ve düştü. Üçü bugün elinizde olan yeteneklerdir.

| İddia | Gerçek |
|---|---|
| **Y9** — `PiiPatterns` Kuzey Amerika biçimlerine göre kurulmuş | ❌ `PiiPatterns.cs:20` **`Iban`** · `:38` **`TurkishNationalId`** — ikincisi **kontrol hanesi doğruluyor**, rastgele 11 haneli değer maskelenmiyor. Eksik olan yalnız TR **telefon** biçimi |
| **§7.7** — tool gövdesi oturumu göremez | ❌ `AgentPrismRunContext.cs:35` **public**. `AgentRunScope` şunları taşır: `RunId`, `RootRunId`, `Depth`, `AgentName`, `TenantId`, **`SessionId`**, `Budget`, `AgentVersion`, `ExperimentId`, `Variant` |
| **§4.4** — koşu ağacı maliyet toplamı (ölçmediniz) | ❌ `RunRecord.TreeCost` · `RunTreeCost` — `InputCost`, `OutputCost`, **`CachedInputCost`** (üçüncü toplanan, alt küme değil), `Currency` ve fiyatı bilinmeyen koşu sayısı |
| **§4.2** — kota eşik olayı (ölçmediniz) | ❌ `AgentPrismQuotaOptions.ThresholdPercents` varsayılan `[80, 100]` · `WebhookEvents.QuotaThreshold` webhook tetikliyor |
| **§4.5** — CI için doğrulayıcı | ❌ `AgentDefinitionCompiler` **public**. Kalan iş tipli istemci/CLI'dır ve planlandı (Faz 83) |

### 2.1 Bunlar geçiş planınızı nasıl değiştirir

1. **Faz C adım 10'u sadeleştirin.** `ChatToolCallCoordinator.EnrichWithSessionContext`'in
   `_sessionId` yarısı için yeni bir mekanizma planlamayın:
   `AgentPrismRunContext.Current?.SessionId` okuyun. Kalan yarı
   (`DynamicParametersJson`) F-34'tür ve o hâlâ açıktır.
   🚨 **Ek üreten tool'lar için bu zorunludur:** `attachments` satırına yazılan
   içerik `session_id` **taşımalıdır**; boş `session_id` taşıyan ek, saklama
   politikası tarafından **öksüz** sayılır ve kesim tarihinden sonra silinir —
   oturum hâlâ yaşarken transcript'teki içerik kaybolur.
2. **GUARD için TR kimlik ve IBAN yazmayın.** `PiiPatterns.TurkishNationalId` ve
   `Iban` ailelerini açın. Aileler **bağımsız** açılır; hepsini birden açmak
   yanlış pozitifi katlar.
3. **Faz B ölçütünü basitleştirin.** "Bir makalenin üretim maliyeti" için
   ağaç toplamını kendiniz hesaplamayın; `TreeCost` alanını okuyun.
4. **Bütçe eşiği köprüsü zaten var.** §4.2 için `quota.threshold` webhook'unu
   `Notifications` modülünüze bağlayın.

---

## 3. Açılan kalemler

| Kalem | Neyi karşılar | Not |
|---|---|---|
| **F-140** Gömme ekseni | Y4 · Y6 · §10.1 · §10.5 | Beş genişleme noktası + tool bağlamı tek eksende: yetenek haritası satırı, site sayfası, **çerçeve-nötr gömme örneği** (arka plan işi senaryosu **zorunlu**), bağlanmamış nokta için derleme tanısı, `GET /api/diagnostics` satırı. `IRunEventSink` köprü örneği (sınırlı kanal + dolulukta **düşürme**) buraya girdi |
| **F-34** Talimatın girdi yüzeyi | Y1 + Y8 | Kalem zaten vardı ama **"ergonomi"** sınıfındaydı ve *"hiçbiri bugün bir tüketiciyi engellemiyor"* satırında duruyordu. Raporunuz o satırı düşürdü. Parametre şeması ile belge kanalı **tek kalemde** birleşti (§5 S1'e cevap) |
| **F-141** Kesilen işin devamı | Y2 + Y5 | Agent turu ile workflow düğümü **tek sözleşme** paylaşır (§5 S2'ye cevap) |
| **F-142** Görsel üretim tool'u | Y3 | Paket kararı planlama turunda **ölçülecek** (§5 S4'e cevap) |
| **F-143** Tool çıktısı boyut sınırı | Y7 | `Timeout` alanının kardeşi |

Kalemler `docs/ADAYLAR.md` içindedir. **Faz numarası taşımıyorlar** ve sıraları
henüz belli değildir — bir kalemin aday olması, yakında geleceği anlamına gelmez.

---

## 4. §5'teki beş sorunuzun cevabı

| # | Soru | Cevap |
|---|---|---|
| 1 | Y1 ve Y8 tek fazda mı? | **Evet, tek kalem.** İkisi de "talimata ne girer" sorusudur; ayrı planlanırsa ikincisi birincinin kararını bozar |
| 2 | Y2 ve Y5 tek fazda mı? | **Evet, tek kalem.** Ortak sözleşme: devam kaydı, deneme sayısı, yan etki kısıtı |
| 3 | Devam koşusu varsayılan kapalı mı? | **Kapalı.** "Sıfır sürpriz" kuralı bir tercih değil, bir tasarım kuralıdır; `AddAgentPrism()` hiçbir davranışı kendiliğinden açmaz. Özelliğin görünürlüğü doküman işidir, varsayılan işi değil |
| 4 | Görsel üretimi ayrı paket mi? | **Ölçülmedi ve tahmin edilmeyecek.** Geçişli bağımlılık ağırlığı planlama turunda sayılacak; Azure AI Foundry 37 pakette bu yüzden düşmüştü |
| 5 | Tampon sarmalayıcısı paket sınırına girer mi? | **Hayır — yalnız örnek.** S3/Azure Blob emsali korunuyor: genişleme noktası varsa somut uygulama tüketicinindir. Örnek deseni verir: sınırlı kanal, arka plan tüketici, dolulukta **düşürme** (bloklama değil) |

---

## 5. Açılmayan kalemler ve gerekçeleri

| Kalem | Neden hayır |
|---|---|
| **Y9** TR/bölgesel PII | Öncül yanlış (§2). TR telefon biçimi tek başına faz değildir |
| **§4.1** agent `Deprecated` durumu | Gerçek bir boşluk, ama ölçülmüş bir engel yok. Kayda geçti |
| **§4.3** `Metadata` şema desteği | Siz de "bugün gerek yok" diyorsunuz. Kayda geçti |
| **§10.1** ABP/EF Core köprü **paketi** | `Volo.Abp` bağımlılığı paket ailesine girmez. İhtiyacın karşılığı **F-140**'tır: örnek + doküman |
| **§10.9** fatura üretimi | İkimiz de aynı fikirdeyiz. Kapalı kalıyor |
| **§10.11** çok-instance iptal/idempotency | `SqlIdempotencyStore` var; yatay ölçekleme gündemde değil |
| Kiracı senkronizasyonu **kancası** | Önce F-140 bunu **belgeleyecek**. Kanca ihtiyacı belgeden sonra ölçülür — bugün elle bir `ITenantStore` yazımı yeterlidir |

---

## 6. §12'deki sekiz soru

Bunlar **sizin kararlarınızdır**, bizim değil: agent tanımının tek kaynağı ·
`ChatSession`/`ChatMessage` · SignalR mi SSE mi · ConvAI · AI Ops Console'un
kapsamı · aynı DB ayrı şema · pgvector · `UseScheduling()`. Kalem üretmediler.

İkisine yalnız **paket tarafındaki gerçeği** ekliyoruz, karar yine sizin:

- **§12.6 (aynı DB, ayrı şema):** doğru okuma. Tüketicinin `public` şeması
  hiçbir koşulda ellenmez; bağlantı havuzu gerçekten ayrıdır ve `MaxPoolSize`
  gözden geçirilmelidir.
- **§12.8 (`UseScheduling()`):** üç tetikleyiciniz doğru. Worker kapalıyken
  store'lar çalışmaya devam eder; yalnız lease alınmaz.

---

## 7. Bir sonraki tur için ricamız

Bu turun en değerli çıktısı yeni kalemler değil, **beş yanlış iddiaydı**. Sebebi
şuydu: rapor XML dokümanı ve OpenAPI çıktısı okunarak yazıldı, kaynak kod
okunarak değil.

🚨 **Bunun kusuru sizde değil, bizde.** `AgentPrismRunContext`, `ITenantContext`,
`AmbientTenantScope` ve `IAttachmentStore` sevk ettiğimiz yetenek haritasında
**hiç geçmiyor**; `ITenantContext` hiçbir anlatı sayfamızda yok. Kod agent'ınız
6.100 üye okudu ve yine bulamadı, çünkü **hangi soruyu soracağını bilmiyordu**.
F-140 tam olarak budur ve bu turun birinci kalemidir.

Bir sonraki tur için iki ricamız var:

1. **Canlı bir koşum yapın.** İki tur da statik okumayla yazıldı. Tek bir gerçek
   `run` — Faz A'nın Roleplay pilotu — bir sonraki raporun kanıt gücünü
   iki katına çıkarır.
2. **Yanlış çıkan iddiaları da yazın.** Bu raporun §2'si (kendi düşürdüğünüz
   altı iddia) turun en kullanışlı bölümüydü. Aynısını sürdürün.
