# Faz Geçmişi — Anlatı Arşivi

> **Arşiv.** Her fazın sonunda `MIMARI.md`'ye yazılan "Faz N sonunda …"
> paragrafları burada birikir. `MIMARI.md` **bugünkü** mimariyi anlatır;
> bu dosya oraya nasıl gelindiğini anlatır.
>
> Bu dosya oturum başında **okunmaz**. Bir fazın neden öyle yapıldığını
> ararken grep'le:
>
> ```bash
> grep -n "Faz 15" docs/arsiv/FAZ-GECMISI.md
> ```
>
> Yeni faz paragrafı `MIMARI.md`'ye değil, **buraya** eklenir.

---

Faz 9 sonunda AgentPrism **denetlenebilir**: üç rol (Reader/Operator/Admin) uç
grupları arasında ayrım yapıyor, `audit_log` gerçekten doluyor (agent, MCP sunucusu,
kiracı, onay kuralı yazmaları + tool onay kararları) ve sır suzgeci bu kayıtlardan
hiçbir kimlik bilgisi sızdırmıyor. Bkz. [`09-YONETISIM-VE-DENETIM-IZI.md`](../09-YONETISIM-VE-DENETIM-IZI.md).

Faz 12 sonunda bir agent kataloğdaki başka bir agent'ı **çağırabiliyor**. Her alt
çağrı ayrı bir `runs` satırı üretir (`parent_run_id`, `root_run_id`, `depth`),
span'leri kök span'in altında iç içe görünür ve ağaç boyunca **tek** bir
`AgentRunBudget` nesnesi paylaşılır. Çağrı grafiği kaydetme anında döngüye karşı
denetlenir; çalışma anında derinlik sayacı ikinci savunma hattıdır. Bkz.
[`12-AGENT-CAGRI-GRAFIGI.md`](../12-AGENT-CAGRI-GRAFIGI.md).

Faz 13 sonunda bir agent'ın konuşma geçmişi **sıkıştırılabiliyor**: beş
strateji (+ sabit sıralı bir pipeline) hem düz `ChatClientAgent` hem
`HarnessAgent` yolunda çalışıyor, tetiklendiğinde `run_events`'e
`HistoryCompacted` olarak yazılıyor ve özetleme çağrısının token'ları
çalıştırmanın toplamına ekleniyor. Üç bellek sağlayıcısı (dosya belleği, todo,
metin araması) da aynı yoldan açılabiliyor. Gerçek bir HTTP çalıştırmasında
doğrulandı: `SlidingWindow` stratejisi 4. turda tetiklendi ve 7 mesajı 5'e
indirdi. Vektör tabanlı `ChatHistoryMemoryProvider` bilinçli olarak kapsam
dışı bırakıldı — gerçek kurucusu bir `VectorStore` istiyor, depoda somut bir
implementasyon yok. Bkz. [`13-BAGLAM-SIKISTIRMA-VE-BELLEK.md`](../13-BAGLAM-SIKISTIRMA-VE-BELLEK.md).

Faz 14 sonunda bir agent'a **görsel/dosya eki** gönderilebiliyor. İkili
içerik `attachments` tablosunda (`bytea`) yaşar; sohbet geçmişindeki mesaj
yalnız küçük bir `UriContent` referansı taşır ve gerçek baytlara yalnız
`AttachmentResolvingChatClient` içinde, gerçek sağlayıcı çağrısından hemen
önce çözülür — geçmiş okumasının maliyeti ek boyutundan bağımsız kalır.
Tür doğrulaması sihirli bayta dayanır, istemcinin `Content-Type`'ı
güvenilmez. `attachments.session_id` **bilerek** yabancı anahtar değildir
(gerçek akışta bir ek, kendi oturumu hiç açılmadan önce yüklenebilir);
oturum silindiğinde eklerin gitmesi uygulama katmanında yapılır. Aynı
migration (0006) `agent_files` tablosunu da getirdi: Faz 13'ten kalan
`FileMemoryProvider`/`TextSearchProvider`, kod değişmeden kalıcı belleğe
(`PostgresAgentFileStore`) döndü. Bkz. [`14-COK-MODLULUK.md`](../14-COK-MODLULUK.md).

Faz 15 sonunda katalogdaki agent'lar **workflow olarak zincirlenebiliyor**.
Beş hazır desen (Sequential, Concurrent, Handoff, GroupChat, Magentic) arayüzden
tanımlanabilir; serbest graf yalnızca kodda kurulur (K2 korunur). Her yürütme bir
`runs` satırıdır (`kind = Workflow`) ve içinde çağrılan her agent Faz 12'nin
`parent_run_id` mekanizmasıyla altına bağlanır — waterfall ek kod olmadan doğru
çizilir. Her super-step'te bir kontrol noktası yazılır ve yarım kalan bir yürütme
ortasından sürdürülebilir. Gerçek bir çalıştırmada doğrulandı: bir workflow + iki
agent satırı, ağaç toplamı 329 token, üç zincirli kontrol noktası, hem kodda hem
arayüzden tanımlı workflow için başarılı sürdürme.

