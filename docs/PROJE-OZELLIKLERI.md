# AgentPrism Özellik Envanteri

> Bu liste, mevcut kod tabanında doğrulanan özellikleri içerir. Planlanan özellikler dahil değildir.

## Agent ve model orchestration yetenekleri

- **Declarative agent tanımları:** Instructions, model binding, tools, skills, callable agents, metadata ve runtime policy kod veya store üzerinden tanımlanabilir.
- **Factory agent kaydı:** Tüketici, herhangi bir MAF `AIAgent` üreten factory'yi kataloğa doğrudan ekleyebilir.
- **Agent tanımı yaşam döngüsü:** Store tabanlı tanımlar oluşturulabilir, güncellenebilir, silinebilir, version'lanabilir, karşılaştırılabilir ve önceki version'a döndürülebilir.
- **Custom agent source:** `IAgentSource` ile repository veya harici runtime gibi ek katalog kaynakları bağlanabilir.
- **Source fault isolation:** Bir custom source hata verdiğinde diğer agent kaynakları çalışmaya ve listelenmeye devam eder.
- **Tanım doğrulama:** Provider, model, tool, skill, callable agent, cycle ve policy hataları model çağrısı yapılmadan denetlenir.
- **Model binding:** Provider, model, temperature, output limiti, `top_p`, reasoning effort ve provider-specific settings agent bazında ayarlanabilir.
- **Structured output:** Text, JSON ve JSON Schema response formatları provider yeteneğine göre doğrulanır ve uygulanır.
- **Structured response validation:** Opt-in `IStructuredResponseValidator`, tamamlanmış response'u run kapanmadan önce custom kurallarla fail-closed olarak denetler.
- **Structured response repair:** Reddedilen non-streaming response, `MaxRepairAttempts` ile sınırlı ek model turlarında onarılabilir; streaming ve durable session run'larında repair yapılmaz.
- **Parameterized instructions:** `{{name}}` placeholder'ları run'a ait doğrulanmış parameter değerleriyle bağlanır.
- **Culture-aware instructions:** Agent instructions, istek culture bilgisine göre uygun dil varyantından çözülebilir.
- **Shared instructions:** Bir agent, tek seviyeli ortak instructions bloğunu compile sırasında kendi instructions metninin önüne ekleyebilir.
- **Agent-to-agent çağrı grafiği:** Agent'lar kayıtlı başka agent'ları tool olarak çağırabilir; cycle denetimi ve child run ilişkileri korunur.
- **Alt-agent bekleme sınırı:** Çağrılan alt-agent iki katmanlı sınırla beklenir; cooperative `ChildDeadline` iptal token'ını okuyan çocuğu durdurur, hard `WaitTimeout` token'ı yok sayan çocuğu beklemeden düşürür ve zaman aşımı çağıran run'ın olay akışına yazılır.
- **Harness mode:** MAF harness üzerinden iteration ve context limitleri ile todo, file-memory, web-search, skill ve mode provider'ları kullanılabilir.
- **Context compaction:** Geçmiş, eşik tabanlı truncation veya utility model ile summarization yapılarak sıkıştırılabilir.
- **Response cache:** `IDistributedCache` ile tenant, provider, model, request ve tool setini dikkate alan response caching kullanılabilir.
- **Concurrent tool calls:** Aynı model turundaki bağımsız tool çağrıları, açıkça etkinleştirildiğinde paralel yürütülebilir.
- **Model fallback zinciri:** Sınıflandırılmış provider hatalarında tanımlı yedek provider ve modellere sırayla geçilir; ortak ledger tamamlanmış tool side effect'lerinin fallback sırasında yeniden çalışmasını önler.
- **Provider concurrency limiti:** Model çağrıları provider ve credential scope bazında sınırlandırılabilir.
- **Context preflight:** İstek context kullanımı run başlamadan tahmin edilir ve model penceresini aşan istek reddedilebilir.
- **Compiled agent cache:** Compile edilen agent'lar definition ve bağlı kaynak fingerprint'lerine göre cache'lenir ve değişiklikte yenilenir.

## Model provider ve media yetenekleri

