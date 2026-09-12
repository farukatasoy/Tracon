# Keşif Turu — 2026-08-23 · Yeni feature fikirleri

> Bu bir **koşum kaydıdır**, spec değildir. Sıralanan fikirler kullanıcı
> tarafından seçilmediği için `docs/ADAYLAR.md` dosyasına F numarası eklenmedi.

**Tetikleyen:** Kullanıcının projeye 10 yeni feature fikri istemesi.
**Zemin:** Faz 89 kapalı · `docs/ADAYLAR.md` içinde 16 başlık · ölçülen en büyük numara F-145.
**Ekosistem taraması:** 2026-08-23 · web erişimi var.

## 1. Ölçülen zemin

| Kaynak | Bulgu |
|---|---|
| Son fazların devir notu | Faz 89, tool wrapper zincirinin dört halkaya çıktığını ve yeni halkaların iki registry yoluna taşınması gerektiğini belirtiyor. Faz 87, skill/alt-agent replay boşluğunu, diğer workflow desenlerinde retry eksikliğini ve hosted-service lifecycle entegrasyon testi eksikliğini bırakıyor. |
| Aday dosyası | Stratejik boşlukların çoğu zaten plana dönüşmüş. Kalan işler ağırlıkla derinleşme, entegrasyon ve işletim kalemleri. ACS (F-72) hâlâ ertelenmiş durumda. |
| Kod | A2A agent card açıkça streaming ve push notification desteğinin olmadığını beyan ediyor (`src/Tracon.AspNetCore/A2A/TraconA2AExtensions.cs:158`). `WorkflowNodeRetryPolicy` yalnız `AddWorkflowFunction` yüzeyine bağlı (`src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs:88-123`). |
| Kod | `WebhookDeliveryStatus.Failed` ve teslimat geçmişi var; yönetim yüzeyinde test gönderme ve geçmiş listeleme var, başarısız teslimatı yeniden sürme ucu yok (`src/Tracon.Abstractions/Webhooks/WebhookTypes.cs:11-23`, `src/Tracon.AspNetCore/Endpoints/WebhookEndpoints.cs:74-96`). |
| Kod | `ModelDescriptor` streaming, tool, reasoning ve structured output yeteneklerini taşıyor; compiler ölçümle yalnız structured output kontrol ediyor (`src/Tracon.Abstractions/Models/ModelDescriptor.cs:4-28`, `src/Tracon.Core/Compilation/AgentDefinitionCompiler.cs:763-789`). |
| Kod | RAG sonucu `SourceId`, `ChunkIndex`, içerik ve distance taşıyor. Agent yanıtında source lineage/citation sözleşmesi yok (`src/Tracon.Abstractions/Knowledge/VectorSearchHit.cs:4-19`, `src/Tracon.Core/Knowledge/VectorSearchToolFactory.cs:68-70`). |
| Kod | Tool approval evaluator kararı `bool` olarak döndürüyor. Eşleşen policy/rule nedenini simüle eden bir yönetim ucu görünmüyor (`src/Tracon.Core/Approvals/ToolApprovalRuleEvaluator.cs:63-115`). |

## 2. Ham fikir listesi

İstenen 10 fikir için 12 ham kalem üretildi. İlk 10 kalem aşağıdaki önerilen
çıktıdır; son iki kalem eleme kontrolüdür.

