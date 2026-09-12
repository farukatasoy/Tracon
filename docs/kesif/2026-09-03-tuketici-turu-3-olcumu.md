# Keşif Turu — 2026-09-03 · ProdigyEnabler tüketici turu 3

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni [`ADAYLAR.md`](../ADAYLAR.md)
> içinde yaşar; bu dosya yalnız oraya işaret eder.

**Tetikleyen:** Kullanıcı, ProdigyEnabler'ın 2026-09-03 tarihli feature talep
belgesini (Bölüm A: A1–A9, Bölüm B: B1–B10) koda karşı ölçmeyi ve faz adaylarını
raporlamayı istedi.
**Zemin:** Faz 138 kapalı · sıralanabilir aday kuyruğu boş · en büyük numara F-184
**İncelenen sürüm:** `1.0.0-preview.1` — turu 2'nin (Faz 136 · 137 · 138) çıktısı
**Ekosistem taraması:** yapılmadı — kalemlerin hiçbiri "MAF/ekosistem bunu artık
veriyor" iddiası taşımıyor; hepsi Tracon'in kendi yüzeyine dair. Ekosistem
karşılaştırması gereken tek kalem B1'dir ve orada işaretlendi.

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| `docs/ADAYLAR.md` § Sıralama | Kuyruk **boş**; plana dönmeyi bekleyen sıralanabilir aday yok. Bu tur kuyruğu yeniden dolduruyor |
| § Bekleyen Kalemler | F-95 · F-165 · F-178 · F-179 bekliyor. **F-179'un ön koşullarından biri kapandı** — `RunRecord.ModelProvider` artık var (`RunRecord.cs:73`) |
| § Bilerek Önerilmeyenler | F-91 (`secret` saklama) ve F-92 (dağıtık hız sınırı) kapalı. Bu turun hiçbir kalemi onlara dokunmuyor |
| `arsiv/KARARLAR-INDEKS-REDDEDILEN.md` | A1–A9 ve B1–B10 konularında **eşleşme yok**. Bu turda daha önce reddedilmiş bir iş yeniden önerilmiyor |
| K-647 | `RunEventType` üyesinin payload iddiası, payload'ı OKUYAN bir testle eşleşmek zorunda. A2 ve A5 bu kapıya girer |
| Turu 2 yanıtı (`2026-09-03-tuketici-gap-yaniti.md`) | Aynı tüketici, aynı gün. AP-REQ-001/002/003 kapandı; bu belge **onların üstüne** yazılmış yeni bir tur |

---

## 2. Raporun güvenilirliği

Tüketici bu turda da kaynak gösterdi ve iddialarını tek tek doğruladı. Ölçüm
sonucu:

| Ölçülen iddia | Sonuç |
|---|---|
| `AgentRunRequest` alanları · `sessionId` gövdeden gelir | ✅ Doğru (`AgentEndpoints.cs:825`) |
| 163 operasyon / 126 path | ✅ **Birebir** doğru (`docs/openapi/tracon.json` sayımı) |
| `RunEventType` kapalı enum, 29 değer | ✅ Birebir doğru (`RunEventType.cs`, 0–28) |
| `RunEventDraft` alanları (5 tane) | ✅ Doğru (`RunEventWriter.cs:356`) |
| `ContentGuardContext` alanları (6 tane) | ✅ Doğru (`ContentGuardContext.cs:25`) |
| `ToolApprovalContext` alanları | ✅ Doğru (`ToolApprovalContext.cs:14`) |
| `AgentRunScope`'ta `UserId` yok | ✅ Doğru (`TraconRunContext.cs:52`) |
| `ModelBinding`'de endpoint/credential yok | ✅ Doğru (`ModelBinding.cs`) |
| `TraconRunRecordingOptions`'ta birleştirme yok | ✅ Doğru (`TraconOptions.cs:538-594`) |
| `Operator` rolü run + onay + session silme veriyor | ✅ Doğru (üç uç da `roles.Operator`) |
| **Kota eşik bildirimi yok** | ❌ **YANLIŞ** — mekanizma var (aşağıda A2) |
| **`IRunEventSink` dolan bir kanalda olay düşürür** | ❌ **YANLIŞ** — Tracon'in kanalı yok (aşağıda A9) |

On iki iddianın onu doğru. İki yanlış iddianın **ikisi de bizim dokümanımızın
ürettiği** bir yanlış anlamadır; tüketicinin okuma hatası değildir. İkisi de
Kanal 2'ye (kusur) gitti.