- **OpenAI provider:** OpenAI Chat Completions ve Responses API yüzeyleri ayrı provider adlarıyla kullanılabilir.
- **OpenAI-compatible provider:** OpenRouter, Groq, vLLM, Ollama, LM Studio ve uyumlu özel endpoint'ler adlandırılmış provider olarak bağlanabilir.
- **Anthropic provider:** Claude modelleri resmi SDK üzerinden prompt caching ve extended thinking settings ile kullanılabilir.
- **Google provider:** Gemini modelleri safety threshold ve thinking budget settings ile kullanılabilir.
- **Azure OpenAI provider:** Deployment tabanlı model çözümleme, API key ve tüketicinin sağladığı Microsoft Entra credential desteklenir.
- **Custom model provider:** Tüketici, `IModelProvider` implementation'ını type, instance veya factory olarak kaydedebilir.
- **Configuration tabanlı model catalog:** Model adları, context pencereleri ve fiyatlar configuration'dan alınır; catalog allowlist olarak kullanılmaz.
- **Image generation:** OpenAI, Azure OpenAI veya Google image generator ile üretilen görseller doğrulanıp attachment olarak saklanabilir.
- **Speech tools:** ElevenLabs tabanlı `speak`, `transcribe` ve `list_voices` tool'ları ile custom speech implementation'ları desteklenir.
- **Voice provider metadata:** `VoiceDescriptor.Attributes`, provider'a özgü güvenli scalar bilgileri bounded biçimde voice catalog'a ve HTTP response'una taşır.
- **Live voice conversation:** WebSocket akışı transcription, durable agent session ve speech synthesis adımlarını uzun süreli bir konuşmada birleştirir.

## Tool, skill, MCP ve context yönetimi

- **Tool kayıt seçenekleri:** `AIFunction`, delegate, attributed type scanning ve source-generated kayıt yolları desteklenir.
- **Scoped tool çalıştırma:** `AddScopedTool`, her tool çağrısı için ayrı DI scope açar ve scope'u çağrı sonunda kapatır.
- **Build-time tool generation:** `[AgentPrismTool]` metotları reflection ve dynamic code olmadan compile sırasında keşfedilip kaydedilebilir.
- **Generated tool schema:** Generator, `[Description]`, standard DataAnnotations constraint'leri ve en fazla üç seviyeli object parameter graph'ını JSON Schema'ya aktarır.
- **Tool governance metadata:** Tool bazında effect, permission, approval, timeout, output limiti ve `SafeToRepeat` policy'si tanımlanabilir.
- **Tool authorization:** `IToolAuthorizationHandler`, mevcut caller ve run context için her tool çağrısını izin veya ret ile sonuçlandırabilir.
- **Tool argument validation:** `IToolArgumentsValidator`, code veya MCP tool gövdesinden önce argument'ları denetler ve ret durumunda gerçek çağrıyı çalıştırmaz.
- **Tool approval akışı:** Hassas çağrılar expiring pending approval oluşturur ve insan kararı sonrası queued run olarak devam eder.
- **Approval presentation:** `IToolApprovalPresenter`, pending approval'ın ham argument'larını best-effort biçimde kullanıcıya anlamlı entity adı ve mesajıyla zenginleştirebilir.
- **Standing approval rules:** Tool adı ve argument koşullarına bağlı, sonradan revoke edilebilen kalıcı approval kuralları tanımlanabilir.
- **Tool timeout ve output sınırı:** Uzun süren çağrılar iptal edilir; büyük sonuçlar model görmeden bounded JSON envelope içine kısaltılır.
- **Client-side tools:** Server yalnız tool declaration'ını yayınlar; tool gövdesini browser veya başka bir client çalıştırıp sonucu sonraki istekte geri verir.
- **Custom content guards:** Birden çok `IContentGuard` user message, tool result, document, model output ve skill resource kaynak bilgisiyle sırayla çalışır; en katı karar uygulanır.
- **Pattern content guard:** Denied term'ler block edilebilir ve seçili PII desenleri maskelenebilir.
- **Skill catalog:** Markdown instructions ve bounded resource içeren skill'ler koddan, store'dan veya file source'dan sağlanabilir.
- **Skill script execution:** Açık opt-in, interpreter allowlist, tenant grant, timeout, output limiti ve concurrency limitleriyle server-side script çalıştırılabilir.
- **Remote MCP tool discovery:** HTTP MCP server'larından tool'lar tenant bazında keşfedilir, normalize edilir, cache'lenir ve yenilenir.
- **MCP authentication ve OAuth:** Header tabanlı authentication, token cache ve kullanıcı etkileşimli OAuth authorization akışı desteklenir.
- **MCP prompts:** Uzak MCP prompt'ları listelenebilir, argument'larla çağrılabilir ve bounded snapshot olarak alınabilir.
- **MCP resources:** Seçili remote resource URI'ları okunup bounded agent context içine eklenebilir.
- **Knowledge ingestion ve search:** Belgeler chunk'lanıp embed edilir; PostgreSQL `pgvector` ile tenant-isolated cosine search yapılır.
- **Working memory:** Agent'lar todo state, tenant-prefixed file memory, text search ve vector search araçlarını kullanabilir.

