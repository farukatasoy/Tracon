# Tüketici analizinin codebase ile ölçümü — 7 Eylül 2026

**Sonuç:** Raporun doküman tutarlılığı eleştirisi haklı. Mevcut kapılar yanlış davranış anlatımlarını geçirebiliyor. Definition edit sırasında alan kaybı da gerçek bir kusur sınıfı. Buna karşılık raporun bütün P0 önerilerini 1.0 öncesi zorunlu faza çevirmek doğru değil: bazı altyapılar zaten var, bazıları bilinçli ürün sınırı, bazıları host'un domain sorumluluğu.

**İlk üç aksiyon önerisi:** Definition round-trip kusurunu kapat; davranış ve yayın durumu anlatımını düzelt; mevcut F-171'i doküman iddialarının kapsamlı doğrulamasına taşı. Bunlardan sonra state upgrade kanıtı ve iki node'lu işletim senaryolarını seç.

## 1. Kapsam, zemin ve kanıt sınırı

- Girdi: [AgentPrism — Kapsamlı Teknik ve Ürün Analizi](../../AgentPrism-Kapsamli-Teknik-Analiz-2026-09-07.md), 1005 satır. SHA-256: `5127dd2a98ad24792c8de8c57baafae9bf70151a428c2402e14c7101f22aec1c`.
- Codebase: `2a3f5cff0f43078d7c41d5faf24ad45cb7131a8e`. Başlangıçta yalnız girdi raporu untracked idi. Bu inceleme ürün kodunu değiştirmedi, commit veya yayın yapmadı.
- Kullanıcının netleştirmesi: **“şu an için henüz yayınlanmamış geliştirme sürümü”**. Registry'de yokluk bir yayın arızası değildir. Yayınlanmış gibi anlatmak doküman kusurudur.
- [Yol haritasında](../../YOL-HARITASI.md) son numaralı faz 154 kapalıdır. Üretecin 155 kayıt sayması ile son faz numarası aynı ölçüm değildir. Son üç fazın devir notları okundu. Faz 153 özellikle case snapshot'ının bulunmadığını ve K-716 sınırını devreder.
- [Aday değerlendirme çerçevesi](../../ADAYLAR.md#değerlendirme-ölçütleri), F-171, ilgili mevcut adaylar, reddedilen karar indeksi ve ilgili kararların gerekçeleri kontrol edildi. Manuel test indeksi tarihsel koşum notları taşır; oradaki eski bulgular güncel kusur diye aktarılmadı.
- Kullanıcı hazır rapordaki feedback'in **tamamını** incelemeyi istedi. Skill'in yeni fikir üretip kullanıcıya eletme aşaması yerine rapordaki 18 öneri araştırma listesi olarak alındı. Yeni F/K/faz numarası ayrılmadı; aday defterine onaylanmış ürün kararı yazılmadı.

**Kanıt sınıfları:** **Çalıştırıldı**: bu tur komut/prob sonucu alındı. **Kodla ölçüldü**: ilgili gövde veya sözleşme okundu; çalışma zamanı sertifikası değildir. **Doğrulama bekliyor**: riskin penceresi bulundu fakat failure injection yapılmadı. **Ürün kararı**: değer/risk yargısı; teknik ispat gibi sunulmaz.

Tam .NET build/test, gerçek SQL crash testi, provider smoke, browser E2E, temiz paket restore ve load/chaos koşumu yapılmadı. Bu belge bir keşif raporudur; yayın onayı veya güvenlik sertifikası değildir. Test dosyasının varlığı “test bu tur geçti” anlamına gelmez. Yokluk sonuçları belirtilen kayıt/arayüz/akış kapsamındadır; repo'nun her satırının incelendiği iddia edilmez.

Tam rapor arşiv inceleme ağacında tutuldu. Keşif giriş kaydı [buradadır](../../kesif/2026-09-07-tuketici-analizi.md). Gerekçe: tur başında `docs/kesif` 355.426 / 370.000 B idi. Ayrıntıyı iki yerde çoğaltmadan bütçe korunur.

## 2. Bu tur çalıştırılan ölçümler

| Ölçüm | Sonuç | Anlamı |
|---|---|---|
| Canlı `llms.txt`, `llms-full.txt`, OpenAPI indirme | Üçü HTTP 200 | Web aracı bu URL'leri açamadı; TLS doğrulamasını kapatmadan `curl` kullanıldı. |
| NuGet meta/Core ve npm client index | Üçü HTTP 404 | Kullanıcının yayınlanmamış sürüm açıklamasıyla tutarlı. Abstractions ve GitHub araması bu tur tekrar edilmedi. |
| Yerel ve canlı güncel OpenAPI sayımı | **128 path, 165 operasyon, 274 schema; 23 alan tag'i + ortak AgentPrism** | Girdi raporundaki 127/164/269 eski snapshot'a ait. Runtime host'un tüm olası rotalarının sayısı değildir. |
| Diagnostics snapshot kontrolü | `/api/diagnostics` mevcut | Conditional endpoint'in doküman build profilinde bulunduğunu gösterir. Her host'ta etkin olduğunu göstermez. |
| UI route kaynak sayımı | **28 module, 30 farklı JSX Screen component, 36 route** | 28/30 farkının nedeni bulundu: kapı dosya/module sayıyor, diğer metin component sayıyor. |
| `node docs-site/scripts/check-content.mjs` | Exit 0; **52 manual / 1104 toplam sayfa** geçti | Aşağıdaki davranış çelişkileri dururken kapı yeşil. Semantik doğruluk kanıtı değil. |
| Gerçek `model.ts` kodunun TypeScript transpile + Node probu | Yalnız vector search açıkken request `memory: null`; `parameters` ve `sharedInstructionsName` request'te yok | Editor mapper kusuru çalışma anında görüldü. Browser veya SQL uçtan uca koşumu değildir. |
| Client `prefix.test.ts` + `paths-coverage.test.ts` | **8 test geçti** | Prefix normalizasyonu ve generated path kapsamı; tüm protokol davranışları değil. |
| Python manifest + farklı içerikte artifact koruma testleri | **2 test geçti** | Manifest/overwrite altyapısı sıfırdan yazılmamalı. Public yayın yapıldığını kanıtlamaz. |
| Başlangıç `dokuman-bakim.py --denetle` | Exit 0; kırık bağlantı 0 | Repo doküman disiplini kapısı da tüketici davranış çelişkilerini yakalamadı. |

### Snapshot kimlikleri

| Yüzey | Bu tur SHA-256 / boyut |
|---|---|
| Canlı `llms.txt` | `fd939cb3ca28060da42773def2856aea91eed44d024e8b0c144f223e27162e96` · 21.193 B |
| Canlı `llms-full.txt` | `d41d5e9e87285dc2a6661fe3b3b697342d5ed87c88698c922ee04d70d79ad3f3` · 666.544 B |
| Canlı ve yerel site OpenAPI | `78d569800d16ccb25b5a4bc984592e7de8b123d85442d89141330ab7af15dcf5` · canlı 586.148 B |
| Paketlenen kaynak OpenAPI | `25e2a31556e2cca2d387864b67a25a3ed0561ad7685ffe9dbf8b66aaea5a8156` |

Site OpenAPI kopyası sanitize edilir; paketlenen dosyayla byte eşitliği beklenmez. İkisinin ölçülen path/operation/schema sayıları eşittir. Bu tur bütün schema açıklamaları için semantik eşitlik testi yapılmadı.

**Önemli düzeltme:** `llms.txt` içindeki `d69f50ba` Git commit'i değildir. `docs-site/scripts/build-agent-map.mjs:97` map gövdesinin SHA-256 özetinden sekiz karakter alır. Map değişmeden tam doküman ve OpenAPI değişebilir. Girdi raporu bunu kesin commit diye sunmuyor; fakat release izlenebilirliği tasarlarken bu damga commit yerine kullanılamaz.

## 3. Doküman kalitesi ve tutarlılığı — ayrı değerlendirme

**Şüphe doğrulandı; sorun yalnız bayat sayaç değil.** Bazı metinler yanlış configuration, yanlış API key ve ikinci bir run başlatma yönlendirmesi üretiyor. Bunlar tüketicinin gerçek davranışını etkiler. Sınırı doğru anlatan sayfaların varlığı, çelişen diğer sayfayı etkisiz kılmaz.

Aşağıdaki D01–D18, kaynak raporun §14.1'deki 12 satırını ve §14.2'deki altı maddesini eksiksiz karşılar.

| ID / feedback | Kod ve doküman ölçümü | Karar ve yapılacak iş |
|---|---|---|
| D01 · HTTP sayıları | `getting-started/index.md:23` ve `reference/compatibility.md:145`: 143; `capabilities.md:240`: 162; güncel JSON: 165. Ana sayfa güncel sayıyı taşıyor. | **Doküman kusuru.** Tüm güncel tüketici yüzeyleri tek ölçüme bağlanmalı; yeni sayıyı elle üç yere yazmak sınıfı kapatmaz. Tarihli snapshot'ın eski sayısı değiştirilmez. F-171. |
| D02 · Tag sayısı | `reference/compatibility.md:146`: 19; JSON: 23 alan + ortak tag. | **Doküman kusuru.** “Alan tag'i” ile toplam tag tanımı ayrılmalı; kaynak JSON'dan üretilmeli veya sayı kaldırılmalı. |
| D03 · Diagnostics kapsamı | `reference/compatibility.md:145–151` conditional rotaları reference dışında gibi sunuyor; JSON'da diagnostics var. | **Anlatım kusuru.** “Host'ta conditional” ile “bu snapshot'ta mevcut” ayrı sütunlar olmalı. Route conditional olma davranışı doğru; koddan kaldırılmaz. |
| D04 · UI 28/30/36 | `check-content.mjs:107` benzersiz import module sayıyor. `skills.tsx` ve `triggers.tsx` ikişer Screen export ediyor. `app.tsx:41` route listesi 30 component / 36 route. | **Ölçüm tanımı kusuru.** “Ekran” kullanıcıya gösterilen component ise 30; 28 module teknik ayrıntıdır. Tek tanım seçip kapıyı düzelt. Salt drift'ten daha yapısal. |
| D05 · Tenancy default | Changelog `multi-tenant by default`; `AgentPrismTenancyOptions.cs:47` bool initializer yok, default false. | **Yüksek öncelikli doküman kusuru.** Tek tenant default açık yazılmalı. Generated changelog değil kök `CHANGELOG.md` düzeltilmeli. |
| D06 · Worker default | `ui.md:162` yalnız `UseScheduling()` ile kuyruğun tüketildiğini söylüyor. `AgentPrismSchedulingOptions.cs:14,22`: Enabled/RunWorker true. Background rehberi doğru. | **Yüksek öncelikli doküman kusuru.** `UseScheduling` worker açmak için şart değil. API-only örneğinde RunWorker=false açık olmalı. |
| D07 · CLI release kapsamı | Changelog migrate/status/health sayıyor; `EvalCommand.cs` ve güncel rehber eval veriyor. `build-changelog.mjs:45` Unreleased bölümünü düşürüyor. | **Sürüm aidiyeti belirsizliği.** Eski release notuna her yeni capability eklenmez. Henüz yayın olmadığından yayın durumu önce düzeltilmeli; ilk gerçek release içeriği o artifact ile sabitlenmeli. “Eval kodu eksik” reddedildi. |
| D08 · OpenAI scope | `guides/openai-api.md:191` ExternalInvoke öneriyor. `OpenAIChatCompletionsEndpoints.cs:53`, `OpenAIResponsesEndpoints.cs:81`: **RunsWrite**. Conversations okuma/silme RunsRead/RunsWrite. | **Yüksek öncelikli doküman kusuru.** OpenAI örneğini RunsWrite ile düzelt; MCP/A2A ExternalInvoke kalır. Kod scope'unu yanlış metne uydurma. Sadece ExternalInvoke verilen key OpenAI çağrısının doğru anahtarı değildir. |
| D09 · A2A expose-all | `external-agents.md:188` yok diyor; :197 iki yüzeye genelliyor. `AgentPrismA2AOptions` ile MCP options ayrı. | **Doküman kusuru.** ExposeAllAgents ve ToolNamePrefix MCP'ye özgü anlatılmalı. A2A'ya sırf metni doğrulamak için yeni feature eklenmez. |
| D10 · Structured validation | Rehber :8 opt-in seam'i açıklıyor; :29 mode tablosu “None”, :301 payload validation “Not performed”. `StructuredResponseValidatingAgent.cs:160` repair/validation akışı; options default kapalı. | **Eksik koşul anlatımı.** İddianın default davranış için doğru olduğunu belirt; request formatı / syntax / custom schema / repair ayrımı yap. Built-in tam schema validator yokluğu gerçek fakat ayrı ürün sınırı. |
| D11 · Package identity | `reference/versioning.md:24` immutable artifact; custom store rehberi :89 aynı preview'ın farklı içerik olabileceğini söylüyor. `kapi.py:870` content fingerprint ile overwrite koruyor, :900 manifest yazıyor; dirty pack ayrı sürüm ister. | **Doküman kusuru, altyapı yokluğu değil.** Resmî build ile eski/harici/local build istisnasını adlandır. Byte eşitliği ile ZIP metadata'dan arındırılmış içerik fingerprint'ini de karıştırma. |
| D12 · TypeScript prefix | `generate.mjs:50` önce `strip-prefix.mjs` çalıştırıyor; public JSON prefiksli. Prefix testleri geçti. | **Kod kusuru iddiası çürütüldü.** Rehber :48'de “generation document” yerine “normalize edilen ara girdi” de; public snapshot ile linkle. Normalize ara JSON'u ayrıca yayımlamak zorunlu değil; dönüşüm ve custom-prefix örneği yeterli. |
| D13 · Approval resume | Background :110–112 karar sonrası elle yeni run diyor. `ApprovalEndpoints.cs:203–276` audit → decide → yeni queued run → ApprovalResume job üretiyor. | **Yüksek öncelikli doküman kusuru.** Durable mailbox kararı otomatik enqueue eder; tüketici ikinci run başlatmamalı. In-band karar sonraki request ile gelir; workflow respond ayrı akıştır. Üç ayrı sequence ve failure davranışı yazılmalı. |
| D14 · Code-only / stored scripts | `AgentContracts.cs:128` Scripts kabul ediyor; yürütme Enabled + AllowStoredScripts + tenant grant koşullu. Script runner ayrı süreç ve sınırlama uygular; OS sandbox vaadi yok. | **Sınır anlatımı düzeltilmeli.** “Tool implementation kodda” doğru; “sunucuda çalışabilecek hiçbir içerik saklanamaz” yanlış. Script writer/admin, grant veren ve host isolation sorumlusu ayrı gösterilmeli. Tool editor yasağı değişmez. |
| D15 · SQLite cross-instance | Background :331 SQLite'ı cross-instance election listesine alıyor; :389 tek process diyor. Store lease semantiği ile production tavsiyesi farklı. | **Doküman kusuru.** Lease implementation varlığı bir cluster support garantisi değildir. SQLite tek process tavsiyesi korunur; cross-instance örneği PostgreSQL/SQL Server ile yazılır. |
| D16 · Telemetry dışarı çıkmaz | `index.mdx:134` mutlak ifade; observability rehberi :45 host OTel exporter kuruyor. Provider/MCP/webhook zaten network kullanır. | **Doküman kusuru.** “AgentPrism'e zorunlu telemetry gönderilmez” gibi dar ifade kullan. Exporter ve entegrasyon egress'ini gizleme. |
| D17 · One-line console | `concepts/index.md:55` bir satırla console; UI rehberi paket + UseUI + MapAgentPrism adımlarını veriyor. | **Onboarding kusuru.** Runtime DI kaydı ile HTTP/UI kurulumu ayrılmalı. Gerçek minimal snippet'i tek kaynak yap; satır sayısı pazarlamasını kaldır. |
| D18 · Eval sequence / ID | `evaluation.md:61` sıra değişince eski sonuçların başka case'e hizalandığını söylüyor; :117 ID hizalamasını doğru açıklıyor. `EvalRunDiffBuilder.cs:195` CaseId kullanıyor. | **Doküman kusuru.** Seq görüntüleme sırasıdır; ID karşılaştırma kimliği. HTTP replace yeni ID ürettiğinde Added/Removed olur; başka case'e sırayla eşleşmez. |

Doküman yolları tabloda `docs-site/src/content/docs/` altına görelidir. Kod yollarının tam envanteri §9'dadır.

### 3.1 Rapora ek ölçülen doküman sorunları

1. **Yayın durumunun yanlış sunumu.** `CHANGELOG.md` preview.1'i 3 Eylül'de “Initial public preview release” diye adlandırıyor. `build-changelog.mjs:45–46` bu çıktıyı kurulabilir sürüm anlatısı olarak üretir. Kullanıcı bunun yayımlanmamış geliştirme sürümü olduğunu belirtti. Release otomasyonu varlığı public yayın kanıtı değildir. Site, README kurulum yönlendirmesi ve release notes bu aşamayı doğru söylemeli; henüz olmayan feed adresi uydurulmamalı.
2. **Mevcut güven bilgisinin tüketiciye ulaşmaması.** `LICENSE:1` MIT; `SECURITY.md:5` destek politikası, :20 private bildirim adresi ve aşağıda yanıt hedefleri var. `src/Directory.Build.props:42,61` lisans/repository metadata'sı da mevcut. Yeni lisans icat etmek yerine siteye görünür bağlantı ve açık erişim durumu gerekir. Private repository'nin varlığı public erişilebilirliğini kanıtlamaz.
3. **XML dokümanı da ayrı kalite yüzeyi.** `MemorySettings.cs:18,27` hâlâ “in this phase / later phase” anlatımı taşıyor. `EvalCase.cs:28` ExpectedTools'u `toolCalled` tarafından okunuyormuş gibi açıklıyor; `EvalCheckRegistry.cs:145` tool listesini check spec'teki `tools` alanından alıyor. Tüketiciye sevk edilen XML ile site aynı contract'ı anlatmalı. Beklenen tool alanını değiştirmek bir davranış kararı; bu tur önerilen ilk düzeltme XML'deki yanlış yönlendirmedir.
4. **Map revision bütün dokümanın sürümü değil.** Aynı `d69f50ba` altında full docs hash'i değişmiş. Map damgasına ayrıca full docs/OpenAPI/source commit kimliği eklenmedikçe bu üçlü tek snapshot olarak tanımlanamaz.

### 3.2 Kapılar neden kaçırıyor?

`check-content.mjs:94–121` package/operation/screen ölçümlerini **landing page'deki belirli ifadelerde** arıyor. `:452` HTTP giriş sayfasının sayılarını da denetliyor. Bu iyi bir başlangıç; capability map ve compatibility prose'undaki bütün tekrarları kapsamıyor. Screen ölçümü ayrıca component yerine module sayıyor. `:438` seçenek adının sayfada bulunmasını kontrol ediyor; seçeneğin **default'unun doğru anlatıldığını** kanıtlamıyor.

Bu nedenle “kapı yeşil → doküman doğru” sonucu hatalıdır. Bu tur semantik çelişkiler varken **52 manual sayfa geçti**. Bir doğruluk yüzdesi üretilemez: bütün iddiaların evreni ve otomatik doğrulanabilir altkümesi henüz tanımlı değil.

**Önerilen kalıcı çözüm — mevcut F-171 üzerinden:**

- Sayılabilir iddialar için tek ölçüm üret: package envanteri, Screen component, route, operation, tag. Sayaç karar vermeye yardımcı olmuyorsa kaldır. Tarihli release snapshot'larını güncel sayıyla ezme.
- Default/policy iddialarını ayrı contract olarak ele al: worker, tenancy, scope, ownership, structured response, opt-in recovery. Options initializer'ı için kaynak/çalıştırma probu; endpoint policy için metadata + gerçek deny/allow testi kullan.
- Her yetenek için dört soru görünür olsun: kayıtlı mı, default açık mı, bu ingress'te destekli mi, bu sürümde var mı? “Destek var” tek sütunu bunları karşılamıyor.
- Üretilen, elle yazılan, paketlenen XML/README, OpenAPI, npm/.NET client ve LLM metnini iddia envanterine bağla. Aynı metni üreteçte regex ile onarmak yerine yanlış kaynağı kırmızı yap.
- Gate'in kendisini bozma örnekleriyle sınamak gerekir: worker false yazısı, yanlış scope, module/component sayısı, stale operation sayısı. Kapı bunlarda kırılmıyorsa kapsamı kanıtlanmış değildir.
- Semantik editoryal inceleme kalır: “tek satır”, “telemetry çıkmaz”, “production-grade” gibi ifadeler salt reflection veya regex ile doğrulanmaz. Context içindeki garantiyi daralt.

**Öncelik:** D05/D06/D08/D13 ve yanlış yayın durumu önce. Sonra definition alanlarını anlatan metinler, diğer D maddeleri ve otomatik koruma. F-171'i beklerken tüketiciyi yanlış yönlendiren mevcut prose kusurları açık bırakılmaz.

## 4. Kusur kanalı — yeni faz adayına dönüştürülmemeli

### B01 · Definition aktarımında alan kaybı — doğrulandı, önce kapatılmalı

**Kanıt zinciri:** `AgentDefinition` → `use-agent-editor.ts:110` → `model.ts:146` → `AgentDefinitionRequest.ToDefinition():70` → `AgentEndpoints.cs:643` → `SqlAgentDefinitionStore.cs:117` JSON payload yazımı. Update gövdesi eski definition'la merge yapmıyor; yeni request'ten yeni definition oluşturuyor.

| Alan | Kaybın yeri | Ölçüm sonucu |
|---|---|---|
| `Parameters` | UI state ve request mapper | HTTP DTO destekliyor, editor göndermiyor; DTO default `[]`. |
| `SharedInstructionsName` | UI state ve request mapper | DTO destekliyor, editor göndermiyor; default null. |
| `McpResourceUris` | HTTP DTO ve SQL payload mapper | DTO bu alanı hiç taşımıyor. `AgentDefinitionPayload.FromDefinition/ToDefinition` da taşımıyor. SQL save sırasında UI'dan önce kaybolabilir. HTTP create veya SQL SaveAsync ile dolu kaldığını varsayan fixture yanlış olur. |
| `SubAgents` | HTTP DTO ve SQL payload mapper | Definition'da var; update DTO'sunda ve SQL payload'ında yok. |
| `Metadata` | HTTP DTO/ToDefinition | Definition'da var; update DTO'sunda yok. Server-owned alanlarla karıştırılmamalı. |
| `Model.ProviderSettings` | UI model yeniden kuruluyor | Mevcut nested model korunmadan yeni ModelBinding oluşturuluyor. |
| `Model.ResponseCache` | UI model mapper | Gönderilmiyor. |
| `Model.AllowConcurrentToolCalls` | UI model mapper | Gönderilmiyor; mevcut true değeri korunmuyor. |
| Yalnız vector search içeren `Memory` | `memoryHasAnything`, model.ts:193 | Predicate yalnız file/todo/text search okuyor. Gerçek source probunda `memory: null` çıktı. Başka memory flag'i true ise bütün memory nesnesi taşınıyor; koşul bu yüzden önemlidir. |

İlk sekiz satır kaynak akışıyla ölçüldü; parameters/shared instructions request yokluğu ve son satır Node probuyla da görüldü. SQL payload mapper da ayrıca okundu: `src/AgentPrism.Sql.Shared/Internal/AgentDefinitionPayload.cs:68,97` iki yönde McpResourceUris/SubAgents alanlarını atıyor. In-memory store ise `definition with` kopyası alıyor (`InMemoryAgentDefinitionStore.cs:112`); aynı kayıt iki backend'de farklı korunabilir. Bütün alanların SQL'de kaybolduğu browser E2E bu tur koşulmadı. Bazı mevcut definition kombinasyonları validation'da reddedilebilir; bu, mapper'ın lossless olduğu anlamına gelmez.

**Aksiyon:** `kusur-giderme` ile tek alanı değil sınıfı kapat. Önce API'den round-trip desteklenen alanları ve .NET/in-memory store üzerinden oluşturulmuş daha geniş definition'ları ayır. SQL SaveAsync/GetAsync için McpResourceUris/SubAgents kaybını ayrı sınır testiyle sabitle; yalnız editor mapper'ını değiştirmek sınıfı kapatmaz. Server-owned `Origin/Version/TenantId/UpdatedAt` alanlarını mass assignment'a açma. Bilinmeyen alanı koruma, açık merge veya edit reddi seçeneklerinden kalıcı contract seç. Mapper testi yanında gerçek HTTP ve UI save sınırını geçen test gerekir.

**Başarı ölçütü:** Yalnız description değiştirilen kayıt bütün diğer izinli alanları korur. Desteklenmeyen alan sessizce silinmez. Vector-only memory korunur. Model alt alanları ve yeni eklenen definition alanları aynı kapıya dahildir.

**Karşı görüş:** Tam editor kontrolü eklemek maliyetlidir; ancak veriyi korumak için her alana UI kontrolü şart değildir. Sessiz kayıp için ciddi bir savunma yoktur. “Belgelenmiş sınırlama” veri bütünlüğü kusurunu meşrulaştırmaz.

### B02 · Yanlış tüketici sözleşmesi — doğrulandı

§3'te D01–D11 ve D13–D18'de tariflenen yanlış/eksik koşullu anlatımlar ile yayın durumu ve XML örnekleri doküman kusuru kanalındadır. D12'de runtime/client hatası bulunmadı; anlatım netleştirilir. D07 tarih/sürüm aidiyeti gerektirir; sırf güncel CLI büyüdü diye tarihsel release notu yanlış sayılmaz.

**Aksiyon:** Kaynak prose'u düzelt, generated çıktıları mevcut üreticiden geçir, site/LLM/paket yüzeyini birlikte doğrula. Bir “doküman redesign” fazını beklemek gerekmez. F-171, bu düzeltmelerin tekrar kaçmamasını sağlayacak ayrı yetenektir.

### B03 · Approval kararından resume enqueue'ya hata penceresi — doğrulama bekliyor

`ApprovalEndpoints.cs:207–274` audit, DecideAsync, StartRunAsync ve EnqueueAsync'i ardışık çağırıyor. Karar uygulandıktan sonra enqueue başarısız olursa sonraki karar request'i `AlreadyDecided` dönebilir. Bu method'da bu adımları kapsayan transaction veya telafi yok. **Kaynakla ölçülen bir failure penceresi; bu tur çalıştırılmış kusur repro'su değil.**

**Aksiyon:** Genel “approval ledger fazı” içine gömmek yerine mevcut kusur protokolüne öncelikli doğrulama girdisi ver. Decide sonrası store failure enjekte et; karar/run/job durumunu, endpoint retry'sini ve operatör kurtarma yolunu ölç. Sistem genelinde başka recovery bu pencereyi kapatıyorsa bulguyu daralt veya kapat. Kapanmıyorsa karar ile resume niyetinin dayanıklı bağlanması gerekir.

**Karşı görüş:** Birkaç bağımsız store'u tek transaction'a zorlamak custom store contract'ını bozabilir. Bu, pencereyi yok sayma gerekçesi değildir; atomic intent/outbox veya tekrar sürülebilir handoff tasarımı ölçülmelidir.

## 5. Kaynak rapordaki 18 önerinin kararı

Buradaki A01–A18 rapor içi izleme kimliğidir; **F-NN değildir**. Mercek numaraları mevcut aday çerçevesine atıftır. Maliyetler kişi-gün tahmini değil, etkilenen yüzeydir. Yeni NuGet bağımlılığı seçilmediği için restore ağırlığı ölçülmüş gibi yazılmadı.

### A01 · Yayın zinciri ve sürümlü doküman — kısmen zaten var, kalanını daralt

**Kaynak:** §15.2, §2, §13, §16. **Ölçüldü:** `kapi.py:900` version/commit/dirty/packages manifesti; `:1146` paket hash'leri; `Directory.Build.targets:79–129` dirty pack sınırı; `src/Directory.Build.props:42,61` MIT/repo; CI pack/release-dryrun/npm/NuGet/release işleri. İki Python testi geçti.

**Aksiyon:** Yayınlanmış anlatısını B02 ile düzelt. İlk public dağıtım seçildiğinde mevcut release rehearsal'ı kullan. Manifesti npm artifact, OpenAPI/docs hash'leri, dependency pins ve migration/AOT profil kimliğiyle bağlamak **adaydır**. Versioned docs ilk gerçek release ile anlam kazanır. Her sayfaya elle introducedIn yazmak yeni drift üretir; önce release snapshot + açık main etiketi yeterliliği değerlendirilir.

**Değer:** İlk tüketici ve support sürümü teşhis eder. **Maliyet:** Build/docs/client metadata; temel manifesti tekrar yazma yok. **Risk:** Local proof'u public provenance diye sunmak. **Hazırlık:** Mevcut release altyapısı. **Mercek:** 1,2,3,6. **Karşı görüş:** Yayın öncesinde tam sürüm seçici erken maliyet; açık development etiketi bugün daha acildir. Public repo zorunluluğu ve “404 = ürün kusuru” reddedildi.

### A02 · Production profile ve policy parity — parçalar var, composition adayı

**Kaynak:** §15.3, §10, §12.3. **Ölçüldü:** `RequiredBindingValidator.cs:130` gerçek binding tipini denetliyor; startup functional testleri var. `RunAuthorizationCoverageTests.cs:24` yeni endpoint'i kendi keşfedemediğini açık yazıyor. Management/OpenAI/voice resource authorization functional test aileleri mevcut.

**Aksiyon:** Required binding ve role gate'i yeniden yazma. Operator-console / customer-chat / worker gibi kullanım bazlı, açıklanabilir validation profili seçilebilir. Bütün ingress'lerin aynı özelliği vermesini değil, **aynı güven sınırını korumasını** test et. API key scope, run authorization, tenant, initiator attribution, worker admission ve exposure farklı contract'lardır; tek evaluator varsayımıyla hepsini birleştirme.

**Değer:** Host entegrasyonunda unutulan ayarı erken bulur. **Maliyet:** Options/diagnostics + ingress test matrisi; yeni paket zorunlu değil. **Risk:** Meşru in-memory/internal host'u yanlış reddetmek. **Hazırlık:** Mevcut seams. **Mercek:** 1,2,3,5. **Karşı görüş:** Binding mevcut diye authorization doğru olmayabilir. Profil ancak deny/allow senaryolarıyla değer taşır. Bütün opt-in özellikleri default açmak reddedildi.

### A03 · Tool operation ledger / unknown outcome — koşullu aday, genel P0 değil

**Kaynak:** §15.4, §6.3, §8.4–5, §13. **Ölçüldü:** `FallbackChatClient.cs:115` ledger turn-local; `TimeoutAIFunction.cs:57` aynı caller token'ıyla body'yi başlatıp bekleme yarışı yapıyor. Mevcut mekanizma business operation transaction'ı değil.

**Aksiyon:** Önce domain operation key'nin host'tan geldiği, downstream idempotency/lookup ile çalışan reference senaryosu kur. Prepared/Executing/OutcomeUnknown ve reconcile ihtiyaçları bu senaryoyla doğrulanırsa opt-in public operation seam/store adayına geç. RunId business dedup key yapılamaz. Crash öncesi/sonrası lookup sonucu bilinmiyorsa otomatik retry güvenli sayılmaz.

**Değer:** Dış etki üreten kurumsal otomasyon. **Maliyet:** Store sözleşmesi, migration'lar, invocation/recovery/test yüzeyi. **Risk:** Framework'ün yapamayacağı exactly-once sözü; domain ledger'ıyla ikinci hakikat kaynağı. **Hazırlık:** Mevcut replay yalnız bir parça; downstream contract gereklidir. **Mercek:** 2,3,5. **Karşı görüş:** Read-only agent için bu altyapı gereksizdir. Ödeme transaction'ının sahibi host kalmalı. Tüm AgentPrism tüketicileri için 1.0 blocker olması reddedildi; kritik side-effect ürününde kabul kapısı olması desteklendi.

### A04 · State compatibility / upgrade preflight — dar kapsamla aday

**Kaynak:** §15.5, §7.4, §8.1. **Ölçüldü:** `AgentSessionManager.cs:188–213` schema generation guard ve deserialize hatasında MAF sürümü; checkpoint store :83 ve WorkflowRunner :946 aynı aile. MAF version stamp otomatik migrator değildir. İlgili test dizinlerinde önceki public artifact corpus'u bu taramada bulunmadı.

**Aksiyon:** İlk release formatını arşivleyen session/checkpoint corpus'u, salt okunur bounded restore probe ve supported upgrade window. Başarısız restore için drain/old runtime prosedürü. Ancak gerçek ihtiyaç ve state erişim contract'ı ölçüldükten sonra migrator veya version-affinity worker seçilmeli. Bütün MAF sürümleri arası converter vaat etme.

**Değer:** Uzun session/approval kullanan nöbetçi. **Maliyet:** CLI/test corpus/diagnostics; genel migrator çok daha pahalı public yüzey. **Risk:** Probe'un veri değiştirmesi, hassas corpus, eski runtime'ı süresiz tutma. **Hazırlık:** Envelope ve hata teşhisi hazır. **Mercek:** 2,3,6. **Karşı görüş:** Henüz önceki public release yok; uyumluluk vaadi icat edilmez. Buna rağmen ilk corpus'u bugün tanımlamak sonraki sürümde gerekli kanıtı mümkün kılar.

### A05 · Lossless edit / immutable revision — üç ayrı iş

**Kaynak:** §15.6, §6.1, §11.5. **Ölçüldü:** B01. `AgentDefinitionRequest` expected revision taşımıyor; update mevcut sürüme karşı compare-and-swap yapmıyor. Versiyon geçmişi olmak stale edit'i reddetmek değildir.

**Aksiyon:** (1) Sessiz alan kaybı **B01 kusuru**. (2) Expected revision/ETag ile stale edit reddi **yeni contract adayı**. (3) Resolved instructions/tool/policy snapshot'ı ve content-addressed provenance **ayrı aday**. Secret value snapshot'a girmez. Hash saklamak, sonradan içeriği geri getirme garantisi değildir; retention birlikte tasarlanır.

**Değer:** Konfigürasyon bütünlüğü ve run açıklanabilirliği. **Maliyet:** İlk iş mevcut mapper/API; diğerleri store/API ve snapshot saklama. **Risk:** Mass assignment, yanlış merge, depolama hacmi. **Hazırlık:** Definition version store var. **Mercek:** 2,3,5,7. **Karşı görüş:** Tam immutable provenance yüksek maliyet; B01'i bunun bitmesine bağlamak yanlış. ETag eklemek API ergonomisini ve conflict contract'ını etkiler; keşif raporu implementasyon seçmez.

### A06 · JSON validator ve deadline — iki ayrı aday, default değişimi yok

**Kaynak:** §15.7, §6.3/6.5. **Ölçüldü:** `NoOpToolArgumentsValidator.cs:23` geçerli sonuç döndürüyor; structured validator JSON syntax/custom seam; `TimeoutAIFunction.cs:57–75` linked timeout token üretmiyor. Bu davranış açıkça belgelenmiş.

**Aksiyon:** Deadline için cooperative token, wait cutoff, caller cancel ve late outcome ayrımını tasarla; backward behavior etkisini yaz. Schema için mevcut `IToolArgumentsValidator` / `IStructuredResponseValidator` üzerinden opt-in adapter veya tüketici örneği değerlendir. Dialect, unsupported keyword, depth/size/regex sınırı ve AOT restore ölçümü yapılmadan paket seçme. Streaming delta'nın doğrulanmış olduğunu vaat etme; bounded buffering istenirse ayrı latency/memory contract'ı gerekir.

**Değer:** Tool yazarı ve veri doğrulayan tüketici. **Maliyet:** Deadline testleri/contract; validator seçilirse ayrı bağımlılık. **Risk:** Cooperative cancellation'ın dış etkiyi geri aldığı yanılsaması, schema engine attack surface. **Hazırlık:** Seams hazır. **Mercek:** 2,3,5. **Karşı görüş:** Host validator zaten mümkün; tam engine'i Core default'una almak gereksiz. K-090 eski **skill script shallow validation** kararıdır; bütün validator adapter'larını yasaklayan genel karar diye genişletilmez. Mevcut timeout semantiği salt rapor P0 dedi diye kusur olarak etiketlenmedi.

### A07 · Distributed cancel/control — somut işletim adayı

**Kaynak:** §15.8, §8.5. **Ölçüldü:** `RunCancellationRegistry.cs:8` process-local ConcurrentDictionary. Shared job cancel ile live run cancel farklı. SQL persistence bunu kendiliğinden çözmez.

**Aksiyon:** Önce SQL durable cancel intent + owner observation/ack contract'ı. Kabul, teslim ve terminal durumu ayrı tut. Yanlış node, owner crash, partition, stale lease ve child-tree cancel iki node testine girer. Bus yalnız latency ihtiyacı ölçülürse opt-in olur. Eski 409 davranışını 202'ye çevirmek public behavior değişimidir.

**Değer:** Çok node çalıştıran nöbetçi. **Maliyet:** Store/control endpoint/event ve migration. **Risk:** Ack'i durdu sanmak, cancellation'a uymayan tool, stale owner. **Hazırlık:** Cancellation registry, heartbeat, SQL altyapısı. **Mercek:** 2,3,6. **Karşı görüş:** Tek process için gereksizdir; her 1.0 tüketicisine şart yapılmaz. Gerçek multi-node işletim seçiliyorsa güçlü adaydır.

### A08 · Budget reservation/settlement — sert bütçe talebine bağlı aday

**Kaynak:** §15.9, §10.5. **Ölçüldü:** `QuotaEnforcer.cs:46,131` admission check ve completion accounting; `RunBudgetChatClient` turn sınırı. Reservation contract'ı mevcut bu akışlarda yok.

**Aksiyon:** Soft quota korunur. Sert bakiye senaryosunda root/child rezervasyonu, expiration, retry ve unknown price dahil settlement modeli ayrı tasarlanır. Image/voice/judge/embedding kapsamı netleşmeden “toplam maliyet tavanı” denmez. Reservation, eksik upstream usage veya bilinmeyen fiyatı sihirli biçimde çözmez.

**Değer:** Faturalı multi-tenant ürün. **Maliyet:** Transactional ledger, migration, bütün harcama yolları. **Risk:** Fazla rezervasyonda kapasite kaybı; az rezervasyonda overshoot. **Hazırlık:** Kullanım/fiyat snapshot'ı var, rezervasyon yok. **Mercek:** 2,3,8. **Karşı görüş:** Bugünkü approximate contract yanlış değildir. Gerçek strict-budget müşterisi olmadan genel release blocker yapmak reddedildi. Redis/global rate limit kararıyla bu işi birleştirme.

### A09 · Immutable eval corpus — değerli ama K-716 kararı açıkça ele alınmalı

**Kaynak:** §15.10, §9.2. **Ölçüldü:** `EvalEndpoints.cs:364` ID'siz case üretir; SQL/in-memory replace yeni ID atar. `EvalCaseResult.cs:20` CaseId var, immutable content snapshot yok. `EvalRunDiffBuilder.cs:195` ID ile eşleştirir. K-716 aynı gün bu sınırı kullanıcı kararıyla kabul etti.

**Aksiyon:** Mevcut CI gate/diff'i yeniden önerme. Stable case identity + content revision ve suite snapshot ayrı aday olabilir; önce **K-716 kapsam kararını genişletme onayı** gerekir. Ekosistem değiştiği kanıtlanmadı. Yeni tüketici feedback'i ihtiyacı yeniden tartışma gerekçesidir, eski kararı otomatik geçersiz kılmaz. Multi-turn corpus, branch/environment baseline ve judge/model/tool fingerprint bu temel üstünde ayrı genişlemeler olarak kalır.

**Değer:** Eval'le release yapan ekip. **Maliyet:** Case CRUD/revision, result snapshot, üç SQL dialect/migration ve diff/CLI/UI. **Risk:** Eski unversioned satırları aynı veriymiş gibi karşılaştırmak. **Hazırlık:** Diff/CLI ve provenance'ın parçaları hazır. **Mercek:** 3,5,7. **Karşı görüş:** Full immutable dataset, salt content-hash uyarısından daha maliyetli. Önce farklı soruları aynı case sanmayı engelleyen dar contract mı, tam yeniden koşulabilir corpus mu gerektiği seçilmeli. HTTP yolu otomatik “yanlış yeşil” değildir: değiştirilmiş ID'ler Added/Removed olur; doğru gate politikası ayrıca gerekir.

### A10 · Critical evidence outbox / audit anchor — iki aday, best-effort default korunur

**Kaynak:** §15.11, §7.2, §10.6. **Ölçüldü:** `RunEventWriter.cs:190–207` store hatasında recording'i disable eder ve sink dispatch bağımsız sürer. Approval audit karar öncesi zorunlu. Bunlar kasıtlı farklı contract'lardır.

**Aksiyon:** B03 gibi mevcut functional handoff penceresini önce ölç. Ayrı ihtiyaç varsa kritik decision intent için durable outbox ve hash-chain head için dış anchor adapter'ı değerlendir. Normal token/run telemetry'sini senkron durable yapıp run'ı store'a bağımlı hale getirme. Loss/delay/backpressure/dedup/retention ve subject erasure sonucu tasarıma dahil olmalı.

**Değer:** Kritik kanıt isteyen kurum. **Maliyet:** Outbox delivery yaşam döngüsü; anchor sink/provenance. **Risk:** Observability hatasının işlevi durdurması, kişisel verinin WORM'da kalması. **Hazırlık:** Run sink ve audit chain var. **Mercek:** 2,3,7. **Karşı görüş:** Domain transaction'ının kanıtı host'ta daha doğru yerde olabilir. Genel recording fail-closed dönüşümü mevcut temel kuralla çeliştiği için reddedildi.

### A11 · Approval lifecycle / execution binding — dar adaylar ve host sorumluluğu

**Kaynak:** §15.12, §10.3. **Ölçüldü:** `ToolApprovalRule.cs:29–70` tenant/agent/tool/hash/conditions/CreatedBy/CreatedAt var; expiry/review/last-used yok. Duplicate decision endpoint'te conflict; presenter snapshot kalıcı. B03 ayrı kusur doğrulaması.

**Aksiyon:** Standing rule expiry/review/last-used/revoke gerekçesi aday olabilir. Onaylanan canonical input ile invocation ilişkisi, policy/definition revision değişimi test edilmelidir. Sipariş entity version ve tutar kontrolünün otoritesi domain tool'dur; framework presenter'a taşınmaz. Dual approval yalnız somut ihtiyaçta. “Duplicate decision idempotent olsun” mevcut 409 contract'ına etkisiyle değerlendirilir; duplicate execution zaten engellenmelidir.

**Değer:** Uzun süreli yetki/onay işleten kurum. **Maliyet:** Rule/store/API/UI/migration; execution binding daha geniş. **Risk:** Süre dolumunda bekleyen işler, tekrar authorization actor'ının yanlış seçilmesi. **Hazırlık:** Rule evaluator ve audit mevcut. **Mercek:** 2,3,5. **Karşı görüş:** Her işlem için iki kişi gerektirmek otomasyonu bozabilir; domain TOCTOU'yu generic metadata tek başına çözemez.

### A12 · Worker recovery / queue operasyonu — mevcut veriden başlayarak aday

**Kaynak:** §15.13, §8.2, §12.1. **Ölçüldü:** `JobRecord.cs:49–76` TotalItems/DoneItems/FailedItems, attempts, LeaseOwner/LeaseUntil taşır. Scheduling endpoint'inde list/detail/cancel ve lane yüzeyi var. Lease expiry/reconciliation test aileleri mevcut.

**Aksiyon:** Önce bu veriyi kullanan oldest pending age, schedule lag, lane coverage ve no-worker diagnostics. Partial-success için yeni terminal enum zorunlu değil; outcome summary kullan. Kalıcı attempt history, poison/redrive ve overlap/misfire policy ayrı kapsam kararlarıdır. Redrive öncesi completed item'ı atlama ve unknown side effect kontrolü A03'e bağlıdır.

**Değer:** Nöbetçi. **Maliyet:** İlk dilim query/metrics/UI; attempt history store/migration ekleyebilir. **Risk:** Redrive'ın dış etkiyi tekrar etmesi ve gereksiz broker mimarisi. **Hazırlık:** Temel alanlar ve SQL queue hazır. **Mercek:** 2,3,7. **Karşı görüş:** Var olan lease owner/partial counts'u yeni API gibi eklemek gereksiz. Yeni queue backend talebi bu incelemeden çıkmıyor.

### A13 · MCP OAuth / content key lifecycle — ayır, secret sınırını koru

**Kaynak:** §15.14, §11.3, §10.6. **Ölçüldü:** `McpOAuthTokenCacheRegistry.cs:17` tenant+server başına in-memory cache. `AesGcmContentProtector.cs:74,87–95` envelope kid ile encrypt/decrypt. Mevcut koruma geçmiş veriyi topluca dönüştüren job değil.

**Aksiyon:** Host-owned token store seam, refresh concurrency ve reauthorization state koşullu aday. **AgentPrism SQL veritabanına token değeri yazma önerisi K-059 ile çelişir; şifreli olması bu sınırı kaldırmaz.** Harici host secret store referansı/seam'i bu kararı koruyabilir. Content key usage inventory/re-encryption/dry-run/progress ayrı adaydır; backup envanteri host runbook'unda kalır.

**Değer:** Restart sonrası MCP erişimi ve key retirement. **Maliyet:** Token ownership/lifecycle; ayrı rotation job ve kolon envanteri. **Risk:** Token sızıntısı, refresh yarışı, eski backup'ın çözülememesi. **Hazırlık:** Cache ve content envelope var; host secret provider seçilmedi. **Mercek:** 2,3,6. **Karşı görüş:** Memory-only doğru ve desteklenen bir tercihtir. Harici vault'u herkese zorunlu tutmak reddedildi. K-059'u yeniden açacak ekosistem kanıtı yok.

### A14 · Protocol conformance / negotiation — küçük matrix ile aday

**Kaynak:** §15.15, §11. **Ölçüldü:** `AgentPrismMetaResponse.cs:14–28` Version/Prefix/Authentication/Storage/Roles taşır; genel protocol capability matrisi yok. `Storage.JobWorkerEnabled` zaten mevcut. .NET/TS istemcileri OpenAPI'den üretiliyor; prefix testleri geçti. F-198 dual JSON/SSE client davranışı zaten açık kayıt.

**Aksiyon:** Önce release'e bağlı ingress capability matrisi ve pinned client fixture'ları: text, streaming, resume, session, approval, cancel, quota, unsupported fields. Yeni capability negotiation DTO gerekiyorsa mevcut meta'nın unauthenticated olmasını hesaba kat; tenant/agent politikası sızdırma. F-198'i çoğaltma. A2A push/background veya MCP approval'ı sırf parity uğruna ekleme.

**Değer:** SDK/entegrasyon yazarı. **Maliyet:** Test fixture/metadata/client/docs. **Risk:** “Her surface aynı” beklentisi ve anonim bilgi sızıntısı. **Hazırlık:** OpenAPI ve meta var. **Mercek:** 1,3,5,6. **Karşı görüş:** Dinamik negotiation, sabit versioned tablo yeterliyken gereksiz olabilir. OpenAPI schema testinin SSE/media contract'ını kanıtlamadığı korunmalı.

### A15 · RAG provenance / ACL / retrieval kalite — kısmen var, talebe bağlı aday

**Kaynak:** §15.16, §6.4. **Ölçüldü:** `VectorSearchHit.cs` SourceId/ChunkIndex/Metadata içeriyor; “provenance hiç yok” yanlış. `VectorSearchRequest.cs:7–22` TenantId/Collection/embedding/top/distance içeriyor; user/document ACL filtresi yok. `VectorSearchToolFactory.cs:59` bu request'i kullanıyor.

**Aksiyon:** Aynı tenant içinde belge bazlı erişim gerekiyorsa host policy'nin retrieval query'ye bağlandığı seam değerlendir. Revision/hash/offset ve ingestion/embedding provenance, deletion lineage ayrı contract'lardır. SourceId'nin mevcut olduğunu koru. Retrieval eval'de recall/grounding/citation ayrımı; hybrid/reranker ve dual-index migration ancak ölçülen gereksinimle.

**Değer:** Kurumsal belge araması. **Maliyet:** Knowledge query/store/ingestion schema ve eval fixture'ları. **Risk:** ACL'yi retrieval sonrası uygulayıp hassas chunk'ı modele çoktan vermek; yeni IAM sistemi. **Hazırlık:** Tenant collection ve source metadata var. **Mercek:** 3,5,7. **Karşı görüş:** Tenant/collection ayrımı yeterli ürünlerde kullanıcı ACL'si gereksiz karmaşıklıktır. Metadata alanı bulunması enforcement kanıtı değildir.

### A16 · Benchmark / chaos / production sample — mevcut harness'i genişlet

**Kaynak:** §15.17, §9.3, §12, §16. **Ölçüldü:** `bench/AgentPrism.Benchmarks` içinde cache, run store query ve event writer benchmark'ları var. `RunEventWriterBenchmarks.cs:20` NoOpRunStore kullanıyor; allocation ölçümü gerçek SQL load değildir. Lease expiry, reconciliation, concurrency ve authorization test aileleri de var.

**Aksiyon:** Sıfırdan benchmark altyapısı kurma. Mevcut bench üstüne bounded SQL/load ve iki process kill senaryoları ekle. DB unavailable, slow sink, provider timeout, retention hacmi, streaming fan-out ve rolling upgrade ayrı failure manifestleri taşımalı. API-only/worker-only/migration örneği bu testlerle bağlansın. CPU/RAM/DB/payload/concurrency ve dependency commit kimliği yazılsın; dış model latency'si kontrol düzlemi overhead'inden ayrı ölçülsün.

**Değer:** Nöbetçi ve satın alma/pilot ekibi. **Maliyet:** Test harness ve CI kaynakları; runtime NuGet büyümesi zorunlu değil. **Risk:** Flaky süre kapısı ve sentetik TPS pazarlaması. **Hazırlık:** Faz 116 allocation kapısı ve SQL test altyapısı. **Mercek:** 2,3,4,7. **Karşı görüş:** Tüm OS/DB/provider kombinasyonlarını tek turda kapsamaya çalışmak pahalıdır. Dar destek matrisi ve derin senaryo daha anlamlı. Ölçülmeden SLO sayısı vaat edilmez.

### A17 · Genel workflow / dış orchestrator — bu tur ertelendi

**Kaynak:** §15.18, §8.1. **Ölçüldü:** Mevcut Workflows katmanı checkpoint ve beş pattern taşır; `WorkflowRunner` resume/compatibility sınırları açık. Rapor generic DAG/typed ports/condition/timer/subworkflow/compensation için somut tüketici akışı vermiyor.

**Aksiyon:** Şimdilik yeni genel workflow motoru fazı açma. Sequential dışı function node, timer, compensation ve external orchestrator adapter birbirinden bağımsız büyük contract'lardır. Gerçek iş akışı mevcut pattern'larla çözülemiyorsa o vakadan ayrı keşif başlat; ortak run/governance korunur.

**Değer:** Ancak uzun business workflow isteyen tüketicide kanıtlanır. **Maliyet:** Versioned graph/state/determinism/compensation ve adapter paketleri. **Risk:** MAF'a paralel runtime olmak. **Hazırlık:** Bu tur seçilmiş SDK imzası/adapter restore ölçümü yok. **Mercek:** 2,6; ihtiyaç kanıtı zayıf. **Karşı görüş:** Mevcut recovery'yi sağlamlaştırmak daha doğrudan değer. Kalıcı mimari ret değil, talep bekleyen erteleme.

### A18 · İleri deney analizi / yeni UI / provider-vector adapter — bu tur ertelendi

**Kaynak:** §15.19, §9.4. **Ölçüldü:** Named scores, eval diff ve kalıcı score trendi Faz 152–154 ile mevcut; A/B assignment management ingress'inde. F-210 evaluator kataloğu zaten aday. Yeni bir “eval platformu” boşluğu yok.

**Aksiyon:** Confidence interval/minimum sample/sequential testing uyarısı/segment analizi için gerçek deney hacmi ve karar ihtiyacı bekle. Tenant admin/end-user review, provider ve vector backend taleplerini ayrı persona ihtiyaçlarıyla seç. F-210'u yeniden numaralama. Method/version/cohort provenance olmadan otomatik winner ilanı ekleme.

**Değer:** Veri oluşmuş deney ürününde. **Maliyet:** İstatistik contract'ı, UI; her adapter'ın ayrı dependency/test yükü. **Risk:** İstatistiksel yanlış güven ve host identity ownership'in ihlali. **Hazırlık:** Skor/query altyapısı var; talep/veri hacmi bu tur ölçülmedi. **Mercek:** 3,7,8; somut aday için kanıt yetersiz. **Karşı görüş:** Yeni provider sayısı olgunluk ölçüsü değildir. Talep yokken bunları 1.0'a bağlamak reddedildi.

## 6. Karar kanalı ve reddedilenler

### 6.1 Mevcut karara temas edenler

| Karar | Ölçüm / yeni durum | Sonuç |
|---|---|---|
| **K-716** · Eval content değişikliği kapsam dışı | Rapor yeni tüketici gerekçesi getiriyor; kod durumu karar günündekiyle aynı. Ekosistem değişimi yok. | A09 seçilirse karar kapsamı kullanıcıyla yeniden açılmalı. Bu rapor kararı değiştirmedi. |
| **K-059** · Secret değeri AgentPrism DB'ye yazılmaz | MCP token cache'in process-local olması gerçek; encrypted token SQL store önerisi sınırı aşar. | DB token store reddedildi. Host secret-store seam'i ayrı seçenek; kararı geçersiz kılan kanıt yok. |
| **K-090** · Skill script argument validation sığ | Tam gerekçe script validator'ına ait; yeni genel tool seam'leri zaten mevcut. | Bunu bütün schema adapter'larına genel yasak diye kullanma. Core'a zorunlu engine eklemek yine gerekçelendirilmedi. |
| **K-103 / K-339** · Alt/external agent approval sınırı | MCP/A2A'nın insanı olmayan çağrıları mevcut sınırla karşılaşıyor. | Negotiation/dokümanla açıkla. Bu tur upstream kanca değişimi ölçülmedi; otomatik yeniden açma yok. |
| **F-171** · Sevk edilen ölçümler için kapı | Mevcut kapı bazı sayıları ölçüyor ama kapsam ve ölçüm tanımı eksik. | Yeni kopya aday açma; mevcut adayın kanıtını bu raporla güncellemek sonraki seçim adımıdır. |
| **F-198 / F-210 / F-165** | Dual JSON/SSE client, evaluator katalogu, manuel testlerin CI'a taşınması kayıtlı işler. | İlgili feedback bunlara bağlanır; aynı başlıklar yeniden aday yapılmaz. |

### 6.2 Yapılmaması önerilen işler

| Öneri / çıkarım | Ret gerekçesi | Ret türü |
|---|---|---|
| Registry'de yokluğu hemen yayınlayarak kapatmak | Kullanıcı geliştirme sürümü dedi; analiz isteği yayın yetkisi değil. Sorun yanlış yayın anlatısıdır. | Bu tur kapsam dışı. |
| Yeni release manifesti/lisans/security policy'yi sıfırdan yazmak | Kod ve dosyalar mevcut; görünürlük ve kapsam eksikleri düzeltilecek. | Yinelenen iş. |
| TypeScript client path tasarımını prefiksli hale getirmek | Prefix strip bilinçli; 8 test geçti. Custom mount kullanımını bozar. | Mevcut doğru davranış korunur. |
| `PrismAgent` / `PrismMessage` paralel tip evreni | MAF tiplerini doğrudan kullanma ilkesini bozar. | Mevcut mimari sınır. |
| UI'da genel C#/shell tool editor | Code-defined tool güvenlik sınırına aykırı. Stored skill script'in dar opt-in contract'ı genel editor gerekçesi değildir. | Mevcut güvenlik sınırı. |
| Her capability'yi default açmak / tek sihirli Production=true | İhtiyaçlar farklı; tüketicinin composition yetkisini azaltır. | Yaklaşım reddi; açıklanabilir profil mümkün. |
| Exactly-once agent / prompt-injection-proof / tüm ürün AOT | Dış sistem/model sınırları ve paket matrisi bu garantileri vermiyor. | Yanlış garanti reddi. |
| Bütün recording'i fail-closed yapmak | Observability işlevi bozamaz; kritik karar handoff'u ayrı çözülür. | Mevcut contract korunur. |
| MCP token'ını encrypted diye AgentPrism SQL'e yazmak | K-059 değer saklama sınırı şifrelemeyle kalkmaz. | Mevcut güvenlik kararı. |
| Generic workflow, microservice veya broker dönüşümü | Somut kullanım acısı yok; embedded library değerini ve dependency yüzeyini değiştirir. | Bu tur erteleme; microservice zorunluluğu reddi. |
| Bütün P0/P1 önerilerini 1.0 blocker yapmak | Read-only/single-process ile kritik side effect/fleet aynı garantiye ihtiyaç duymuyor. | Öncelik çerçevesi reddi. |
| Tarihsel release/snapshot sayılarını bugünkü sayıyla değiştirmek | İnceleme kanıtını bozar; main ve release farklı snapshot'lardır. | Doküman yöntemi reddi. |
| Generic billing/IAM/OS sandbox ürününe dönüşmek | Bütçe, document ACL ve script isolation ihtiyaçları host ownership'i ortadan kaldırmaz. | Kapsam sınırı; dar seams mümkün. |

Bunlar keşif önerileridir. Bu tur kalıcı yeni K kaydı veya aday defterinin “Bilerek Önerilmeyenler” bölümüne yeni kullanıcı kararı yazılmadı. Mevcut reddedilmiş kararlar yeniden önerilmedi.

## 7. Rapordaki diğer eleştirilerin eksiksiz izleme haritası

Bu tablo §15 dışındaki analiz, risk, sınır ve tavsiyelerin nereye işlendiğini gösterir. Aynı feedback tekrarlandığında yeni kusur sayılmadı. Pozitif ürün anlatıları destekleyici kaynaklarla kontrol edildi; her capability için runtime kabul testi iddia edilmez.

| Kaynak bölüm | Eleştiri / sınır / feedback | Sonuç veya hedef |
|---|---|---|
| §1–2 | Tasarım kapsamı production kanıtı değildir; public artifact/repo/CI erişimi belirsiz | Kullanıcı açıklamasıyla yayın yokluğu normal; B02 yayın anlatısı, A01 kanıt zinciri, A16 işletim kanıtı. İsimdaş React/ACP projelerinin metrikleri kullanılmadı. |
| §3 | Embedded .NET control plane doğru konum; tek agent için fazla; Python/hosted runtime değil | Korunur. Generic runtime/SaaS/microservice yönü çıkarılmadı. Persona bazlı öncelik §8. |
| §4.1 | MAF wrap yok ifadesi ile decorator gerçekliği | Paralel domain type yok; `TimeoutAIFunction` ve diğer decorators gerçek wrapper. Dokümanda “tipleri değiştirmez” daha kesin ifade; MAF wrapper'larını kaldırma işi yok. |
| §4.2–3 | Çok store contract'ı custom backend maliyetini büyütür; TryAdd ve fail-open/closed ayrımı | Contract paketinde store/provider/job/tool aileleri var. A02 composition, A10 kritik kanıt. Bir store değiştirmenin bütün backend'i değiştirmediği rehberde kalmalı. |
| §5 | 20 paket, TFM/AOT istisnaları, Google dependency ağırlığı; API diff behavior uyumu değildir | Proje metadata/TFM kaynakları ölçüldü; A01/A14 sürüm ve behavior kanıtı. Tüm ürün için AOT veya production rozeti önerilmedi. Bu tur yeni restore bağımlılık sayısı verilmedi. |
| §5.2 | Aynı DbDataSource/pool, aynı transaction değildir; public API dosyası breaking change'i engellemez | Doğru sınırlar korunur. B03 ve A10'da transaction garantisi varsayılmadı. |
| §6.1 | Deploy'suz prompt değişimi review'suz değişim olmamalı; version resolved config değildir | B01, A05 stale edit/provenance. Code definition önceliği ve read-only davranışı değiştirilmez. |
| §6.2 | Model catalog allowlist değil; unknown model çalışabilir; fallback ledger global dedup değil | A02 policy açıklığı, A03 operation sınırı, A08 unknown price. Kataloğu zorunlu model allowlist'e çevirmek önerilmedi. |
| §6.3 | Singleton tool state, turn-local concurrency, effect etiketi izin değildir; retry/timeout/output boyutu | A03/A06; host thread-safety, scoped tool ve domain authorization rehberi korunur. Output limit ve retry contract'ının production örneğinde açık seçilmesi gerekir; effect metadata'yı otomatik yetki sayma. |
| §6.4 | History/compaction/memory/RAG ayrı; embedding dimension değişimi migration ister | A15 provenance/ACL/embedding migration; D14 scripts sınırı. Compaction'ın durable run evidence'ını sildiği çıkarılmadı. |
| §6.5 | Schema request ≠ tam validation; session repair bastırılır; streaming geri alınamaz | D10/A06. Attachment boyut/magic-byte malware taraması değildir; voice latency/cost ve node affinity A08/A16 kapsam notu. |
| §7.1 | SQL provider değişimi veri taşıma değil; migrations default açık; read views tenant filtresiz | A01 release/migration runbook, A16 deployment örneği. View doğrudan SQL tüketicisinin tenant filtresi sorumluluğu korunur; ORM/RLS zorunluluğu çıkmaz. |
| §7.2–3 | Event sequence ≠ durable log; sink outbox değil; SSE resume yüzeyleri farklı; replay deterministik değil | A10/A14. Client-side tool replay sınırı mevcut kayıtla korunur; F-198'e bağlanır. |
| §7.4 | Session optimistic conflict tekrarında side effect; branch copy; envelope teşhis sağlar | A03/A04. Session concurrency kusuru iddia edilmedi; client'ın kör retry yapmaması gerekir. In-memory branch ve UI branch noktası farkı belgeli capability sınırı. |
| §8.1 | Beş pattern generic durable business workflow değil; function node/checkpoint replay side effect | A03/A04/A17. Checkpoint mevcut diye deterministic saga garantisi verilmez. |
| §8.2 | Worker default, per-process slots, attempts, cron/DST, disabled manual trigger, schedule delete ≠ job cancel | D06 ve A12. Bunlar ayrı işletim contract'ları; cron'u genişletme işi türetilmedi. Lane coverage ve Completed/partial items görünürlüğü A12. |
| §8.3 | Async attachment/initial approval sınırı; karar sonrası resume belirsiz | D13/B03. Async body özelliklerini otomatik genişletmek yerine mevcut destek matrisi A14. |
| §8.4 | Üç route idempotency; raw-body fingerprint; crash reservation; exception sonrası tekrar | A03. JSON semantic canonicalization talebi kanıtlanmadı; aynı serialized body/key korunmalı. HTTP replay external effect transaction'ı değildir. |
| §8.5 | Live/queued cancel farkı; false orphan; continuation/drain/singleton lease opt-in | A07/A12/A16. Failed run dış etki durdu demek değildir. Node kill ve DB partition senaryosu gerekir. |
| §9.1–2 | Multi-turn promotion sınırı; ExpectedTools otomatik assertion değil; ilk baseline yok; retention partial diff | A09 ve XML kusuru. CLI gate zaten var. İlk-run bootstrap gate ile regression kanıtı ayrılmalı; mevcut Added/Removed ve retention conflict davranışı korunur. |
| §9.3 | Fake provider SQL/provider quality/load kanıtı değil | A16. Test varlığı ile bu tur test başarısı ayrıldı. |
| §9.4 | Online summary reset; kalıcı skor otoriter; management-only A/B; otomatik winner yok | Kalıcı score trend zaten Faz 154. A18 ve A14. Yeni “score history” adayı çıkarılmadı. |
| §10.1–2 | Role ∩ scope; claim/header güveni; tenant ≠ user ownership; async identity | A02; D05/D08; gerçek deny matrisi. Tenant içi bütün run verisi default user-isolated diye pazarlanmaz. |
| §10.3 | Standing rule expiry yok, presenter TOCTOU çözmez, audit failure davranışı farklı | A11/B03/D13. Domain check ve initiator/approver ayrımı korunur. |
| §10.4 | Pattern guard DLP/prompt-injection çözümü değil; Source.Unknown gevşetilmemeli | A02 güven sınırı/negative tests ve reddedilen garantiler. Bu tur yeni guard motoru gerekçesi çıkmadı. |
| §10.5 | Local rate limit, approximate quota, turn-tree budget, provider concurrency farklı | A08. Kesin para tavanı iddiası reddedildi; mevcut kota yanlış implementation diye sınıflanmadı. |
| §10.6 | BYOK reference, egress; hash-chain rewriting; lazy rotation; export/erasure resolver; retention | A10/A13. Host backup/key lifecycle ve resolver kapsamı runbook'ta açık olmalı; otomatik compliance sertifikası çıkarılmaz. |
| §11.1–2 | OpenAPI optional route; OpenAI model=agent adı; tam pass-through değil | D01/D03/D08/A14. Parser'ın alan kabul etmesi execution garantisi sayılmaz. |
| §11.3 | MCP discovery/restart token/stdio yok; A2A names frozen; approval-exposure sınırı | D09/A13/A14. Stdio veya A2A push ekleme talebi çıkmadı. Code tool isim önceliği korunur. |
| §11.4–5 | Generated client SSE/media sınırı; widget untrusted results ve admin key; UI field loss | B01/A14/F-198. BFF/host auth veya dar credential örneği korunur; kalıcı admin key browser'a yerleştirilmez. |
| §11.6 | Trigger HMAC native Slack imzası değil; webhook retry exactly-once değil | Entegrasyon sözleşmesi rehberde açık tutulur. Yeni Slack adapter'ı talep yokken faza çevrilmez; A03 dedup ilkesi. |
| §12.1 | Cost snapshot billing ledger değil; unknown price sıfır değil; job metrics latency tanımı | A08/A12/A16. Currency/tax/invoice platformu önerilmedi. |
| §12.2–3 | API/worker ayrımı, migration/readiness/liveness, reverse proxy auth, keys/retention/drain | A02/A04/A07/A13/A16. Reconciliation açmak veya SQL eklemek bütün işletim garantilerini açmaz. |
| §13 | Public kanıt, Core genişliği, opt-in composition, ingress asimetrisi, external effect atomik değil | B01–B03 ve A01–A16. Core'u paketlere bölmek için ölçülmüş dependency/performance gerekçesi yok. Module ownership/dependency test görünürlüğü desteklenir. |
| §14.3 | Önceki analizin on düzeltmesi | Yukarıdaki ilgili satırlarda korundu. Girdi raporunun eski snapshot sayısı güncel diye tekrarlanmadı; decorators, üç idempotency route, opt-in recovery, quota/A/B/security/AOT sınırları dar tutuldu. |
| §15 | 18 öneri ve beş önerilmeyen iş | §5 A01–A18 ve §6.2'de tamamı karara bağlandı; alt talepler ayrıldı. |
| §16 | 1.0 minimum kapı, dört aşamalı sıra ve read-only pilot | §8 yeniden önceliklendirme. Release state'e bağlı gerçek proof; tüm ileri yetenekleri zorunlu yapma reddi. |
| §17 | Kaynak/snapshot ve yapılmayan testler | §1–2. Canlı yeniden ölçüm yeni snapshot olarak kaydedildi; eski hash'ler değiştirilmedi. |

## 8. Alınması önerilen aksiyon sırası ve release yaklaşımı

| Sıra | Aksiyon | Kanal | Tamamlanma kanıtı |
|---|---|---|---|
| 1 | B01 definition alan kaybı | Kusur giderme | API/UI gerçek edit round-trip; mapper, nested model, vector-only memory ve server-owned alan sınırı. |
| 2 | B02 kritik prose: yayın durumu, tenant/worker default, OpenAI scope, approval resume; sonra D tablosu ve XML | Kusur giderme | Kaynak düzeltmesi + generated site/LLM/paket metni kontrolü; gerçek kod contract'ıyla karşılaştırma. |
| 3 | B03 approval decide→enqueue pencere ölçümü | Kusur doğrulama; doğrulanırsa giderme | Failure injection, tekrar request ve recovery sonucu. Yeni faz beklemez. |
| 4 | Mevcut F-171'in kapsamı: tüm tekrarlar + doğru screen tanımı + davranış iddiaları | Mevcut aday → seçilirse faz-planlama | Yanlış sayı/default/scope örneklerinde gate kırmızı; doğru yüzeylerde yeşil. |
| 5 | A04 dar state corpus/preflight ve A16 iki process failure/deployment kanıtı | Yeni aday önerisi | Restore corpus, DB outage/kill/lease ve açık operating procedure. |
| 6 | A02 profile composition ve A14 ingress matrisi | Yeni aday önerisi; F-198 ile ilişki | Gerçek deny/allow + supported/unsupported testleri; magic profile yok. |
| 7 | A07 distributed cancel, A12 queue diagnostics | Çok node hedeflenirse aday | Owner dışındaki node'dan intent/ack/terminal ayrımı ve orphan lane teşhisi. |
| 8 | A05 concurrency/provenance ve A09 eval revisions | Public/state contract kararı | Stale edit conflict; revision-aware comparison. A09 önce K-716 seçimi. |
| 9 | A03/A06/A08/A10/A11/A13/A15'in dar dilimleri | Somut kritik kullanım gereksinimine bağlı | Domain effect, schema/deadline, bütçe, audit/token/RAG sınırlarına özel kabul testi. |
| 10 | A17/A18 geniş ürün yüzeyleri | Ertele | Mevcut yetenekle çözülemeyen tüketici vakası ve dependency/işletim maliyeti ölçümü. |

**1.0 konusunda değerlendirme:** Bütün yeni özelliklerin bitmesi şart değildir. Doğru doküman, configuration integrity, tanımlı güven sınırlarının çalışması, sürüm/upgrade politikası ve tekrar üretilebilir artifact zorunlu kalite beklentisidir. Sert bütçe, ödeme exactly-once veya generic workflow gibi kapsam genişlemeleri için önce desteklenecek kullanım seçilir. Bu tur 1.0 yayın onayı verilmedi.

**Pilot önerisinin disposition'ı:** Read-only veya downstream idempotency'si kanıtlı bir domain agent, gerçek tenant/user binding, SQL, generated tool, recording ve eval ile denenebilir. İki node gerekiyorsa cancel/lease kill senaryosu eklenir. UI prompt edit/version rollback ve DB outage gözlemi pilotun parçasıdır. Pilot tamamlanmış sayılmadı; yeni çalıştırma veya deployment yapılmadı.

## 9. Yeniden doğrulama için kaynak envanteri

Bütün yollar repository köküne görelidir. Satırlar ölçüm commit'ine aittir; sonraki değişiklikte sembol adıyla aranmalıdır.

| Kanıt | Kaynak |
|---|---|
| UI mapper ve dar state | `src/AgentPrism.UI/frontend/src/screens/agent-editor/model.ts:146` · `use-agent-editor.ts:110,158` |
| HTTP definition dönüşümü/update | `src/AgentPrism.AspNetCore/Contracts/AgentContracts.cs:12,70` · `Endpoints/AgentEndpoints.cs:599,643` |
| Tam definition/model/memory alanları | `src/AgentPrism.Abstractions/Agents/AgentDefinition.cs:84,96,133,145,167` · `ModelBinding.cs:68,125,138` · `MemorySettings.cs:39` |
| Payload yazımı ve eksik alanlar | `src/AgentPrism.Sql.Shared/Stores/SqlAgentDefinitionStore.cs:111` · `src/AgentPrism.Sql.Shared/Internal/AgentDefinitionPayload.cs:68,97` · `src/AgentPrism.Core/Storage/InMemoryAgentDefinitionStore.cs:112` |
| Default worker/tenant | `src/AgentPrism.Abstractions/Scheduling/AgentPrismSchedulingOptions.cs:14,22` · `src/AgentPrism.AspNetCore/Tenancy/AgentPrismTenancyOptions.cs:47` |
| OpenAI scope | `src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs:81` · `OpenAIChatCompletionsEndpoints.cs:53` · `OpenAIConversationsEndpoints.cs:53–94` |
| Approval karar/handoff | `src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs:203` · `src/AgentPrism.Core/Approvals/ApprovalResumeJobHandler.cs:26` |
| Run recording / local cancellation | `src/AgentPrism.Core/Recording/RunEventWriter.cs:156` · `RunCancellationRegistry.cs:8` |
| Tool wait ve default validator | `src/AgentPrism.Core/Tools/TimeoutAIFunction.cs:57` · `NoOpToolArgumentsValidator.cs:23` |
| Structured syntax/custom/repair | `src/AgentPrism.Core/Compilation/StructuredResponseValidatingAgent.cs:145` · `src/AgentPrism.Core/AgentPrismStructuredResponseOptions.cs:20,28` |
| Turn-local fallback replay | `src/AgentPrism.Core/Models/FallbackChatClient.cs:115` |
| Approximate quota / run tree | `src/AgentPrism.Core/Quotas/QuotaEnforcer.cs:46,131` · `src/AgentPrism.Core/Models/RunBudgetChatClient.cs` |
| State restore ve stamp | `src/AgentPrism.Core/Sessions/AgentSessionManager.cs:188` · `src/AgentPrism.Workflows/Internal/AgentPrismCheckpointStore.cs:83` · `WorkflowRunner.cs:946` |
| Eval ID/snapshot sınırı | `src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs:364` · `src/AgentPrism.Sql.Shared/Stores/SqlEvalStore.cs:130` · `src/AgentPrism.Core/Evaluation/InMemoryEvalStore.cs:122` |
| Eval hizalaması / tool check | `src/AgentPrism.Abstractions/Evaluation/EvalRunDiffBuilder.cs:195` · `EvalCaseResult.cs:20` · `src/AgentPrism.Core/Evaluation/EvalCheckRegistry.cs:145` |
| Profile ve authorization coverage sınırı | `src/AgentPrism.Core/Diagnostics/RequiredBindingValidator.cs:130` · `tests/AgentPrism.Core.UnitTests/Architecture/RunAuthorizationCoverageTests.cs:24` |
| OAuth cache ve content key | `src/AgentPrism.Mcp/Internal/McpOAuthTokenCacheRegistry.cs:17` · `src/AgentPrism.Core/Security/AesGcmContentProtector.cs:74,87` |
| Rule ve queue mevcut alanları | `src/AgentPrism.Abstractions/Approvals/ToolApprovalRule.cs:29` · `src/AgentPrism.Abstractions/Scheduling/JobRecord.cs:49` |
| Protocol metadata | `src/AgentPrism.AspNetCore/Contracts/AgentPrismMetaResponse.cs:14` |
| Knowledge source/query | `src/AgentPrism.Abstractions/Knowledge/VectorSearchHit.cs:7` · `VectorSearchRequest.cs:7` · `src/AgentPrism.Core/Knowledge/VectorSearchToolFactory.cs:59` |
| Release manifest / identity | `scripts/kapi.py:870,900,1146` · `scripts/kapi_test.py:692,717` · `.github/workflows/ci.yml:261,313,358,426` |
| License/source/security | `LICENSE:1` · `SECURITY.md:5,20` · `src/Directory.Build.props:42,61` |
| Site gate ve revision | `docs-site/scripts/check-content.mjs:94,107,438,452` · `build-agent-map.mjs:97` · `build-changelog.mjs:45` |
| TS normalize/client tests | `packages/agentprism-client/scripts/generate.mjs:50` · `packages/agentprism-client/scripts/strip-prefix.mjs:20` · aynı paketin `test/prefix.test.ts` ve `test/paths-coverage.test.ts` dosyaları |
| Mevcut bench | `bench/AgentPrism.Benchmarks/RunEventWriterBenchmarks.cs:20` · `CompiledAgentCacheBenchmarks.cs` · `RunStoreQueryBenchmarks.cs` |

### Tekrar çalıştırılabilen küçük ölçümler

```bash
node docs-site/scripts/check-content.mjs
python3 scripts/dokuman-bakim.py --denetle
```

Client testleri `packages/agentprism-client` dizininde:

```bash
npm exec -- vitest run test/prefix.test.ts test/paths-coverage.test.ts
```

Manifest testleri `scripts` dizininde:

```bash
python3 -m unittest \
  kapi_test.YayinTestleri.test_manifest_her_paket_icin_id_dosya_ve_sha256_tasir \
  kapi_test.YayinTestleri.test_farkli_icerik_koşumu_durdurur_ve_mevcut_artifacti_korur
```

UI mapper probu repository kökünde gerçek source'u transpile eder; dosya değiştirmez:

```javascript
// node --input-type=module ile stdin'den çalıştırılır.
import fs from 'node:fs';
import ts from './src/AgentPrism.UI/frontend/node_modules/typescript/lib/typescript.js';
const source = fs.readFileSync(
  'src/AgentPrism.UI/frontend/src/screens/agent-editor/model.ts', 'utf8');
const js = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { emptyForm, toRequest } = await import(
  'data:text/javascript;base64,' + Buffer.from(js).toString('base64'));
const request = toRequest({ ...emptyForm, name: 'review-agent', provider: 'openai',
  model: 'configured-model',
  memory: { enableVectorSearch: true, vectorCollection: 'internal-documents' } });
console.log(request.memory); // Ölçülen mevcut sonuç: null
console.log(Object.hasOwn(request, 'parameters')); // false
console.log(Object.hasOwn(request, 'sharedInstructionsName')); // false
```

## 10. Ekosistem kontrolü ve kapanış

**Bakılan tarih: 2026-09-07.** [MCP HTTP authorization specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization) kontrol edildi. Token handling, audience ve authorization discovery protokol sorumluluklarıdır; bu kaynak AgentPrism DB'de token saklamayı zorunlu kılmaz. A13 için host-owned storage seçeneği protokol gereğiyle çelişmeyen bir tasarım yönüdür; bu bir SDK implementasyon seçimi değildir.

Canlı proje ölçümleri: [LLM index](https://agentprism.doayen.web.tr/llms.txt), [tam metin](https://agentprism.doayen.web.tr/llms-full.txt), [OpenAPI](https://agentprism.doayen.web.tr/openapi/agentprism.json), [NuGet meta index](https://api.nuget.org/v3-flatcontainer/agentprism/index.json), [NuGet Core index](https://api.nuget.org/v3-flatcontainer/agentprism.core/index.json), [npm index](https://registry.npmjs.org/@agentprism%2fclient). 404 bağlantıları ölçüm adresidir; yayımlanmış artifact bağlantısı gibi okunmaz.

“MAF artık bu boşluğu kapatıyor”, “rakiplerin hiçbiri yapmıyor” veya yeni SDK imzası iddiası üretilmedi. Bu nedenle yeni MAF tipi kullanımı/reflection keşfi yapılmadı. A17/A18 gibi ertelenen geniş fikirler için ekosistem üstünlüğü kanıtı yok; seçilirlerse ayrı güncel SDK/restore ölçümü gerekir.

**Üç kanalın durumu:** B01/B02 kusurları ve B03 failure penceresi raporlandı; giderme uygulanmadı. Yeni aday önerileri §5'te; onaylanmış F kaydı yok. K-716 yeniden kapsam seçimi gerektiriyor; K-059 korunuyor. Kararlar kullanıcı adına değiştirilmedi.

**Kullanıcı girdisi:** Yayınlanmamış geliştirme sürümü olduğu netleşti ve sınıflandırma buna göre değiştirildi. Kalan seçimler raporu tamamlamak için engel değil; hangi adayın uygulanacağına ilişkin sonraki ürün kararlarıdır.

**Kapanış doğrulaması:** `python3 scripts/dokuman-bakim.py` exit 0; kırık bağlantı 0, generated indekslerde değişiklik yok. Raporun A01–A18 ve D01–D18 kimlikleri eksiksiz ve tekrarsız kontrol edildi. Bütçeler içinde; önceden var olan 24 dar bütçe bildirimi sürüyor. Tam faz kapanış kapıları koşulmadı: bu tur bir faz implementasyonu değil, rapor üretimidir.