---

## 3. Derinleşen kalemler (Aşama 3)

### A1 · Kullanıcı düzeyinde run ve session yetkilendirmesi

**Kanıt seviyesi:** Ölçüldü.
`SessionEndpoints.cs:22,52,65,79` — dört ucun tamamı yalnız rol (`Reader`/`Operator`)
ve `RequireApiKeyScope` taşıyor. `AgentSessionManager.cs:365` sorguyu yalnız
`_tenantContext.TenantId` ile daraltıyor. `AgentEndpoints.cs:169-270` run yolunda
dört kapı var (`DrainGate`, `RunAttributionGate`, `QuotaGate`, `PreflightGate`) —
**hiçbiri çağıran kimliğini session sahipliğiyle karşılaştırmıyor.**
`TraconDiagnosticsCollector.cs:221-245` sevk edilen beş genişleme noktasını
sayıyor: `ITenantContext`, `IRunAttributionContext`, `IToolAuthorizationHandler`,
`IRunEventSink`, `IAttachmentStorage`. **Run başlatmayı veya session okumayı
yetkilendiren bir nokta yok.** Tüketicinin "denedik, yetmiyor" tablosunun altı
satırı da doğru.

**Mercek:** 2, 3, 5, 6.
**Eleyici sınır:** K2 ✅ (kod, arayüz değil) · K3 ✅ (MAF tipi yok) · yeni paket yok ·
AOT ✅ (`reflection` yok) · **public API büyür** — yeni bir `Abstractions` kontratı,
bir sürüm kararıdır.
**Karşı görüş:** Ciddi bir karşı gerekçe bulunamadı. `IToolAuthorizationHandler`
zaten aynı şekle ve aynı fail-closed davranışına sahip; bu kalem o desenin
kardeşidir. Tek gerçek soru **kapsam**: `Access` enum'u tüm session
işlemlerini mi kapsar, yalnız run başlatmayı mı.
**Sonuç:** En güçlü kalem. Aday önerisi.

### A4 · İçerik guard'ında kaynak ve tool adı

**Kanıt seviyesi:** Ölçüldü.
`ContentGuardMessageMasker.cs:74` `message.Role`'ü **zaten okuyor**;
`:167` ve `:202-210` `FunctionResultContent`'i `TextContent`'ten **zaten
ayırıyor**. Yani tüketicinin istediği ayrım çağrı yerinde mevcut ve
`ContentGuardContext` kurulmadan hemen önce **atılıyor**. Bu, yeni bilgi üretmek
değil, var olanı geçirmektir — tüketicinin gerekçesi doğru.

**Ölçümün bulduğu ek ayrıntı (raporda yok):** `FunctionResultContent` tool
**adını** taşımaz, yalnız `CallId` taşır. `ToolName`'i doldurmak için mesaj
listesinde `CallId → FunctionCallContent.Name` eşlemesi kuran ikinci bir geçiş
gerekir. `Source` bedavadır; `ToolName` değildir. Kapsam bu farkı yazmalıdır.

**Mercek:** 3, 5.
**Eleyici sınır:** K2 ✅ · K3 ✅ · paket yok · AOT ✅ · public API **additive**
(`init` alan + yeni enum, `Unknown = 0` geriye uyumlu).
**Karşı görüş:** Guard'ın kendisi zaten en sıkı kuralı uygulayabilir; kaynak ayrımı
bir **kolaylık** olarak görülebilir. Ancak tüketicinin gerekçesi bunu aşıyor: tek
desen seti yanlış pozitif ile güvenlik kaybı arasında seçim dayatıyor ve bu seçim
kütüphanenin değil tüketicinin olmalı.
**Sonuç:** Ucuz ve doğru yerde. Aday önerisi.

### A5 · Genişletilebilir run olayı türü (`Custom`)

**Kanıt seviyesi:** Ölçüldü.
`RunEventWriter.cs:139` `AppendAsync` public; `TraconRunContext.cs` `Writer`
public. Yazma yolu **açık**, taşınacak tür **yok** (`RunEventDraft.Type` kapalı
enum). Tüketicinin tespiti tam.
`src/Tracon.UI/frontend/src/lib/run-event.ts:8` — arayüzdeki `RunEventType`
listesi **elle** bakılıyor ("by hand" yorumu dosyada yazılı); yeni bir değer
oraya da girer. `runs_v1` görünümü run seviyesindedir
(`SqlServer/MigrationsViews/0001_read_views.sql:23`), olay türünden
**etkilenmez** — tüketicinin bu iddiası da doğru.