| # | Fikir | Kim için | Neden şimdi | Sonuç |
|---|---|---|---|---|
| 1 | MCP Tasks ile uzun süren tool/agent çağrılarının poll, result ve cancel yaşam döngüsü | Tracon'i MCP server olarak tüketen ekip | MCP Tasks 2025-11-25'te tanımlandı; Tracon'de queued run ve approval altyapısı zaten var | ✅ |
| 2 | MCP elicitation → Tracon approval/input mailbox köprüsü | Harici MCP tool'ları insan girdisi isteyen ekipler | MCP server artık kullanıcıdan form veya URL tabanlı ek bilgi isteyebiliyor | ✅ |
| 3 | MCP `skill://` kaynaklarını güvenli, read-only context provider olarak kullanma | Kurumsal ortak skill kataloğu kullanan ekipler | MAF, MCP üzerinden skill discovery ve on-demand içerik alma yüzeyi yayımladı | ✅ |
| 4 | A2A için SQL-backed task store ve push notification/SSE desteği | Tracon agent'ını başka agent'lara açan ekipler | Mevcut Agent Card push ve streaming'i unsupported ilan ediyor; A2A hosting dokümanı in-memory task store'u production için uygun görmüyor | ✅ |
| 5 | Graph-wide workflow retry ve compensation policy | MAF workflow kullanan üretim ekipleri | Retry bugün yalnız `AddWorkflowFunction` callback'inde; diğer workflow desenleri için ortak policy yok | ✅ |
| 6 | Webhook dead-letter ve güvenli redrive merkezi | Nöbetçi mühendis ve platform ekibi | Failed teslimatlar listelenebiliyor ama operator seçip yeniden sürme ve yeni delivery lineage'ı yok | ✅ |
| 7 | Model capability-aware compile ve fallback seçimi | Birden çok provider/model kullanan ekipler | Capability metadata zaten mevcut; tool/streaming/reasoning uyumsuzluğu çalışma anına kalabiliyor | ✅ |
| 8 | RAG source lineage ve citation contract | Bilgi asistanı ve regüle domain ekipleri | Retrieval sonucu source kimliği taşıyor; final yanıtın hangi source'a dayandığını zorunlu kılan yüzey yok | ✅ |
| 9 | Tool policy explain/simulate endpoint'i | Güvenlik yöneticisi ve uygulama geliştiricisi | Approval kararı var; production'da gerçek tool çalıştırmadan “hangi rule neden eşleşti?” sorusu yanıtlanamıyor | ✅ |
| 10 | GenAI OpenTelemetry semantic convention conformance modu | OTel/Langfuse/Jaeger/Datadog kullanan işletim ekipleri | MEAI artık chat, embedding, image, speech ve realtime için GenAI semantic-convention client'ları sunuyor; Tracon'in özel span sözleşmesiyle uyum katmanı gerekiyor | ✅ |
| 11 | MCP resource subscription/change notification desteği | Sürekli değişen uzak kaynakları kullanan ekipler | Düşük ilk değer; poll ve cache invalidation davranışı ayrıca karmaşık | ❌ elendi |
| 12 | Provider warm-up ve per-model smoke-test matrisi | Platform operasyon ekibi | Sağlık kontrolü zaten var; yeni feature yerine mevcut health yüzeyinin derinleştirilmesi olur | ❌ elendi |

**Önerilen üç kalem ve gerekçesi:**

1. **A2A SQL-backed task store + push:** mevcut dış tüketici yüzeyindeki açık,
   doğrudan ölçülmüş ve kurumsal entegrasyon değerine sahip.
2. **Model capability-aware compile/fallback:** mevcut metadata'yı kullanır;
   yeni model listesi veya provider wrapper'ı istemeden runtime sürprizlerini
   azaltır.
3. **Webhook dead-letter/redrive:** gece 03:00 operasyon acısını doğrudan
   çözer ve mevcut queue/history sözleşmesinin doğal devamıdır.

**Kullanıcının elemesi:** Henüz alınmadı. Kullanıcı ilk 10 fikirden seçebilir.

## 3. Ekosistem taraması

