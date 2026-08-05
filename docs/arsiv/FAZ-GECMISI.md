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

Faz 21 sonunda AgentPrism **dış dünyayla sözleşmeye bağlandı**. Kullanım iki ayrı
mekanizmayla sınırlanabiliyor: hız sınırı (saniye/dakika, bellekte, ASP.NET Core
paylaşılan çerçevesinden — yeni paket gerekmedi) ve kota (gün/ay, veritabanında,
kiracı ve agent kapsamında). İkisi de varsayılan olarak hiçbir isteği reddetmiyor.
Olaylar imzalı webhook'larla dışarı yayılıyor; teslim Faz 17'nin *aynı* iş
kuyruğunu kullanıyor — `IJobStore` bunun için geri adımlı beklemeyle genişletildi,
ikinci bir kuyruk yazılmadı. Fazın en büyük işi güvenlikti: webhook adresini
kullanıcı verdiği için SSRF yüzeyi açılıyor, bu yüzden denetim
`SocketsHttpHandler.ConnectCallback` içine gömüldü — doğrulanan adres, soketin
bağlandığı adresin ta kendisi. Canlı sınamada metadata ucu (`169.254.169.254`) ve
`10/8` reddedildi, loopback teslim edildi, imza bağımsız bir dinleyicide
doğrulandı. İki gerçek hata yalnızca örnek uygulama çalıştırılınca çıktı: atanmamış
`JobRecord.Payload` `/api/jobs`'ın tamamını 500'e düşürüyordu (K-166) ve
`AllowInsecureHttp` loopback *adresini* açmadığı için yerel teslim imkânsızdı
(K-167). Ayrıntı [`21-KOTA-VE-OLAY-YAYINI.md`](../21-KOTA-VE-OLAY-YAYINI.md).


### Faz 26 — Anthropic ve Gemini (2026-08-05)

Fazın tüm maliyeti tek bir ölçüme bağlıydı ve ölçüm planı **iyi yönde** bozdu:
plan topluluk paketlerini (`Anthropic.SDK`, `Google_GenerativeAI`) varsayıyordu,
oysa ikisinin de **resmî** birinci taraf karşılığı vardı (`Anthropic` 12.39.0,
`Google.GenAI` 1.16.0) ve ikisi de kendi `AsIChatClient` adaptörünü taşıyordu.
Böylece "IChatClient uygulamasını biz yazarız" senaryosu hiç gerçekleşmedi ve faz
`AgentPrism.OpenAI` ile birebir aynı şekli aldı (K-204).

Google'ın resmî SDK'sı `Google.Apis.Auth` üzerinden `Newtonsoft.Json`,
`System.Management` ve `System.CodeDom` çekiyordu — 11 geçişli bağımlılık. Ağırlık
bilerek kabul edildi ve tek pakette izole edildi; alternatif tek bakımcılı bir 0.x
paketti ve bakımsız kalma riski daha pahalı sayıldı (K-205).

Sözleşmeye tek bir alan eklendi: `ModelBinding.ProviderSettings`. `AgentDefinition.Metadata`
ile aynı şekli seçmek işe yaradı — `jsonb` yolu, HTTP sözleşmesi ve kaynak üreteci
bağlamı hiç değişmeden çalıştı (K-208).

Güvenlik filtresi tespiti sağlayıcı paketlerine değil `AgentPrism.Core`'a kondu;
devre kesiciyle aynı desen. Dekoratörün devre kesicinin **dışında** durması
gerektiği tasarım aşamasında yakalandı: filtrelenmiş bir yanıt sağlayıcının
sağlıklı olduğunu gösterir, içeride olsaydı arka arkaya filtrelenen birkaç istek
sağlayıcıyı kapatırdı (K-206).

En pahalı ölçüm, SDK'nın ham gösterimiyle ilgiliydi: Anthropic adaptörü
`RawRepresentationFactory` çıktısındaki alanların **üzerine yazmıyor**. Yer tutucu
bir model adıyla gönderilen istek gerçekten o adla gitti ve `404` döndü —
varsayımla ilerlenseydi hata yalnız üretimde görünürdü. Gemini tarafında K-032'nin
bedeli somut olarak yaşandı: `gemini-2.5-flash` çağrısı *"no longer available to
new users"* döndü. Ayrıntı
[`26-ANTHROPIC-VE-GEMINI.md`](../26-ANTHROPIC-VE-GEMINI.md).

### Faz 27 — Azure OpenAI (2026-08-05)

Faz 26'nın dersi bir faz sonra aynen tekrarlandı, bu kez **derleme yeşilken**.