## Run, session ve içerik yönetimi

- **Streaming run:** .NET ve HTTP yüzeyleri text, reasoning ve tool activity güncellemelerini akışlı olarak iletebilir.
- **Non-streaming run:** Tamamlanmış response tek HTTP sonucu olarak döner ve kayıt altına alınır.
- **Run recording:** Run özeti, status, event'ler, tool çağrıları, usage, cost, hata ve isteğe bağlı input varsayılan olarak kaydedilir.
- **Ordered event stream:** Run event'leri gapless sıra numarasıyla saklanır; SSE akışı live veya replay modunda `Last-Event-ID` ile sürdürülebilir.
- **Event frame sözleşmesi:** Her `RunEventType` üyesi kayıtlı akışta kararlı bir frame adıyla yayılır; sevk edilmiş adlar değişmez ve isimsiz üye bırakılması build kapısında hata olur.
- **Run tree:** Root ve child run ilişkileri saklanır; ağaç usage, cost ve child sayıları birlikte sorgulanabilir.
- **Run budget:** Depth, toplam run, token, cost ve wall-clock duration limitleri model turları arasında uygulanır.
- **Run cancellation:** Bir run kimliğiyle iptal istenebilir ve root run iptali aynı ağaçtaki etkin child run'lara yayılır.
- **Run replay:** Kaydedilmiş input seçili agent version'ıyla yeniden çalıştırılabilir; recorded tool playback ve mismatch guard seçenekleri vardır.
- **Run karşılaştırma:** İki run output, usage, cost, süre ve status verileri üzerinden karşılaştırılabilir.
- **Run feedback ve score:** Kullanıcı feedback'i veya custom/model judge score'u tamamlanmış run'a eklenebilir ve silinebilir.
- **Run error classification:** Hatalar override edilebilen classifier zinciriyle sınıflandırılır ve fingerprint ile failure cluster'larına ayrılır.
- **Run attribution:** Tüketicinin user, correlation ve job label bilgileri run kaydına aktarılabilir.
- **Custom run event'leri:** Tüketici kodu, doğrulanmış bir custom type ve payload ile mevcut run stream'ine kendi event'ini yazabilir.
- **Durable sessions:** Conversation kimliği ve MAF session state store üzerinden kaydedilir; session geçmişi okunabilir ve silinebilir.
- **Session optimistic concurrency:** Generation tabanlı compare-and-swap, stale session state yazımını reddeder ve HTTP yüzeyinde conflict sonucu üretir.
- **Session branching:** SQL store kullanan bir conversation, adreslenebilir bir geçmiş öğesinden yeni session olarak çatallanabilir.
- **Attachment yönetimi:** Image, audio, PDF ve text dosyaları size, media type ve magic-byte kontrolleriyle yüklenebilir, indirilebilir ve silinebilir.
- **Multimodal messages:** Text, image, audio ve document içerikleri MAF content tipleriyle provider'a iletilir.
- **Document channel:** Referans belgeleri instructions metninden ayrı message alanında taşınır ve run input ile birlikte kaydedilir.

## Workflow, job ve dayanıklılık yetenekleri