Faz 16 sonunda workflow **görülebiliyor ve insanla konuşabiliyor**. Derlenmiş
graf arayüzde elle çizilen SVG olarak görünür; düğümler çalıştırma sırasında
canlı renklenir çünkü düğüm kimlikleri `ExecutorInvoked` olaylarının metniyle
birebir aynıdır (K-131). Bir graf dış istek portuna ulaştığında yürütme durur,
durumu kontrol noktasına yazılır ve çalıştırma `RunStatus.AwaitingInput` olarak
kapanır; yanıt yeni bir `runs` satırı açar (K-130). `Magentic` plan onayı
açılabilir hâle geldi.

🚨 **Faz 15'in kontrol noktası sınırı kaldırıldı** (K-127). Ölçüldü: bir grafta
kimliği değişken olan tek şey agent executor'udur; yardımcı düğümler zaten
sabittir. `WorkflowAgentIdentity` sarmalayıcının kimliğini `(workflow, agent)`
çiftinden türetir, böylece kontrol noktaları süreç ömrünü aşar. Gerçek bir
süreç yeniden başlatmasıyla doğrulandı: kimlik aynı kaldı ve yeniden
başlatmadan **önce** oluşan bekleyen istek sonrasında cevaplandı. Bkz.
[`16-WORKFLOWS-ARAYUZ.md`](../16-WORKFLOWS-ARAYUZ.md).

Faz 17 sonunda bir agent veya workflow **toplu** ve **zamanlanmış** olarak
çalıştırılabiliyor. Kuyruk PostgreSQL üzerinde `FOR UPDATE SKIP LOCKED` ile
kiralanır — ek bir mesaj kuyruğu (Redis, RabbitMQ) gerekmez; gerçek eşzamanlılık
testiyle doğrulandı: iki gerçek `IJobStore` örneği 50 iş için yarıştı, hiçbiri
iki kez kiralanmadı. `JobWorkerBackgroundService` tek bir `PeriodicTimer`
döngüsünde hem sırası gelen `job_schedules` satırlarını `jobs`'a düşürür hem
kiralanabilir işleri `IJobHandler` sözleşmesine dağıtır; işçi
`AgentPrismSchedulingOptions.RunWorker = false` ile kapatılabilir, kuyruk yine
de yazılabilir/okunabilir kalır (K-018 deseni). Bir toplu iş her ögesi için
sıradan bir `runs` satırı üretir — `job_items.run_id` üzerinden geriye bağlanır,
ikinci bir kayıt hattı açılmaz. Beş alanlı cron alt kümesi elle yazılmıştır
(K-007); yaz saati geçişinde geçersiz bir yerel zaman sessizce atlanır. Zamanlanmış
bir işin hangi kiracı için çalıştığı ne HTTP bağlamında ne sabit varsayılanda
bulunur — `AmbientTenantScope` (AsyncLocal) bu boşluğu `IHttpContextAccessor`
ile aynı desenle doldurur (K-136). Gerçek bir çalıştırmada doğrulandı: bir
zamanlama oluşturuldu, elle tetiklendi, iki ögeli iş ~5 saniyede tamamlandı ve
her öge gerçek bir `runs` satırına (gerçek model kullanımıyla) bağlandı. Bkz.
[`17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md`](../17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md).