**Mercek:** 5, 6.
**Eleyici sınır:** K2 🚨 **bakılmalı.** Serbest `CustomType` string'i arayüzden
**kod** tanımlatmaz, yalnız etiket taşır — K2'yi ihlal etmez. K3 ✅ · AOT ✅ ·
public API büyür · **K-647 kapısı**: `Custom`'ın payload'ı hakkında bir iddia
yazılırsa onu okuyan bir test gerekir.
**Karşı görüş:** Gerçek bir karşı gerekçe var. Kapalı enum bir **sözleşmedir** ve
`Custom` onu delik açmadan genişletmez: `Custom` gören her tüketici artık
tanımadığı bir tür kümesiyle karşılaşır. Tüketici bunu kabul ediyor ("jenerik bir
kart yeter") ama bu, kütüphane için tek yönlü bir kapıdır — GA'dan sonra geri
alınamaz.
**Sonuç:** Aday önerisi, ama A1/A4'ten **sonra**. Karar değeri yüksek.

### A3 · Onay isteğine görüntülenebilir varlık kimliği

**Kanıt seviyesi:** Ölçüldü.
`PendingApproval.cs:22-66` — `ToolName` ve ham `Arguments` var, sunum alanı yok.
`ToolApprovalContext.cs:14` aynı. Tüketicinin "elimizde yalnız bir Guid var"
tespiti doğru.

**Mercek:** 1, 3.
**Eleyici sınır:** K2 ✅ (çözümleyici koddadır) · K3 ✅ · AOT ✅ · public API büyür.
🚨 **Scoped bağımlılık sorunu:** presenter veritabanına gidecek, yani scoped
bağımlılık ister. `MEMORY.md`'deki tuzak burada geçerlidir — tool'un gördüğü
servis sağlayıcı **boştur** (K-218). Kayıt biçimi (`AddScopedTool` benzeri) fazın
**açık sorusudur**, plan bunu varsayamaz.
**Karşı görüş:** Sunum, kütüphanenin değil tüketicinin işidir — front-end Guid'i
zaten çözebilir. Karşı-karşı görüş: aynı ekran **bizim console'umuzda da** var ve
o da bugün ham argüman gösteriyor; yani kalem yalnız tüketiciye değil ürüne de
yarıyor.
**Sonuç:** Aday önerisi, orta öncelik.

### A6 · Akış delta'larının sunucuda birleştirilmesi

**Kanıt seviyesi:** Ölçüldü. `grep -rn "Coalesc" src/` → run yolunda **sıfır**
eşleşme (yalnız SQL `COALESCE` kullanımları). `TraconOptions.cs:538-594`
ailesi (`RecordMessageDeltas`, `RecordReasoningDeltas`, `MaxPayloadLength`) akışın
şeklini zaten yönetiyor; tüketicinin "aynı ailenin üyesi" gerekçesi yerinde.

**Mercek:** 1, 4.
**Eleyici sınır:** K2 ✅ · K3 ✅ · AOT 🚨 `BoundaryPattern` bir **regex**'tir;
`Core` AOT uyumlu kalmalı, yani `RegexOptions.Compiled` değil kaynak üretici
veya yorumlanan regex gerekir. Plan bunu ölçmelidir.
**Karşı görüş:** Gerçek ve güçlü. Tüketici kendisi "çözebiliyoruz, bu yüzden P1"
diyor. Sunucuda birleştirme **gecikme ekler** (`MaxDelay` kadar) ve bu, akışın
algılanan hızını doğrudan düşürür — kütüphane bu ödünü tüketici adına vermemeli.
Varsayılan kapalı olduğu sürece sorun yok, ama kalemin değeri "her tüketici aynı
tamponu yazıyor" gözleminden ibaret.
**Sonuç:** Aday, ama **düşük** öncelik.

### A8 · Run scope'unda çağıran kimliği

**Kanıt seviyesi:** Ölçüldü. `RunRecord.cs:47,57` — `UserId` ve `Labels` zaten
toplanıyor ve `runs` satırına **yazılıyor**. `TraconRunContext.cs:52`
`AgentRunScope` onları taşımıyor. Yani değer üretiliyor, bir adım ötede
düşürülüyor. Tüketicinin "yeni kavram getirmiyor" gerekçesi doğru.

**Mercek:** 5.
**Eleyici sınır:** hepsi ✅; public API'ye iki `init` alan ekler.
**Karşı görüş:** Ciddi bir karşı gerekçe bulunamadı. Kalem çok küçük — **tek
başına bir faz değildir.** A1 ile aynı faza girmelidir: A1'in
`RunAuthorizationRequest.UserId` alanı zaten aynı değeri gerektiriyor.
**Sonuç:** Bağımsız aday **değil** — A1'in kapsamına katılır.

### A7 · Model bağlaması başına endpoint ve credential

**Kanıt seviyesi:** Ölçüldü. `ModelBinding.cs` on bir üye taşıyor, endpoint yok.
`TenantProviderBinding.cs:68,71` `ApiKeyConfigurationName` **ve** `Endpoint`
taşıyor. Tüketicinin bulduğu asimetri **gerçek**: aynı fikir kiracı düzeyinde var,
bağlama düzeyinde yok.

**Mercek:** 3, 5.
**Eleyici sınır:** K2 ✅ (K-059 korunuyor — değer değil ad saklanıyor) · K3 ✅ ·
AOT ✅ · public API büyür. 🚨 **Egress guard'ı** yeni adrese de uygulanmalı;
plan bunu DoD'ye almalıdır.
**Karşı görüş:** Tüketici kendisi "engelleyici değil, tek endpoint kullanıyoruz"
diyor. Kalemi ayakta tutan tek şey asimetri argümanı — ve asimetri tek başına bir
talep kanıtı değildir. **Gerçek bir kullanıcı acısı ölçülmedi.**
**Sonuç:** Aday, ama en düşük öncelik. Talep kanıtı zayıf.

### B1 · Tek run'da tool çağrısı sayısı sınırı

**Kanıt seviyesi:** Ölçüldü. `HarnessSettings.cs:21` `MaximumIterationsPerRequest`
**tek** sayı tavanıdır ve harness'a bağlıdır. `TraconOptions.cs:147-192`
ailesi token (`MaxTotalTokens`), çocuk run (`MaxTotalRuns`), derinlik
(`MaxDepth`), maliyet (`MaxTotalCost`) ve süre (`MaxDuration`) sayıyor —
**tool çağrısı sayısı yok.** Tüketicinin boşluk tespiti doğru.

**Mercek:** 2, 8.
**Karşı görüş:** `MaxDuration` (Faz 114) ucuz-tool döngüsünü **zaten** kesiyor;
tüketicinin "dakikalarca bekletir" senaryosu bugün süre tavanına takılır. Kalemin
değeri bu yüzden düşünüldüğünden dar.
**Sonuç:** Aday, düşük öncelik. Ekosistem karşılaştırması yapılmadı.

### B8 · İş nesnesine maliyet atfı (`SubjectRef`)

**Kanıt seviyesi:** Ölçüldü. `RunLabels.cs:22` `MaxCount = 8`; `RunRecord.cs:57`
`Labels` serbest sözlük. Tüketicinin "etiketlerin toplamı `totalRuns`'a eşit
değil" gözlemi bölümleme (partition) ile etiketleme arasındaki farkı doğru
kuruyor. Faturalandırma için tekil bir boyut gerçekten eksik.

**Mercek:** 3, 7, 8.
**Karşı görüş:** Gerçek. `Labels` **zaten** bu işi yapabilir — tüketici tek bir
etiket anahtarını (`subject`) bölümleme olarak kullanmayı seçebilir. Kalem yeni
bir yetenek değil, var olanın **tiplenmiş** hâlidir; kalıcı şema ve migration
maliyeti buna değer mi, ölçülmedi.
**Sonuç:** Aday, ama önce "Labels neden yetmiyor" sorusu cevaplanmalı.

### B2 · B10 · Değerlendirilen ama önerilmeyen Bölüm B kalemleri

| Kalem | Ölçüm | Neden bu turda aday değil |
|---|---|---|
| **B2** tool sonucu önbelleği | `TraconToolAttribute.cs` — `CacheSeconds` yok; `ModelBinding.ResponseCache` var (`:125`) | Fikir sağlam ve `Effect` doğrulaması hazır. Ama çok kiracılı önbellek **kalıcı bir güvenlik yüzeyidir**; yanlış anahtar bir kiracıya başkasının sonucunu verir. 1.0 öncesi alınacak en riskli kalem |
| **B3** gölge run | `Variant`, `compare` ucu (`RunEndpoints.cs:241`), `IRunJudge` var | Gerçek para harcayan bir mod. Bütçe/kota muhasebesi ayrı raporlanmalı — kapsam göründüğünden büyük |
| **B4** Git `IAgentSource` | Sevk edilen tek implementasyon `CodeAgentSource` (`Catalog/CodeAgentSource.cs:12`) | Boşluk gerçek. Ama yeni bir paket + Git bağımlılığı; K-212 deseni. Ayrı bir tur ister |
| **B5** redaksiyon profili | `IContentProtector` var (`Security/IContentProtector.cs:7`) | Tüketici maliyeti kendisi yazmış: redakte edilmiş run **replay edilemez**. Bu, Faz 112'nin replay sözleşmesiyle çelişir |
| **B6** transkript dışa aktarımı | Yalnız `/api/data-subjects/{id}/export` var (`DataSubjectEndpoints.cs:31`); session export **yok** | Boşluk gerçek ve kalem ucuz. Sıralanabilir — ama bu turun A kalemlerinden zayıf |
| **B7** tool şeması sözleşme testleri | `Contracts/Tools/` yalnız `CustomToolContract` + `RepeatableToolContract` taşıyor | Boşluk gerçek: argüman güvenliği sözleşmesi yok. Faz 98'in deseni hazır. **Bölüm B'nin en olgun kalemi** |
| **B9** harcama projeksiyonu | `forecast` → **sıfır** eşleşme | Kota eşiği zaten var (A2). Projeksiyon onun üstüne biner; önce A2'nin kanal sorunu çözülmeli |
| **B10** prompt bisector | `diff` ucu sunucuda diff hesaplamıyor (`RunEndpoints.cs:248`) | n blok = n koşum. Maliyeti en yüksek, talep kanıtı en zayıf kalem |

---

## 4. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

Numaralar **kullanıcı onayından sonra** verilir; en büyük mevcut numara F-184'tür.

| Sıra | Kalem | Kaynak | Not |
|---|---|---|---|
| 1 | Run ve session yetkilendirme seam'i (+ scope'ta `UserId`) | A1 + A8 | En güçlü kalem; A8 buraya katıldı |
| 2 | İçerik guard'ında kaynak ve tool adı | A4 | Ucuz, bilgi çağrı yerinde mevcut |
| 3 | Genişletilebilir run olayı türü | A5 | Tek yönlü kapı; karar değeri yüksek |
| 4 | Onay isteğine sunum çözümleyicisi | A3 | Scoped kayıt biçimi açık soru |
| 5 | Tool argümanı sözleşme testleri | B7 | Bölüm B'nin en olgun kalemi |
| 6 | Session transkript dışa aktarımı | B6 | Ucuz, dar |
| 7 | Akış delta'larının sunucuda birleştirilmesi | A6 | Tüketici "çözebiliyoruz" diyor |
| 8 | Bağlama başına endpoint ve credential | A7 | Talep kanıtı zayıf |

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi | `kusur-giderme` |
|---|---|---|---|
| **`IRunEventSink` doküman cümlesi yanlış okunuyor** — `docs-site/src/content/docs/guides/embedding.md:109` "A channel that reaches capacity **drops** the event" cümlesi **tüketicinin kendi kanalını** tarif ediyor, ama Tracon'in davranışı gibi okunuyor. Gerçek: Tracon'in kanalı **yok**; `RunEventWriter.cs:203` sink'i doğrudan `await` ediyor. Tüketici bu cümleye dayanarak var olmayan bir kanal için metrik istedi (A9) | `RunEventWriter.cs:189-215` · `embedding.md:106-110` | ✅ | Bekliyor |
| **Kota eşik bildirimi keşfedilemiyor** — mekanizma **var** (`TraconQuotaOptions.cs:41` `ThresholdPercents = [80,100]`, `QuotaEnforcer.cs:296` bir kez/periyot, `quota.threshold` webhook'u) ama yalnız **üretilen API referansında** görünüyor. `llms-full.txt`'te `ThresholdPercents` **bir kez** geçiyor; anlatı sayfası `concepts/governance.md` § "Quotas and rate limits" eşikten hiç söz etmiyor. Belgeyi baştan sona okuyan tüketici özelliği bulamadı ve **var olanı yeniden önerdi** (A2) | `TraconQuotaOptions.cs:41` · `QuotaEnforcer.cs:245-315` · `governance.md:238-258` | ✅ | Bekliyor |

Her iki kusur da **doküman kusurudur**, kod kusuru değil.

### Kapanış (2026-09-03, `kusur-giderme`)

**Sınıf taraması — kusur 1.** Sink'i anlatan dört yüzey tarandı
(`capabilities.md` · `guides/observability.md` · `concepts/runs.md` ·
`guides/embedding.md`). Yanıltıcı cümle **yalnız `embedding.md`'de**. Ama tarama
**ikinci bir vaka** buldu: `concepts/runs.md`'nin sink örneği kendi kuralını
çiğniyordu — `await queue.PublishAsync(...)`, "do not block on further I/O"
cümlesinin iki satır üstünde. Bir okuyucu örneği kopyalarsa dokümanın uyardığı
yavaş sink'i kurar. İkisi de düzeltildi; örnek artık sınırlı bir `Channel`'a
yazıp dönüyor.

**Sınıf taraması — kusur 2.** `WebhookEvents`'in **on** sabiti anlatıya karşı
tarandı: yalnız `quota.threshold` hiçbir anlatı sayfasında geçmiyordu.
Ölçüm sırasında ilk iddiam **daraldı** ve düzeltildi: `Quotas:ThresholdPercents`
aslında `reference/configuration.md:381`'de var — ama çıplak bir varsayılan
satırı olarak, davranışı anlatmadan. § Webhooks ise **hangi olayların var
olduğunu hiç listelemiyordu**. Asıl boşluk buydu.

**Kapı.** `scripts/dokuman-bakim.py` → `sevk_edilen_olay_anlatisi()`: her
`WebhookEvents` sabiti en az bir **anlatı** sayfasında geçmelidir; `/api/` ve
`schema-*` üretilen sayfaları anlatı sayılmaz. **Kırmızı→yeşil kanıtlandı** —
düzeltme geri alındığında kapı tam olarak `quota.threshold`'u bildirdi. Dört
birim testi eklendi (`SevkEdilenOlayAnlatisiTestleri`).

🚨 **Kusur 1'in kapısı yok.** Öznesiz cümle mekanik olarak denetlenemez; yalnız
`docs/hafiza/dokumantasyon.md` notu koruyor. İlk vakadır. Sınıf **ikinci** kez
görülürse yazı yetmemiş demektir ve kapı zorunlu olur.

**Ölçülen kapanış:** `quota.threshold` anlatıda 0 → 2 · `ThresholdPercents`
`llms-full.txt`'te 1 → 3 · `governance.md`'de "threshold" 0 → 6 · yanıltıcı
cümle 1 → 0. Dört site kapısı (`content` · `build` · `links` · `weight`) ve
`dokuman-bakim.py` yeşil; 125 script testi geçti.

### Kanal 3 — yeniden açılması önerilen karar

Bu turda yok. Hiçbir kalem kapatılmış bir kararı geçersizleştirmiyor.

---

## 5. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye |
|---|---|---|---|
| **A2** `QuotaDefinition.WarnAtPercent` + `RunEventType.QuotaThresholdCrossed` | Eşik mekanizması **zaten var**; öneri onu çoğaltır. Geriye kalan gerçek boşluk yalnız **kanaldır** (webhook ↔ SSE) ve bu, A5'in (`Custom` olay) üstüne oturur | Hayır — kanal sorusu A5 kapandıktan sonra yeniden sorulur | Bu not |
| **A9** `TraconRunEventSinkOptions.Capacity` + `OnDropped` | Tracon'in düşüren bir kanalı yok; öneri metrik eklemek değil **yeni bir kanal inşa etmek** demek. Tüketici bunu bilseydi istemezdi | Hayır — gerçek bir sink darboğazı ölçülürse yeniden açılır | Bu not |
| **B5** redaksiyon profili | Redakte edilmiş run replay edilemez; Faz 112'nin replay sözleşmesiyle çelişir | Hayır | Bu not |
| **B10** prompt bisector | n blok = n koşum; maliyet en yüksek, talep kanıtı en zayıf | Hayır | Bu not |

---

## 6. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| Hangi kalemler faza dönüşsün, hangi sırayla? | *bekliyor* |
| İki doküman kusuru için `kusur-giderme` şimdi mi koşsun? | *bekliyor* |