- **Multi-agent workflows:** Sequential, Concurrent, Handoff, GroupChat ve Magentic workflow pattern'leri MAF graph olarak çalıştırılabilir.
- **Workflow tanımları:** Workflow'lar koddan veya store tabanlı editor/API yüzeyinden tanımlanabilir ve graph doğrulamasından geçirilir.
- **Workflow functions:** Typed application fonksiyonları agent olmadan workflow node'u olarak kaydedilebilir.
- **Durable checkpoints:** SQL store ile workflow state kaydedilir ve process restart sonrası devam ettirilebilir.
- **Human-in-the-loop:** Bekleyen workflow request'leri listelenebilir; insan response'u checkpoint'ten yeni execution step başlatır.
- **Workflow node retry:** Transient provider hatası yalnız ilgili function node'unda backoff policy ile yeniden denenebilir.
- **Job queue:** Lease, retry, item status, cancellation ve handler dispatch içeren at-least-once job yürütme modeli sağlanır.
- **Custom job handler:** Tüketici, reserved namespace dışında açık bir string handler key ile `IJobHandler` implementation'ı kaydedebilir.
- **Programmatic job dispatch:** `IJobDispatcher`, yalnız kayıtlı handler key'leri için uygulama kodundan durable job queue'ya iş ekler.
- **Job queue lane'leri:** Worker'lar seçili lane'lere abone olabilir ve her lane için ayrı concurrency limitiyle unrelated background işleri izole edebilir.
- **Agent ve workflow jobs:** Tek agent, batch agent ve workflow çalıştırmaları ortak durable queue üzerinden yürütülebilir.
- **Schedules:** One-time ve cron schedule'lar açık time zone bilgisiyle job üretebilir ve elle tetiklenebilir.
- **Worker control:** Bir process job worker çalıştırabilir veya yalnız API node'u olarak yapılandırılabilir.
- **Async HTTP run:** `Prefer: respond-async` isteği run'ı queue'ya alır ve `202` ile izleme adresi döndürür.
- **Idempotent HTTP işlemleri:** Aynı tenant, operation ve `Idempotency-Key` için tamamlanmış response yeniden çalıştırılmadan döndürülür.
- **Singleton execution:** SQL-backed distributed lease, singleton background service'ler için tek etkin executor seçer.
- **Run reconciliation:** Heartbeat scanner, process kaybı sonrası sahipsiz kalan running kayıtları failed durumuna geçirir.
- **Run continuation:** Uygun orphaned session run'ı yeni bir run olarak devam eder ve güvenli tamamlanmış tool sonuçlarını replay eder.
- **Graceful drain:** Host kapanırken yeni run'lar reddedilir ve etkin run'ların belirli timeout içinde bitmesi beklenir.

## Evaluation ve controlled change

- **Eval suite ve case yönetimi:** Tekrarlanabilir input, parameter, expected değer, check ve run history içeren suite'ler yönetilebilir.
- **Built-in eval checks:** Deterministic check'ler judge model çağrısı olmadan case sonucunu değerlendirebilir.
- **Custom eval checks:** Tüketici, adlandırılmış MAF `EvalCheck` implementation'larını registry'ye ekleyebilir.
- **Run judges:** Type, instance veya factory ile custom judge; ayrıca ayrı model binding kullanan built-in model judge kaydedilebilir.
- **Run-to-case promotion:** Mevcut bir run input'u eval case olarak bir suite'e aktarılabilir.
- **Online evaluation:** Filtreli ve saatlik sınırı olan live run sample'ları background job ile otomatik score edilebilir.
- **A/B experiments:** Stable assignment, agent version variant'ları ve arm bazlı sonuç raporu ile deney yürütülebilir.
- **Canary rollback:** Explicit canary policy başarısız olduğunda background scan deneyi durdurabilir veya version'ı geri alabilir.

## Güvenlik, governance ve multi-tenancy