Faz 18 sonunda bir agent **eval takımıyla ölçülebiliyor**: bir takım hedef agent
+ bildirimsel `checks` (`nonEmpty`, `containsExpected`, `keywords`, `toolCalled`,
`toolCallsPresent`, `hasImageContent` — `Microsoft.Agents.AI.EvalChecks`'e eşlenir)
taşır, vakalar `PUT .../cases` ile tam değiştirilir. Koşu, Faz 17'nin **aynı**
`jobs` kuyruğunu kullanır (`JobKind.Eval`) — ayrı bir yürütme yolu açılmadı. Her
vaka `IAgentCatalog.ResolveAsync` ile çözülen agent üzerinde **yeni bir
oturumda** çalışır ve kendiliğinden kendi `runs` satırını üretir (`EvalJobHandler`
`AgentPrismRunOptions.Kind = RunKind.Eval` verir); bu satırlar normal
istatistiklerden (`IRunStore.GetStatisticsAsync`) hariç tutulur ama transkript
ve span erişimi aynı kalır. `agent_version`/`model_id` tetikleme anında değil,
işçi işi fiilen **çalıştırmaya başlarken** çözülür — kuyrukta beklerken tanım
değişebileceği için. Gerçek bir çalıştırmada doğrulandı: aynı takım, veritabanı
kaynaklı bir agent'ın iki ardışık sürümüne karşı koşturuldu ve `agentVersion: 1`
/ `agentVersion: 2` farklı model çıktılarıyla yan yana görüldü. Yeni bir NuGet
paketi gerekmedi (K-139): eval tipleri zaten doğrudan referanslı `Microsoft.Agents.AI`
içinde. Bkz. [`18-DEGERLENDIRME.md`](../18-DEGERLENDIRME.md).

Faz 19 sonunda iki sürüm **yan yana görülebiliyor** ve **aynı anda çalıştırılabiliyor**.
Diff hesabı sunucuda yapılmaz — `GET .../versions/{a}/diff/{b}` iki ham
`AgentDefinition` döner, istemcideki elle yazılmış bir LCS diff'i (`lib/diff.ts`,
K-045 gerekçesinin devamı) satır/alan/küme karşılaştırmasını çizer. A/B deneyi
(`Experiment`) aynı agent'ın iki sürümü arasında trafiği **oturum bazlı
deterministik** bir SHA-256 atamasıyla böler (`ExperimentAssignmentResolver`) ve
yalnızca `AgentEndpoints.RunAsync` içine gömülüdür — alt-agent çağrıları, workflow
adımları ve eval çalıştırmaları deneye hiç girmez (K-131), her biri
`IAgentCatalog.ResolveAsync`'i kendi amacıyla (güncel sürüm / sabit sürüm) çağırır.
Versiyon çözümü `IVersionedAgentSource` marker arayüzüyle eklendi: yalnızca
veritabanı kaynağı (`DefinitionStoreAgentSource`) uygular, kod kaynağı (K-003)
dokunulmadı kaldı — sürüm istenen bir kod agent'ı `AgentPrismException` fırlatır.
`runs.agent_version` her çalıştırmada dolar (deney dışı çalıştırmalarda da,
descriptor'ın güncel sürümü varsayılan olur); `experiment_id`/`variant` yalnız
deney tarafından atanmış çalıştırmalarda dolar ve **hiçbiri metrik etiketi
olmaz** — sürüm etiketi (`agentprism.agent.version`) kardinalitesi kabul
edilebilir bulunup varsayılan açık bırakıldı, deney kimliği sınırsız büyüyeceği
için hiç etikete girmedi. Gerçek bir çalıştırmada doğrulandı: iki talimat
sürümü arasında %50/%50 ağırlıklı bir deney, 20 farklı oturumla çalıştırıldı ve
control/v2 kollarına 6/14 dağıldı (küçük örneklem varyansı, 10.000 örnekte
±2 puan içinde kaldığı ayrıca test edildi). Sonuç tablosunda istatistiksel bir
"kazanan" iddiası **yoktur** — ham sayılar gösterilir. Bkz.
[`19-SURUM-KARSILASTIRMA-VE-AB.md`](../19-SURUM-KARSILASTIRMA-VE-AB.md).

Faz 5 sonunda kabul senaryosu tamamlandı: paket kurulur, `.UseUI()` +
`app.MapAgentPrism()` yazılır ve tarayıcıda bir kontrol düzlemi açılır. Faz 6 ekranı
sekize çıkardı (MCP & approvals) ve arayüz artık span waterfall'ı, tool çağrı
sayılarını ve onay kartlarını gösteriyor. Arayüz assembly'ye Brotli sıkıştırılmış
gömülüdür (85,1 KB), tüketici projede hiçbir JavaScript bağımlılığı oluşturmaz ve
JavaScript bütçesi Faz 16 sonunda 105,2 KB / 250 KB gzip'tir.

Faz 6 sonunda AgentPrism **işletilebilir**: her çalıştırmanın span ağacı ve metriği
var, geri alınamaz tool'lar kullanıcı onayı bekliyor, tool'lar uzak MCP
sunucularından da gelebiliyor ve kiracı istekten çözülüp hiçbir uçtan sızmıyor.

Faz 8 sonunda AgentPrism **tek satıcıya bağlı değildir**: `UseOpenAICompatible(ad, ...)`
herhangi bir OpenAI uyumlu uca (OpenRouter, Groq, vLLM, yerel Ollama/LM Studio)
bağlanır, her sağlayıcı `GET {endpoint}/models` ile ücretsiz denetlenir ve ardışık
hata veren bir sağlayıcı devre kesici tarafından geçici olarak durdurulur. Doğrulandı:
gerçek OpenAI + gerçek OpenRouter anahtarlarıyla üç sağlayıcı (`openai`,
`openai-responses`, `openrouter`) da gerçek yanıt üretti; ayrıntı
[`08-SAGLAYICI-GENISLEMESI.md`](../08-SAGLAYICI-GENISLEMESI.md).

Faz 10 sonunda agent'lar markdown tabanlı, script'siz skill'ler yükleyebilir.
Skill kaynakları tenant-yalıtımlı saklanır, kod kaydı aynı ad için veritabanı
kaydını geçersiz kılar ve MAF'ın varsayılan onay zinciri kapatılmaz. Gerçek
OpenRouter çalıştırmasında `load_skill` onayı Playground'da kabul edildi; skill
talimatı yüklenip modelin yanıtını belirledi. Ayrıntı
[`10-AGENT-SKILLERI.md`](../10-AGENT-SKILLERI.md).

Dış yüzey Faz 4'ten beri açık: stok OpenAI SDK'sı `base_url` değiştirerek AgentPrism'e
bağlanıyor, agent'ı `model` alanından seçiyor, tool döngüsü sunucuda tamamlanıyor,
konuşma hem `previous_response_id` hem `conversations.create()` ile zincirleniyor ve
her çalıştırma `run_events` tablosuna yazılıp SSE ile geri oynatılabiliyor.

