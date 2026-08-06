# KARARLAR — İndeks

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

`KARARLAR.md` ~115 KB'dir (~48k token) ve **baştan sona okunmaz.**
Kalemi bu indeksten bul, sonra yalnız o satırı oku:

```bash
grep -n 'K-059' docs/KARARLAR.md            # tek kalemin tam gerekçesi
sed -n '120,121p' docs/KARARLAR.md          # satır numarasıyla
grep -n 'jsonb\|migration' docs/KARARLAR.md  # konu araması
```

İşaretler: 👤 kullanıcı kararı (teknik kanıtla değil, konuşarak değişir) · 🔁 yeniden açılmış

Tarih bu indekste **yoktur** (K-214) — `KARARLAR.md`'deki kalemin kendisinde durur.

Daha önce kanıtla reddedilmiş bir işi mi arıyorsun? O liste ayrı dosyada:
[`KARARLAR-INDEKS-REDDEDILEN.md`](KARARLAR-INDEKS-REDDEDILEN.md) (K-214, Faz 32 bölünmesi).

---

## Kalıcı Kararlar (251 kalem)

| K | Satır | Karar |
|---|---|---|
| K-001 | L46 | Modüler paket ailesi + meta paket 👤 |
| K-002 | L47 | Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü 👤 |
| K-003 | L48 | Hibrit agent tanımı: kod + çalışma anı veritabanı 👤 |
| K-004 | L49 | Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner 👤 |
| K-005 | L50 | Hedef framework `net8.0;net9.0;net10.0` 👤 |
| K-006 | L51 | Trim/AOT uyumu katman bazlı  |
| K-007 | L52 | Geçişli sabitleme kapalı  |
| K-008 | L53 | Ön sürüm MAF bağımlılığı yalnız `AgentPrism.AspNetCore` içinde  |
| K-009 | L54 | Sırlar `dotnet user-secrets` ile 👤 |
| K-010 | L55 | Arayüz erişimi üç katmanlı 👤 |
| K-011 | L56 | Klasör düzeni `src/` + `samples/` + `tests/` 👤 |
| K-012 | L57 | Tool'lar yalnız kodda tanımlanır  |
| K-013 | L58 | Ayrı `agentprism` PostgreSQL şeması  |
| K-014 | L59 | `run_events` append-only  |
| K-015 | L60 | Birincil anahtarlar `uuid` v7  |
| K-016 | L61 | Public API takibi Faz 7'ye ertelendi  |
| K-017 | L62 | Sürümleme MinVer ile git etiketinden  |
| K-019 | L63 | Agent kaynakları `IAgentSource` ile soyutlandı  |
| K-020 | L64 | `MAAI001` bastırması tek dosyada toplandı  |
| K-021 | L65 | Yapılandırma elle bağlanır, `Bind()` kullanılmaz  |
| K-022 | L66 | Çalıştırma olayı sıra numarasını yazıcı üretir, depo değil  |
| K-023 | L67 | Kendi UUIDv7 üretecimiz (`AgentPrismId`)  |
| K-024 | L68 | `IAgentDecorator` genişleme noktası  |
| K-018 | L69 | Bellek içi store'lar birinci sınıf implementasyon  |
| K-025 | L70 | `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil  |
| K-026 | L71 | Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil 👤 |
| K-027 | L72 | Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil  |
| K-028 | L73 | Sağlayıcı ve depolama ayarları kendi alt bölümlerinde 👤 |
| K-029 | L74 | Şema adı SQL metnine gömülür, katı doğrulamadan sonra  |
| K-030 | L75 | Responses API her zaman sunucu tarafı depolama kapalı çalışır  |
| K-031 | L76 | `OPENAI001` bastırması tek dosyada toplandı  |
| K-032 | L77 | Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur 👤 |
| K-033 | L78 | Tool taraması açık işaretleme ister (`[AgentPrismTool]`) 👤 |
| K-034 | L79 | `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir 👤 |
| K-035 | L80 | Ayar sınıfları `record` olamaz  |
| K-036 | L81 | OpenAI uyumlu uçlar AgentPrism tarafından yazılır, MAF'ın `Map*` uçlarıyla değil 👤 |
| K-037 | L82 | `ChatHistoryProvider` `AddAgentPrism()` içinde açıkça kaydedilir  |
| K-038 | L83 | `/v1/*` uçları OpenAI hata biçimini kullanır, `ProblemDetails` değil  |
| K-039 | L84 | Kütüphane OpenAPI üretimini dayatmaz, yalnız üstveri taşır  |
| K-040 | L85 | Enum'lar JSON'da ad olarak yazılır  |
| K-041 | L86 | Çalıştırma özeti depoda hesaplanır  |
| K-042 | L87 | `MapAgentPrism()` yalnız korumalı grubun convention builder'ını döndürür  |
| K-043 | L88 | Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur  |
| K-044 | L89 | Çalıştırma kimliğini çağıran üretir (`AgentPrismRunOptions`) 👤 |
| K-045 | L90 | Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı 👤 |
| K-046 | L91 | Arayüz kabuğu bearer token katmanından muaftır 👤 |
| K-047 | L92 | Arayüz token'ı `sessionStorage`'da tutulur 👤 |
| K-048 | L93 | Arayüz varlıkları Brotli sıkıştırılmış gömülür  |
| K-049 | L94 | `IAgentPrismUiProvider` tek metotlu tutuldu  |
| K-050 | L95 | Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır  |
| K-051 | L96 | `AgentPrism.UI.csproj` SDK'yı açık `Import` ile yükler  |
| K-052 | L97 | Frontend birim testleri `npm run build` içinde koşar  |
| K-053 | L98 | `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı  |
| K-054 | L99 | Faz 6 kapsamı daraltıldı: Workflows ertelendi 👤 |
| K-055 | L100 | Span'ler `ActivityListener` ile toplanır, exporter ile değil  |
| K-056 | L101 | Örnekleme kararı çalıştırma bittiğinde verilir  |
| K-057 | L102 | `ModelContextProtocol.Core`, tam `ModelContextProtocol` paketi değil  |
| K-058 | L103 | MCP'de yalnızca uzak HTTP aktarımı; stdio yok  |
| K-059 | L104 | MCP kimlik doğrulama değeri veritabanında saklanmaz  |
| K-060 | L105 | MCP tool adları `{sunucu}_{tool}`; nokta kullanılmaz  |
| K-061 | L106 | "Bir daha sorma" MAF'ın biçimiyle değil AgentPrism deposunda tutulur  |
| K-062 | L107 | Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı  |
| K-063 | L108 | `run_events` partition'ı açılmadı (kapandı → K-199)  |
| K-064 | L109 | İkinci faz önceliği: yetenek derinliği 👤 |
| K-065 | L110 | Ses: hedef gerçek zamanlı konuşma katmanı 👤 |
| K-066 | L111 | Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası 👤 |
| K-067 | L112 | Alt agent çalıştırması ayrı bir `runs` satırıdır 👤 |
| K-068 | L113 | Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz 👤 |
| K-069 | L114 | `UseOpenAICompatible(ad, ...)` ayrı ad, `UseOpenAI` aşırı yüklemesi değil 👤 |
| K-070 | L115 | Model sağlayıcı devre kesici varsayılan olarak açık 👤 |
| K-071 | L116 | Sağlayıcı sağlık denetimi arka planda varsayılan olarak koşmaz 👤 |
| K-072 | L117 | Sağlayıcı adı deseni regex değil elle karakter denetimiyle doğrulanır  |
| K-073 | L118 | Sağlık denetimi hata detayında `HttpRequestException.Message` değil `HttpRequestError` kategorisi kullanılır  |
| K-074 | L119 | Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi  |
| K-075 | L120 | Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapAgentPrism()` çağrısında bir kez yapılır  |
| K-076 | L121 | Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil  |
| K-077 | L122 | Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır  |
| K-078 | L123 | `/api/meta` yanıtına rol bilgisi (`roles`) eklendi  |
| K-079 | L124 | `mcp.refresh` denetim izine uç katmanında yazılır, depo dekoratöründe değil  |
| K-080 | L125 | Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil  |
| K-081 | L126 | Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler  |
| K-082 | L127 | Skill sürüm geçmişi tutulmaz 👤 |
| K-083 | L128 | `AllowedTools` yalnız saklanır, zorlanmaz 👤 |
| K-084 | L129 | Bir agent için varsayılan skill sınırı 10'dur 👤 |
| K-085 | L130 | Derlenmiş agent cache anahtarında skill parmak izi vardır  |
| K-086 | L131 | Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz  |
| K-087 | L132 | Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir 👤 |
| K-088 | L133 | Yorumlayıcı beyaz listesi varsayılan olarak boştur  |
| K-089 | L134 | Denetim izine yazılamayan bir script çalıştırılmaz  |
| K-090 | L135 | Argüman doğrulaması sığdır; tam JSON Schema doğrulayıcı eklenmedi  |
| K-091 | L136 | Argümanlar script sürecine stdin ile geçirilir  |
| K-092 | L137 | İzin kaydı silinmez, `revoked_at` ile iptal edilir  |
| K-093 | L138 | Alt çalıştırma ayrı bir `runs` satırıdır  |
| K-094 | L139 | `root_run_id` denormalize edilir  |
| K-095 | L140 | `runs.parent_run_id` için yabancı anahtar konmaz  |
| K-096 | L141 | Bütçe ağaç boyunca tek nesnedir; `AgentRunBudget` bilerek `class`'tır  |
| K-097 | L142 | Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır  |
| K-098 | L143 | Alt agent gec (lazy) çözülür; derleme anında çözülmez  |
| K-099 | L144 | Trace tamponunun sahibi yalnız kök çalıştırmadır  |
| K-100 | L145 | `RunQuery.OnlyRootRuns` varsayılanı `true` 👤 |
| K-101 | L146 | Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma 👤 |
| K-102 | L147 | Alt çalıştırmalar kök akışa yalnız özet olay yazar 👤 |
| K-103 | L148 | Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur  |
| K-104 | L149 | Sıkıştırma bağımlılığı için yeni paket alınmadı  |
| K-105 | L150 | `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında 👤 |
| K-106 | L151 | Sıkıştırma varsayılan kapalıdır 👤 |
| K-107 | L152 | Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez 👤 |
| K-108 | L153 | Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir  |
| K-109 | L154 | `Pipeline` sırası sabittir: ToolResult → SlidingWindow → Summarization  |
| K-110 | L155 | `TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır; bu faz `InMemoryAgentFileStore`  |
| K-111 | L156 | İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans  |
| K-112 | L157 | `attachments.session_id` yabancı anahtar DEĞİLDİR  |
| K-113 | L158 | Ek türü sihirli bayta göre doğrulanır; istemcinin bildirdiği `Content-Type` yok sayılır  |
| K-114 | L159 | Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur  |
| K-115 | L160 | `IFormFile` alan minimal API uçları `.DisableAntiforgery()` gerektirir  |
| K-116 | L161 | `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez  |
| K-117 | L162 | Kalıcı agent dosya belleği (`agent_files`) aynı migration'da (0006) eklendi 👤 |
| K-118 | L163 | Workflow yürütmesi ayrı pakette (`AgentPrism.Workflows`), `AspNetCore` bu pakete referans vermez  |
| K-119 | L164 | Beş desenin tamamı Faz 15'te uygulandı 👤 |
| K-120 | L165 | Workflow çalıştırmaları `runs` tablosunda yaşar; `workflow_runs` açılmadı  |
| K-121 | L166 | Kontrol noktası durumu `json` sütununda, `jsonb` DEĞİL  |
| K-122 | L167 | 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir  |
| K-123 | L168 | Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar  |
| K-124 | L169 | `Magentic` plan onayı Faz 15'te kapalı (`RequirePlanSignoff(false)`)  |
| K-125 | L170 | `GroupChat` yönetici agent kabul etmez; `managerAgentName` yalnız `Magentic` içindir  |
| K-126 | L171 | Workflow tanımlarında sürüm GEÇMİŞİ tutulmaz  |
| K-127 | L172 | 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır  |
| K-128 | L173 | Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar  |
| K-129 | L174 | `Microsoft.Agents.AI.Workflows.Declarative` alınmadı 👤 |
| K-130 | L175 | Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar  |
| K-131 | L176 | Graf tanımdan değil, DERLENMİŞ workflow'dan çıkarılır  |
| K-132 | L177 | Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı  |
| K-133 | L178 | Graf hatası çalıştırmayı `Failed` yapar  |
| K-134 | L179 | `WorkflowJobHandler` ayrı bir pakete değil, `AgentPrism.Core`'a konur  |
| K-135 | L180 | Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil  |
| K-136 | L181 | Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır  |
| K-137 | L182 | İş iptali ayrı bir `cancel_requested` sütunu gerektirmez; `jobs.status` tek gerçek kaynaktır  |
| K-138 | L183 | `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti  |
| K-139 | L184 | Eval (Faz 18) için yeni bir NuGet paketi eklenmedi  |
| K-140 | L185 | AI yargıç (`AIJudgeLoopEvaluator`/`LoopAgent`) Faz 18 kapsamı dışında bırakıldı  |
| K-141 | L186 | Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi 👤 |
| K-142 | L187 | `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir  |
| K-143 | L188 | Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context)  |
| K-144 | L189 | Deney atama anahtarı varsayılan olarak oturum kimliğidir; `Experiment.AssignmentKey` bu fazda rezerve 👤 |
| K-145 | L190 | `EvalRunTriggerRequest.AgentVersion` eklendi; eval, A/B deneyinden tamamen bağımsız bir mekanizmadır 👤 |
| K-146 | L191 | `agentprism.agent.version` metrik/span etiketi varsayılan açıktır (`IncludeAgentVersionTag = true`); `experiment_id`/`variant` hiçbir zaman etiket olmaz 👤 |
| K-147 | L192 | A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür  |
| K-148 | L193 | Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi  |
| K-149 | L194 | `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı  |
| K-150 | L195 | Maliyette para birimi dönüşümü yapılmaz 👤 |
| K-151 | L196 | Ağaç maliyeti kendi maliyetiyle toplanmaz; iki ayrı alan 👤 |
| K-152 | L197 | `/api/stats` Eval/Workflow'u hariç tutmaya devam eder (K-141); yeni `/api/stats/timeseries` bilerek hariç TUTMAZ 👤 |
| K-153 | L198 | `POST /api/stats/recalculate-costs` eklendi: Admin + denetim izi 👤 |
| K-154 | L199 | Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır  |
| K-155 | L200 | `AgentPrism:Pricing` `AgentPrismOptions`'ta (Core), bir saglayici paketinde değil  |
| K-156 | L201 | Dashboard'un devre kesici uyarısı mevcut `/api/models/health` `Unhealthy` durumunu kullanır; yeni uç eklenmedi  |
| K-157 | L202 | `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı  |
| K-158 | L203 | Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında  |
| K-159 | L204 | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir  |
| K-160 | L205 | Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi 👤 |
| K-161 | L206 | Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez 👤 |
| K-162 | L207 | Kota aşımında devam eden çalıştırma KESİLMEZ; yalnızca yeni çalıştırma reddedilir 👤 |
| K-163 | L208 | Webhook imzası zaman damgasını İÇERİR  |
| K-164 | L209 | SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz  |
| K-165 | L210 | Hız sınırının varsayılanı KAPALIDIR 👤 |
| K-166 | L211 | `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür  |
| K-167 | L212 | `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil  |
| K-168 | L213 | MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok  |
| K-169 | L214 | MCP OAuth geri dönüş adresi (`OAuthCallbackBaseUri`) sabit bir ayardır, istekten türetilmez  |
| K-170 | L215 | MCP OAuth token'ları `(kiracı, sunucu)` başına tek bellek içi önbellekte, iki tüketici arasında paylaşılır  |
| K-171 | L216 | Arka plandaki (etkileşimsiz) OAuth denemesi hemen başarısız olur, beklemez  |
| K-172 | L217 | `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir  |
| K-173 | L218 | Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi  |
| K-174 | L219 | Mod A kaynak okuması `AgentDefinitionCompiler`'a `IMcpResourceContextProviderFactory` soyutlamasıyla bağlanır  |
| K-175 | L220 | MCP kaynak toplu okuma önbelleği `ConcurrentDictionary`'e geçirildi  |
| K-176 | L221 | SQL kalıcılık mantığı `AgentPrism.Sql.Shared` altında PAYLAŞILAN KAYNAK olarak yaşar 👤 |
| K-177 | L222 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ  |
| K-178 | L223 | Migration numaraları sağlayıcı başına bağımsızdır  |
| K-179 | L224 | Şema adı kuralı iki sağlayıcıda AYNIDIR  |
| K-180 | L225 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda  |
| K-181 | L226 | `AgentPrism.SqlServer` AOT uyumlu olarak İŞARETLENMEZ  |
| K-182 | L227 | Diziler SQL Server'a JSON metni olarak taşınır  |
| K-183 | L228 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır  |
| K-184 | L229 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez  |
| K-185 | L230 | `AgentPrism` meta paketi `AgentPrism.SqlServer`'ı İÇERMEZ  |
| K-186 | L231 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 |
| K-187 | L232 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti  |
| K-188 | L233 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor  |
| K-189 | L234 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu  |
| K-190 | L235 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır  |
| K-191 | L236 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ  |
| K-192 | L237 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ  |
| K-193 | L238 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR  |
| K-194 | L239 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING`  |
| K-195 | L240 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir  |
| K-196 | L241 | `AgentPrism.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi)  |
| K-197 | L242 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni)  |
| K-198 | L243 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz  |
| K-199 | L244 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı)  |
| K-200 | L245 | Parti silme her sağlayıcıda farklı teknik kullanır  |
| K-201 | L246 | `MaxRows` var ama uygulanmıyor (ertelendi)  |
| K-202 | L247 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır  |
| K-203 | L248 | `sessions`/`conversations` ayrı hedeftir  |
| K-204 | L249 | Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil 👤 |
| K-205 | L250 | `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi 👤 |
| K-206 | L251 | İçerik filtresi tespiti `AgentPrism.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil 👤 |
| K-207 | L252 | Paket adı `AgentPrism.Google`, sağlayıcı adı `google` 👤 |
| K-208 | L253 | `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır 👤 |
| K-209 | L254 | `AgentPrism` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ  |
| K-210 | L255 | Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı 👤 |
| K-211 | L256 | `azure-openai` hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırıktır  |
| K-212 | L257 | Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık 👤 |
| K-213 | L258 | Azure'ın Responses yüzeyi desteklenmiyor  |
| K-214 | L259 | `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı  |
| K-215 | L260 | Ses sözleşmeleri `AgentPrism.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır  |
| K-216 | L261 | `AgentPrism.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır  |
| K-217 | L262 | `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir  |
| K-218 | L263 | Tool bağımlılıkları KURULUM anında alınır; `AIFunctionArguments.Services` MAF boru hattında boştur  |
| K-219 | L264 | `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz  |
| K-220 | L265 | `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz  |
| K-221 | L266 | Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir  |
| K-222 | L267 | Konuşma katmanı Seçenek A ile ve `AgentPrism.Core`'da  |
| K-223 | L268 | `MapAgentPrism` `UseWebSockets()`'i koşullu olarak kendisi kurar  |
| K-224 | L269 | WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez  |
| K-225 | L270 | `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz 👤 |
| K-226 | L271 | Artımlı (geçici) transkript yok; çözüm tek atımlıdır 👤 |
| K-227 | L272 | Ses dakikası bir kota birimi değildir 👤 |
| K-228 | L273 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı  |
| K-229 | L274 | `t` fonksiyonu modül düzeyindedir ve kimliği hiç değişmez  |
| K-230 | L275 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır (K-047)  |
| K-231 | L276 | Varsayılan dil tarayıcıdan gelir 👤 |
| K-232 | L277 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir  |
| K-233 | L278 | Rozet metni küçük harf, süzgeç/başlık metni büyük harf: iki ayrı anahtar kümesi  |
| K-234 | L279 | Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu  |
| K-235 | L280 | Konuşma çözümlemesine dil kodu gönderilmez  |
| K-236 | L281 | `--ap-subtle` ve `--ap-muted` WCAG AA'ya göre düzeltildi  |
| K-237 | L282 | Klavye kısayolu metin alanında tetiklenmez; `Ctrl+Enter` yereldir  |
| K-238 | L283 | Komut paleti istemci tarafında arar  |
| K-239 | L284 | `run_scores`: `author` NULL'ı sağlayıcı bazlı işlenir  |
| K-240 | L285 | `ScoredRuns`/`PositiveRate` iki yolla hesaplanır  |
| K-241 | L286 | `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi  |
| K-242 | L287 | `FeedbackControl` ikili puan gösterir, yıldız YAZILMADI  |
| K-243 | L288 | Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ  |
| K-244 | L289 | `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok  |
| K-245 | L290 | `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır  |
| K-246 | L291 | İptal isteği `run.cancel` eylemiyle denetim izine yazılır  |
| K-247 | L292 | K-183'ün isareti `AgentPrism.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `AgentPrism.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı  |
| K-248 | L293 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok  |
| K-249 | L294 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `AgentPrism:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir  |
| K-250 | L295 | `AgentPrismDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `AgentPrism.AspNetCore.AgentPrismHealthCheck` içindedir  |
| K-251 | L296 | `AddAgentPrismHealthChecks()` `AddAgentPrism()`'in önceden çağrıldığını KAYIT ANINDA denetlemez  |