- **Loopback varsayılanı:** Management, MCP ve A2A yüzeyleri explicit remote access ayarı olmadan uzak istemcilere açılmaz.
- **Static bearer token:** Management API, constant-time karşılaştırılan isteğe bağlı bearer token ile korunabilir.
- **ASP.NET Core authorization:** Consumer authentication pipeline'ı ve named authorization policy endpoint grubuna uygulanabilir.
- **Role policy'leri:** Reader, Operator ve Admin işlemleri ayrı policy adlarıyla sınırlandırılabilir ve startup'ta zorunlu tutulabilir.
- **Scoped API key'ler:** API key'ler hash'li, tenant-bound, expiring, revocable ve kapalı scope kümesiyle sınırlı olarak yönetilir.
- **Multi-tenancy:** Tenant claim, izinli header veya doğrulanmış API key üzerinden çözülür ve store sorguları tenant sınırını uygular.
- **Tenant BYOK:** Tenant, provider credential değerini store'a yazmadan configuration key adı ve endpoint ile kendi model bağlantısını tanımlayabilir.
- **Tenant provider egress policy:** Her tenant için izinli provider kümesi belirlenir ve yetkisiz binding agent compile aşamasında reddedilir.
- **Session sahipliği:** Opt-in mod, bir oturumu açan kullanıcıyı kalıcı olarak kaydeder; oturum listesi sayfalamadan önce sahibe daraltılır ve başka sahibin oturumu var olmayan oturumla aynı yanıtı alır. Katı modda mod açılmadan önce yazılmış sahipsiz satırlar da reddedilir.
- **Usage quota:** Request, token ve cost limitleri günlük veya aylık dönemlerde, seçili time zone ile run admission sırasında uygulanabilir.
- **Kota eşiği korelasyonu:** Eşik geçişi webhook payload'ında tetikleyen run ve user kimliğini taşır; opt-in ayarla aynı bildirim, eşiği geçiren run'ın kendi olay akışına dönem başına yalnız bir kez yazılır.
- **HTTP rate limiting:** İstekler tenant, API key veya remote address partition'ına göre fixed-window limitlenebilir.
- **Audit trail:** Administrative mutation'lar actor, action, entity ve redacted before/after payload ile kaydedilir; agent, skill, workflow, MCP server ve governance store'larının kapsamı bir build kapısıyla zorlanır ve dışarıda bırakılan store yazılı gerekçe ister.
- **Tamper-evident audit chain:** Audit kayıtları hash chain ile bağlanır ve bütünlük `/api/audit/verify` üzerinden denetlenebilir.
- **Signed outbound webhooks:** Event'ler HMAC imzası, HTTPS ve header kontrolleriyle gönderilir; retry ve ardışık hata sonrası disable desteklenir.
- **Signed inbound triggers:** Harici sistemler HMAC'li JSON isteğiyle API key olmadan agent veya workflow run'ını queue'ya alabilir.
- **Trigger replay ve flood koruması:** Timestamp, signature tabanlı idempotency, payload limiti ve trigger bazlı rate limit uygulanır.
- **Outbound network guard:** Provider, MCP ve webhook bağlantıları DNS sonucu ve socket connect aşamasında private network ve izin policy'sine karşı doğrulanır.
- **Secret reference sınırı:** Persist edilen binding ve trigger kayıtları yalnız izinli prefix altındaki configuration key adlarını tutabilir.
- **At-rest content protection:** Session, history, run input/event, tool payload, agent file ve attachment alanları AES-256-GCM ile şifrelenebilir.
- **Retention ve archive:** Operational data için target bazlı yaş policy'si, preview, bounded cleanup job ve custom archive sink kullanılabilir.
- **Data subject hakları:** Consumer resolver ile kimliğe bağlı içerik preview, export ve erase işlemlerinden geçirilebilir.
- **CORS allowlist:** Management ve embed istekleri yalnız exact origin allowlist'i yapılandırıldığında cross-origin erişim alır.
- **External protocol guard:** MCP server ve A2A çağrıları `ExternalInvoke` scope'u ile remote-access ayarlarının güvenli birleşimini zorunlu tutar.
- **Run ve session authorization:** `IRunAuthorizationHandler`, run başlangıçlarını, mevcut bir run'ın her kaynağını (okuma, liste, iptal, replay, trace, tool çağrısı, ek, workflow ve eval yüzeyleri) ve session read, list, delete, branch veya speak erişimini caller bağlamına göre reddedebilir; reddedilen tekil kaynak var olmayan kaynakla aynı yanıtı verir.
- **Skill script güvenlik sınırı:** Script özelliği opt-in ve audit'li çalışır; işletim sistemi sandbox'ı sağlamadığı açık bir contract olarak korunur.