| Kaynak | Bakılan tarih | Ne değişti | Tracon'e etkisi |
|---|---|---|---|
| [MCP Tasks specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks) | 2026-08-23 | `tools/call` için task augmentation, `tasks/get`, `tasks/result`, `tasks/cancel`, TTL ve optional status notification tanımlanıyor. Özellik experimental. | Tracon'in queued run/approval state'i MCP task state'e bağlanabilir; task ID tenant/auth context'e sıkı bağlanmalı. |
| [MCP Elicitation specification](https://modelcontextprotocol.io/specification/2025-11-25/client/elicitation) | 2026-08-23 | Server'ın kullanıcıdan form veya URL mode ile ek bilgi istemesi ve güvenlik kuralları tanımlanıyor. | Mevcut approval/input mailbox için standart dış protokol köprüsü adayıdır. Secret/API key form üzerinden istenmemeli. |
| [MAF Agent Skills](https://learn.microsoft.com/en-us/agent-framework/agents/skills) | 2026-08-23 | MCP server `skill://index.json` ile skill keşfi ve içeriklerin on-demand alınmasını destekliyor. API experimental; archive script'leri çalıştırılmıyor. | Tracon'in kendi skill-script güvenlik sınırını gevşetmeden read-only remote skill context desteği üretilebilir. |
| [MAF A2A hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/agent-to-agent) | 2026-08-23 | A2A hosting'de `ITaskStore` ve session store replace edilebilir; varsayılan in-memory store production için uygun değil. | Tracon SQL run/job store'u durable A2A task store'a bağlamak doğal entegrasyondur. |
| [MAF durable extension](https://learn.microsoft.com/en-us/agent-framework/integrations/durable-extension) | 2026-08-23 | Durable agents/workflows için checkpoint, resume, human-in-the-loop ve distributed worker yetenekleri sunuluyor. | Workflow-wide retry/compensation ve A2A task adaylarında MAF'ın kapalı-kutu sınırları yeniden ölçülmelidir; Tracon MAF tiplerini paralel sarmalamamalı. |
| [Microsoft.Extensions.AI namespace](https://learn.microsoft.com/en-us/DOTNET/api/microsoft.extensions.ai?view=netstandard-2.0-pp) | 2026-08-23 | OpenTelemetry semantic-convention decorators chat, embedding, image, speech ve realtime client'ları için mevcut. | Tracon'in OTel yüzeyi özel span'larla standardı birlikte taşıyacak bir conformance seçeneği kazanabilir. |

## 4. Derinleşen 10 kalem

### 1. MCP Tasks ile asynchronous agent/tool execution

**Kanıt seviyesi:** Ölçüldü — mevcut MCP server mapping'i var; task capability
ve task lifecycle kodu için `rg -n "task|tasks" src/Tracon.AspNetCore/McpServer
src/Tracon.Mcp` yeni bir Tracon task yüzeyi göstermedi. Ekosistem kanıtı
2026-08-23 tarihli MCP Tasks specification.
**Mercek:** 1, 2, 3, 6.
**Eleyici sınır kontrolü:** K2 uyumlu; K3 uyumlu; yeni NuGet paketi gerekmeyebilir;
public MCP contract ve experimental protocol riski vardır; AOT etkisi düşük.
**Karşı görüş:** MCP Tasks experimental olduğu için Tracon bugün değişken bir
wire contract'ı public hale getirebilir.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 2. MCP elicitation bridge

**Kanıt seviyesi:** Ölçüldü — mevcut remote MCP client/prompt/resource yüzeyleri
var; elicitation bridge görünmüyor. Ekosistem kanıtı 2026-08-23 MCP
elicitation specification.
**Mercek:** 1, 2, 3, 6.
**Eleyici sınır kontrolü:** Tool kodu üretmez; secret'ı form mode ile isteme sınırı
zorunludur; public interaction state büyür; MCP API imzaları uygulama öncesi
`maf-api-kesfi`/reflection ile doğrulanmalı.
**Karşı görüş:** Approval, workflow input ve elicitation üç ayrı state modeline
ayrılırsa ürün karmaşıklaşır; yalnız gerçekten dış MCP server'ların elicitation
gönderdiği kanıtlanırsa alınmalı.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 3. MCP remote skills as read-only context

**Kanıt seviyesi:** Ölçüldü — Tracon'de MCP prompt/resource client'ları var;
MAF'ın MCP skill source'u 2026-08-23'te dokümante edilmiş durumda.
**Mercek:** 1, 3, 6, 7.
**Eleyici sınır kontrolü:** K2 korunur: uzak skill içeriği kod çalıştırmaz; archive
script'leri execute edilmez; ayrı paket ihtiyacı ve experimental API riski vardır.
**Karşı görüş:** Tracon'in mevcut code-defined skill modelini ikinci bir
skill kaynağıyla bölmek discovery, versioning ve tenant isolation maliyeti yaratır.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 4. A2A durable task store + push/SSE

**Kanıt seviyesi:** Ölçüldü — `TraconA2AExtensions.cs:158-159` streaming ve
push notification'ı unsupported ilan ediyor. MAF A2A hosting dokümanı
`InMemoryTaskStore`'u development-only olarak tarif ediyor.
**Mercek:** 1, 2, 3, 6.
**Eleyici sınır kontrolü:** K3 korunur; mevcut `A2A.AspNetCore`/MAF bağımlılığı
üzerinden ilerleyebilir; public A2A task/push contract büyür; SQL migration ve
tenant/auth correlation gerekir.
**Karşı görüş:** Push callback güvenilirliği yeni bir webhook benzeri teslimat
problemi açar. İlk sürümde SQL task store + polling olmadan push eklemek daha
doğru olabilir.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 5. Graph-wide workflow retry ve compensation

**Kanıt seviyesi:** Ölçüldü — retry parametresi `AddWorkflowFunction` ile sınırlı;
Faz 87 devir notu diğer workflow desenleri için boşluk bırakıyor.
**Mercek:** 2, 3, 5, 6.
**Eleyici sınır kontrolü:** MAF workflow executor imzaları önce `maf-api-kesfi` ile
doğrulanmalı; agent/tool dış etkilerinde retry idempotency ve `SafeToRepeat`
zorunlu; yeni public policy contract riski yüksek.
**Karşı görüş:** Genel retry, external/destructive tool çağrılarını çoğaltabilir.
Compensation ve retry yalnız executor boundary'sinde tanımlanmazsa güvenlik
riski üretir.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 6. Webhook dead-letter ve redrive

**Kanıt seviyesi:** Ölçüldü — `Failed` status ve history endpoint var; mevcut
endpoint'lerde failed delivery için redrive operation yok.
**Mercek:** 2, 3, 5, 7.
**Eleyici sınır kontrolü:** K2/K3/AOT etkisi yok; mevcut job queue kullanılabilir;
public admin API ve audit trail gerekir; aynı event'in yeniden gönderilmesi
idempotency contract'ı ister.
**Karşı görüş:** Redrive dış sistemde duplicate side effect oluşturabilir. Yeni
delivery id, original delivery id lineage'ı, operator reason ve explicit
confirmation olmadan bu feature güvenli değildir.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 7. Model capability-aware compile ve fallback

**Kanıt seviyesi:** Ölçüldü — capability alanları `ModelDescriptor`'da var;
compiler'ın doğrudan ölçülen kontrolü structured output ile sınırlı.
**Mercek:** 1, 2, 5, 6, 8.
**Eleyici sınır kontrolü:** Yeni model listesi yazmaz; AOT dostu; fallback ile
birleşirse mevcut model routing contract'ına dokunur; bilinmeyen model için
fail-open/fail-closed kararı gerekir.
**Karşı görüş:** Provider capability catalog'ları eksik veya bayat olabilir;
yanlış negatif agent'ı engeller, yanlış pozitif ise runtime hatasına geri döner.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 8. RAG source lineage ve citation contract

**Kanıt seviyesi:** Ölçüldü — retrieval hit source kimliği taşır; tool sonucu bu
bilgiyi döndürür; final assistant response için zorunlu citation contract'ı yok.
**Mercek:** 1, 3, 5, 7.
**Eleyici sınır kontrolü:** K2 uyumlu; AOT için typed response/metadata seçilmeli;
provider-specific citation parsing yapılmamalı; public response/event contract
gerekebilir.
**Karşı görüş:** Citation zorunluluğu modelin serbest yanıtını daraltır ve
grounding garantisi vermez. İlk aşama “source evidence available” işareti ve
eval check olabilir; otomatik citation doğruluğu ayrı iştir.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 9. Tool policy explain/simulate

**Kanıt seviyesi:** Ölçüldü — gerçek evaluator kararı üretim çağrısında bool'a
indirgeniyor; governance API'de gerçek tool çağırmadan eşleşme açıklayan bir
simülasyon yolu görünmüyor.
**Mercek:** 2, 3, 5, 7.
**Eleyici sınır kontrolü:** K2 korunur: simulation hiçbir tool body çalıştırmaz;
argument redaction zorunludur; policy'nin code-defined kısmı deterministik ve
side-effect-free olmalıdır; public admin contract büyür.
**Karşı görüş:** Arbitrary code-defined policy'nin “neden” açıklaması mümkün
olmayabilir. Feature yalnız data rule explainability ile başlatılmalı veya policy
contract'ına açıklama üretme zorunluluğu eklenmelidir.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

### 10. GenAI OpenTelemetry conformance mode

**Kanıt seviyesi:** Ölçüldü — Tracon kendi `Tracon` ActivitySource'unu
ve chat/run span'lerini üretiyor; MEAI 2026-08-23 itibarıyla GenAI semantic
convention decorator'larını birden fazla client tipi için sağlıyor.
**Mercek:** 2, 3, 4, 6, 7, 8.
**Eleyici sınır kontrolü:** Yeni collector yazılmaz; MEAI standardı kullanılır;
experimental semantic convention değişebilir; sensitive prompt/content
recording varsayılanı kapalı kalır; double-span ve attribute cardinality
ölçülmelidir.
**Karşı görüş:** Standardın kendisi experimental olduğu için Tracon public
API'sini doğrudan semconv alanlarına bağlamak erken olabilir. İlk adım public
contract değil, opt-in exporter/conformance test seti olmalıdır.
**Sonuç:** Kullanıcı seçimine bırakıldı; F numarası verilmedi.

## 5. Üç kanalın çıktısı

### Kanal 1 — yeni aday

| F-NN | Başlık | Aday dosyasına yazıldı mı |
|---|---|---|
| — | İlk 10 fikir | Hayır; kullanıcı seçimi bekleniyor |

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` koşuldu mu |
|---|---|---|---|
| Devir notundaki hosted-service lifecycle konusu test kapsamı boşluğudur; yeni feature değildir. | Faz 87 devir notu | Evet | Hayır; bu turda kusur koşulmadı |
| F-144 ve F-145 mevcut test boşluklarıdır; yeni feature olarak yeniden önerilmedi. | `docs/ADAYLAR.md` içindeki F-144/F-145 kayıtları | Evet | Hayır; bu turda kusur koşulmadı |

### Kanal 3 — yeniden açılması önerilen karar

| K-NNN | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| — | Bu taramada mevcut bir kararı geçersiz kılan ekosistem değişimi ölçülmedi. | MCP Tasks ve MAF A2A task store yeni adaydır; F-72 ACS kararını yeniden açmaz. | Gerek yok |

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| MCP resource subscription/change notification | İlk kullanım değeri düşük; mevcut resource/poll/cache yüzeyiyle birleştiğinde kapsam belirsiz. | Hayır, bu turda değil | Bu not |
| Provider warm-up ve smoke-test matrisi | Mevcut health endpoint'inin derinleşmesi; bağımsız feature eşiği düşük. | Hayır, bu turda değil | Bu not |

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| Hangi fikirler faz planına dönüştürülsün? | Henüz cevaplanmadı. |