Plan iki iş öngörüyordu: Azure OpenAI (kolay yarı) ve Azure AI Foundry (zor yarı).
Foundry'nin koşulu "MAF 1.16.0 ile sürüm uyumu" olarak yazılmıştı. Ölçüm bu koşulu
çürüttü — `Microsoft.Agents.AI.Foundry` 1.5.0 MAF 1.16.0 ile sorunsuz yüklendi,
tipleri yansımayla listelendi. Yerine hiç beklenmeyen bir sayı çıktı: **37 geçişli
paket** (`Azure.AI.Projects`, `Azure.Storage.Blobs`, `Azure.Identity`,
`Google.Protobuf`, `Microsoft.ML.Tokenizers`…). Bir faz önce 11 bağımlılık için
uzun uzun tartışılmıştı. Foundry ertelendi; ölçümler ve kaybedilen garantiler
tablosu faz dokümanında korundu (K-212).

Asıl bulgu Azure OpenAI tarafında çıktı. `Azure.AI.OpenAI` 2.1.0 `OpenAI` 2.1.0'a
karşı derlenmiş; AgentPrism `OpenAI` 2.12.0 kullanıyor ve merkezî paket yönetimi
tek sürüm zorluyor. NuGet çakışmayı sessizce çözdü, `dotnet build` **sıfır uyarı**
verdi — ve `AzureChatExtensions`'ın istek tarafı metotlarının tamamı çalışma anında
`MissingMethodException` attı. Derleme yeşilliğinin hiçbir şey kanıtlamadığı
durumun ders kitabı örneği. Sonuç: paket o yüzeye hiç dokunmuyor ve sağlayıcıya
özgü **hiçbir ayar sunmuyor** (K-211). Faz 26'nın `*ProviderSettingsChatClient`
kalıbı bu pakette hiç oluşturulmadı.

Kimlik tarafında plan "`Azure.Identity` bedeli bilinçli kabul edilir, alternatifi
kimlik fabrikasıdır" diyordu ve alternatif kazandı — ölçümle: `AzureOpenAIClient`
`Azure.Core.TokenCredential` alıyor, yani yönetilen kimlik `Azure.Identity`
**olmadan** çalışıyor. Paket yalnız `Azure.Core`'a bağlandı (K-210).

Gerçek bir Azure aboneliği yoktu; bunun yerine Azure'un veri düzlemi sözleşmesini
taklit eden yerel bir uç kuruldu ve gelen istegin yolu, başlıkları ve gövdesi
kaydedildi. Tool döngüsü, akışlı token sayımı ve Entra `Bearer` başlığı bu şekilde
uçtan uca doğrulandı. Ayrıntı [`27-AZURE-FOUNDRY.md`](../27-AZURE-FOUNDRY.md).

---

## Migration geçmişi (`agentprism` şeması)

`docs/MIMARI.md`'den taşındı (2026-08-03, Faz 21) — birikimli anlatı sıcak yolda
tutulmaz. Bugünkü tablo listesi `MIMARI.md` bölüm 5'tedir; şemanın kaynağı her
zaman `src/AgentPrism.PostgreSql/Migrations/*.sql` dosyalarıdır.

| Migration | Faz | Ne eklendi |
|-----------|-----|------------|
| 0001 | 0 | Temel 13 tablo + `__migrations` defteri |
| 0002 | 6 | `traces`, `spans`. Ayrıca Faz 6'da `tool_approval_rules` ve `mcp_servers` eklendi; `tool_invocations` dolmaya başladı |
| — | 9 | `audit_log` **doldu**; şema Faz 0'da kurulmuştu, yazan kod Faz 9'da geldi — migration gerekmedi |
| 0003 | 10 | `agent_skills`, `agent_skill_resources` |
| 0004 | 11 | Skill script izinleri |
| 0005 | 12 | `runs` tablosuna çağrı grafiği sütunları (yeni tablo yok) |
| 0006 | 14 | `attachments`, `agent_files` |
| 0007 | 15 | `workflows`, `workflow_checkpoints`; `runs` tablosuna `kind` (0=Agent 1=Workflow 2=Eval) ve `workflow_name` |
| 0008 | 17 | `job_schedules`, `jobs` (`FOR UPDATE SKIP LOCKED`), `job_items` |
| 0009 | 18 | `eval_suites`, `eval_cases`, `eval_runs`, `eval_case_results` — dördü de `IEvalStore` üzerinden; koşular Faz 17'nin `jobs` kuyruğunu (`kind=Eval`) kullanır, ayrı kuyruk açmaz |
| 0010 | 19 | `experiments` (`variants jsonb`, tek sütun — ayrı tablo yok); `runs` tablosuna `agent_version`, `experiment_id`, `variant`. Aynı agent için tek `Running` deney kuralı `experiments_running_agent_uq` kısmi benzersiz indeksiyle veritabanında zorlanır |
| 0011 | 20 | `runs` tablosuna maliyet sütunları: `input_cost`, `output_cost`, `cost_currency`, `pricing_source` (yeni tablo yok) |
| 0012 | 21 | `quotas`, `quota_usage`, `webhook_subscriptions`, `webhook_deliveries`; `jobs` tablosuna `max_attempts`. `webhook_deliveries` bir kuyruk **değildir** — zamanlama ve kiralama `jobs` tablosunda yaşar (K-160) |