## Persistence ve storage

- **In-memory varsayılanlar:** `AddAgentPrism()` gerekli core store contract'ları için database gerektirmeyen in-memory implementation'lar kaydeder.
- **PostgreSQL persistence:** AgentPrism verileri embedded migration'larla ayrı schema altında saklanır; optional `pgvector` ve read view setleri vardır.
- **SQL Server persistence:** SQL Server 2019+ ve Azure SQL için aynı durable store contract'ları ayrı migration setiyle uygulanır.
- **SQLite persistence:** Tek dosyalı, table-prefix destekli SQL persistence sağlanır; single-writer sınırı nedeniyle multi-instance kullanım hedeflenmez.
- **Migration yönetimi:** Migration'lar startup'ta otomatik veya `MigrationRunner` ve CLI ile ayrı deployment adımı olarak uygulanabilir ve status bilgisi okunabilir.
- **Consumer data source kullanımı:** Üç SQL provider da tüketicinin verdiği `DbDataSource` veya connection pool ile çalışabilir.
- **Read contract view'ları:** Optional SQL view seti, run ve ilgili operational veriyi analytics tüketicilerine kararlı kolon sözleşmesiyle sunar.
- **Persisted payload compatibility:** Session ve workflow checkpoint kayıtları schema ile MAF version bilgisini taşır; daha yeni bilinmeyen schema okunmadan reddedilir.
- **Replaceable store'lar:** Consumer registration'ı `TryAdd*` davranışıyla korunur; built-in store'lar custom implementation'larla değiştirilebilir.
- **External attachment storage:** `IAttachmentStorage` ile binary içerik custom object store'a yönlendirilebilir.
- **Tenant-isolated durable contracts:** SQL store'lar definition, session, run, job, eval, workflow, governance ve observability verilerinde tenant sınırını uygular.

## Gözlemlenebilirlik ve operasyon

- **OpenTelemetry traces:** Run, model, tool, compaction ve ilgili işlem span'leri standard `ActivitySource` üzerinden yayınlanır.
- **Persisted trace sample:** Başarılı run'lar oranla, failed run'lar ise isteğe bağlı olarak her zaman store'a yazılan trace örneği üretebilir.
- **Metrics:** Run, token, cost, tool, hata, judge, cache ve job execution veya duration ölçümleri standard .NET metrics ile yayınlanır; bounded lane cardinality kullanan queue-depth gauge opt-in olarak açılabilir.
- **Cost attribution:** Model, cache input, voice ve image usage fiyatlandırılır; root ve child run cost toplamları saklanır.
- **Applied price snapshot:** Run kaydı, gerçek provider kimliğini ve cost hesabında kullanılan model, rate, currency ile source bilgisini snapshot olarak saklar.
- **Cost recalculation:** Management endpoint, yalnız fiyatı bilinmeyen geçmiş run'ları hesaplar ve önceden fiyatlandırılmış snapshot'ları değiştirmez.
- **Provider health:** Provider health check sonuçları cache'lenir ve isteğe bağlı background polling ile yenilenir.
- **Circuit breaker:** Tekrarlanan provider hataları closed, open ve half-open durumlarıyla yeni çağrıları durdurur; tenant BYOK credential'ları ayrı state taşır.
- **ASP.NET Core health check:** AgentPrism durumu consumer'ın `IHealthChecksBuilder` pipeline'ına eklenebilir.
- **Diagnostics report:** Explicit opt-in endpoint, active store'ları, extension point'leri, provider configuration'ını ve schema readiness durumunu raporlar.
- **Run analytics:** Summary, time series, agent/version/label/user kırılımları, error rate ve bounded failure cluster sorguları sağlanır.
- **Tool usage analytics:** Tool çağrı sayısı, başarı, süre ve provider-specific usage bilgileri run ve aggregate düzeyinde izlenir.
- **Run event bridge:** `IRunEventSink`, domain event'lerini consumer message bus veya channel implementation'ına aktarabilir.
- **Silent-gap diagnostics:** Persist edilen event veya span akışındaki beklenmeyen boşluklar log ile bildirilir.

## ASP.NET Core, protokoller ve client araçları

- **Management HTTP API:** `MapAgentPrism()` agent, run, session, workflow, job, eval, governance, storage ve operations endpoint'lerini seçilebilir prefix altında yayınlar.
- **OpenAPI metadata:** Management endpoint'leri operation id, tag, summary, response schema ve security metadata'sı üretir.
- **OpenAI Responses compatibility:** `/v1/responses`, streaming, conversation chaining, tool loop ve attachment input ile mevcut OpenAI client'larına hizmet verir.
- **OpenAI Chat Completions compatibility:** `/v1/chat/completions`, streaming ve non-streaming chat completion contract'ını agent run'larına dönüştürür.
- **OpenAI Conversations compatibility:** Conversation oluşturma, okuma, silme ve item listeleme session store üzerinde uygulanır.
- **MCP server:** Explicit allowlist'teki agent'lar remote MCP client'larına tool olarak sunulabilir.
- **MCP task mode:** Uzun MCP çağrıları run-backed task olarak başlatılabilir, poll edilebilir ve tenant sınırında saklanabilir.
- **A2A server:** Explicit allowlist'teki agent'lar Agent-to-Agent protokolü üzerinden dış agent'lara sunulabilir.
- **Typed .NET client:** `AgentPrism.Client`, management API için DI ile kaydedilen generated client ve DTO yüzeyi sağlar.
- **Typed TypeScript client:** `@agentprism/client`, browser ve Node.js için aynı OpenAPI kaynağından generated operation ve error mapping sunar.
- **CLI:** `agentprism` global tool migration apply/status, provider health ve threshold tabanlı eval quality gate komutlarını çalıştırır.
- **Embeddable chat widget:** `embed.js`, başka origin'deki sayfaya agent chat, streaming ve client-side tool desteği ekleyebilir.
- **Voice WebSocket protocol:** Browser veya native client'lar için version'lanmış audio/message frame contract'ı yayınlanır.

## Dashboard ve kullanıcı arayüzü

- **Embedded console:** React ve TypeScript SPA, Brotli-compressed asset olarak assembly içinden servis edilir ve consumer projesine JavaScript bağımlılığı eklemez.
- **Runtime base path:** Console `/agentprism`, `/panel` veya consumer'ın seçtiği başka bir prefix altında çalışır.
- **Dashboard:** Run, token, cost, error ve agent activity özetleri chart ve time series görünümüyle sunulur.
- **Agent ve skill yönetimi:** Agent ve skill listeleri, editor'ları, validation raporu, version diff ve rollback işlemleri arayüzden kullanılabilir.
- **Playground:** Streaming run, parameter, attachment, client-side tool, speech ve conversation devamı tek test ekranında kullanılabilir.
- **Session ve run inceleme:** History, transcript, event, trace waterfall, tool call, feedback, replay, compare, branch ve cancellation yüzeyleri vardır.
- **Workflow ve job yönetimi:** Workflow graph/editor, checkpoint, human request, schedule, job listesi ve job ayrıntıları görüntülenebilir.
- **Eval ve experiment yönetimi:** Suite, case, eval run, score, experiment result ve canary policy ekranları vardır.
- **Governance ekranları:** Approval, MCP server, trigger, API key, quota, webhook, retention, tenant provider ve audit işlemleri Settings ve ilgili ekranlardan yönetilir.
- **Catalog ve operations ekranları:** Tool, model health, diagnostics ve runtime settings bilgileri ayrı ekranlarda gösterilir.
- **Kullanılabilirlik seçenekleri:** Türkçe ve İngilizce locale, light/dark/system theme, responsive layout, keyboard shortcut ve command palette desteklenir.

## Paketleme, DI ve genişletilebilirlik

- **Modüler NuGet ailesi:** Core, provider, persistence, HTTP, UI, workflow, voice, testing, client ve template yetenekleri ayrı paketlerden seçilebilir.
- **Meta package:** `AgentPrism` paketi OpenAI, PostgreSQL, MCP, workflow, ASP.NET Core ve UI bileşenlerini tek reference ile getirir.
- **Target framework desteği:** Runtime paketleri `net8.0`, `net9.0` ve `net10.0` hedeflerini destekler.
- **Package bazlı AOT contract'ı:** Abstractions, Core, PostgreSQL, OpenAI, Anthropic, Google, Azure ve Voice trimming ve Native AOT uyumluluğunu bildirir.
- **Consumer-first DI:** Servisler `TryAdd*` ile kaydedilir; tüketicinin önceden verdiği implementation korunur.
- **MAF type passthrough:** `AIAgent`, `AgentSession`, `ChatMessage` ve `AIFunction` yeni bir AgentPrism abstraction'ı ile sarılmaz.
- **Agent decorator seam:** Custom `IAgentDecorator` type, instance veya factory olarak kayıt ve telemetry/recording pipeline'ına sıralı biçimde katılabilir.
- **Zorunlu binding profili:** `RequireCustomBinding<T>()` ile ilan edilen genişleme noktası yerleşik varsayılanla çözülüyorsa host başlamaz; hata mesajı hangi sözleşmenin, hangi tiple çözüldüğünü ve nasıl düzeltileceğini söyler.
- **Project template:** `dotnet new agentprism-api`, çalışan bir control plane, sample tool, boş secret placeholder'ları ve README üretir.
- **Pre-release dependency isolation:** Preview MAF hosting bağımlılıkları yalnız `AgentPrism.AspNetCore` paketinde tutulur.
- **Package artifact kimliği:** Pack gate, aynı version için farklı içerikli package üretimini ve mevcut release artifact'ının overwrite edilmesini reddeder.

## Test, kalite ve coding-agent desteği

- **Fake model provider:** `FakeModelProvider`, network çağrısı yapmadan deterministic text, usage ve tool-call turn'leri script edebilir.
- **Integrated test host:** `AgentPrismTestHost`, gerçek catalog, HTTP endpoint'leri ve in-memory store'larla test uygulaması başlatır.
- **Framework-neutral assertions:** `RunAssertions`, run status, output ve tool invocation sonuçlarını xUnit, NUnit veya MSTest bağımlılığı olmadan denetler.
- **Extension contract suites:** `AgentPrism.Testing.Contracts.Xunit`, custom store, provider, judge, source, tool, tool validator, tool authorization ve job handler implementation'larına ortak davranış testleri verir.
- **Source generator diagnostics:** Tool generator; ad, signature, parameter, description, instance method ve JSON serialization hatalarını build sırasında raporlar.
- **Usage analyzer diagnostics:** Eksik registration, literal secret, el yapımı retry/decorator, stale agent map ve hatalı ambient scope kullanımını build sırasında bildirir.
- **Generated agent map:** Opt-in build target, mevcut dosyayı ezmeden repository için AgentPrism capability map içeren `AGENTS.md` üretebilir.
- **Local reference üretimi:** Build, restore edilen exact package version'larının XML API ve OpenAPI dosyalarına işaret eden `AgentPrism.LocalReference.md` yazabilir.
- **Web-agent doküman yüzeyi:** Documentation build'i capability index içeren `llms.txt` ve tam metinli `llms-full.txt` üretir.
- **Public API tracking:** Shipped ve unshipped baseline dosyaları kayıtsız public contract değişikliğini build hatasına dönüştürür.
- **Packed-consumer doğrulaması:** Testler gerçek `.nupkg`, dependency graph, template, embedded UI ve generated tool kullanımını consumer proje üzerinden doğrular.
- **Gerçek altyapı testleri:** PostgreSQL, SQL Server, SQLite, browser E2E, OpenAPI drift ve TypeScript client testleri ilgili sınırları çalıştırır.
- **Build kalite kapıları:** Warning-as-error, formatting, test, package, source-language, documentation ve frontend bundle budget kontrolleri repository kapılarına bağlıdır.

## Özet

- **Toplam kategori:** 13
- **Toplam özellik:** 202
