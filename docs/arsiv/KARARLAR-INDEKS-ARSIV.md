# KARARLAR — İndeks Arşivi

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme). Yeni kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).

## Arşivlenen Kararlar (398 kalem)

| K | Satır | Karar |
|---|---|---|
| K-001 | 47 | Modüler paket ailesi + meta paket 👤 |
| K-002 | 48 | Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü 👤 |
| K-003 | 49 | Hibrit agent tanımı: kod + çalışma anı veritabanı 👤 |
| K-004 | 50 | Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner 👤 |
| K-005 | 51 | Hedef framework `net8.0;net9.0;net10.0` 👤 |
| K-006 | 52 | Trim/AOT uyumu katman bazlı |
| K-007 | 53 | Geçişli sabitleme kapalı |
| K-008 | 54 | Ön sürüm MAF bağımlılığı yalnız `AgentPrism.AspNetCore` içinde |
| K-009 | 55 | Sırlar `dotnet user-secrets` ile 👤 |
| K-010 | 56 | Arayüz erişimi üç katmanlı 👤 |
| K-011 | 57 | Klasör düzeni `src/` + `samples/` + `tests/` 👤 |
| K-012 | 58 | Tool'lar yalnız kodda tanımlanır |
| K-013 | 59 | Ayrı `agentprism` PostgreSQL şeması |
| K-014 | 60 | `run_events` append-only |
| K-015 | 61 | Birincil anahtarlar `uuid` v7 |
| K-016 | 62 | Public API takibi Faz 7'ye ertelendi 🔁 |
| K-017 | 63 | Sürümleme MinVer ile git etiketinden |
| K-019 | 64 | Agent kaynakları `IAgentSource` ile soyutlandı |
| K-020 | 65 | `MAAI001` bastırması tek dosyada toplandı |
| K-021 | 66 | Yapılandırma elle bağlanır, `Bind()` kullanılmaz |
| K-022 | 67 | Çalıştırma olayı sıra numarasını yazıcı üretir, depo değil |
| K-023 | 68 | Kendi UUIDv7 üretecimiz (`AgentPrismId`) |
| K-024 | 69 | `IAgentDecorator` genişleme noktası |
| K-018 | 70 | Bellek içi store'lar birinci sınıf implementasyon |
| K-025 | 71 | `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil |
| K-026 | 72 | Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil 👤 |
| K-027 | 73 | Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil |
| K-028 | 74 | Sağlayıcı ve depolama ayarları kendi alt bölümlerinde 👤 |
| K-029 | 75 | Şema adı SQL metnine gömülür, katı doğrulamadan sonra |
| K-030 | 76 | Responses API her zaman sunucu tarafı depolama kapalı çalışır |
| K-031 | 77 | `OPENAI001` bastırması tek dosyada toplandı |
| K-032 | 78 | Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur 👤 |
| K-033 | 79 | Tool taraması açık işaretleme ister (`[AgentPrismTool]`) 👤 |
| K-034 | 80 | `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir 👤 |
| K-035 | 81 | Ayar sınıfları `record` olamaz |
| K-036 | 82 | OpenAI uyumlu uçlar AgentPrism tarafından yazılır, MAF'ın `Map*` uçlarıyla değil 👤 |
| K-037 | 83 | `ChatHistoryProvider` `AddAgentPrism()` içinde açıkça kaydedilir |
| K-038 | 84 | `/v1/*` uçları OpenAI hata biçimini kullanır, `ProblemDetails` değil |
| K-039 | 85 | Kütüphane OpenAPI üretimini dayatmaz, yalnız üstveri taşır |
| K-040 | 86 | Enum'lar JSON'da ad olarak yazılır |
| K-041 | 87 | Çalıştırma özeti depoda hesaplanır |
| K-042 | 88 | `MapAgentPrism()` yalnız korumalı grubun convention builder'ını döndürür |
| K-043 | 89 | Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur |
| K-044 | 90 | Çalıştırma kimliğini çağıran üretir (`AgentPrismRunOptions`) 👤 |
| K-045 | 91 | Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı 👤 |
| K-046 | 92 | Arayüz kabuğu bearer token katmanından muaftır 👤 |
| K-047 | 93 | Arayüz token'ı `sessionStorage`'da tutulur 👤 |
| K-048 | 94 | Arayüz varlıkları Brotli sıkıştırılmış gömülür |
| K-049 | 95 | `IAgentPrismUiProvider` tek metotlu tutuldu |
| K-050 | 96 | Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır |
| K-051 | 97 | `AgentPrism.UI.csproj` SDK'yı açık `Import` ile yükler |
| K-052 | 98 | Frontend birim testleri `npm run build` içinde koşar |
| K-053 | 99 | `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı |
| K-054 | 100 | Faz 6 kapsamı daraltıldı: Workflows ertelendi 👤 |
| K-055 | 101 | Span'ler `ActivityListener` ile toplanır, exporter ile değil |
| K-056 | 102 | Örnekleme kararı çalıştırma bittiğinde verilir |
| K-057 | 103 | `ModelContextProtocol.Core`, tam `ModelContextProtocol` paketi değil |
| K-058 | 104 | MCP'de yalnızca uzak HTTP aktarımı; stdio yok |
| K-059 | 105 | MCP kimlik doğrulama değeri veritabanında saklanmaz |
| K-060 | 106 | MCP tool adları `{sunucu}_{tool}`; nokta kullanılmaz |
| K-061 | 107 | "Bir daha sorma" MAF'ın biçimiyle değil AgentPrism deposunda tutulur |
| K-062 | 108 | Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı |
| K-063 | 109 | `run_events` partition'ı açılmadı (kapandı → K-199) |
| K-064 | 110 | İkinci faz önceliği: yetenek derinliği 👤 |
| K-065 | 111 | Ses: hedef gerçek zamanlı konuşma katmanı 👤 |
| K-066 | 112 | Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası 👤 |
| K-067 | 113 | Alt agent çalıştırması ayrı bir `runs` satırıdır 👤 |
| K-068 | 114 | Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz 👤🔁 |
| K-069 | 115 | `UseOpenAICompatible(ad, ...)` ayrı ad, `UseOpenAI` aşırı yüklemesi değil 👤 |
| K-070 | 116 | Model sağlayıcı devre kesici varsayılan olarak açık 👤 |
| K-071 | 117 | Sağlayıcı sağlık denetimi arka planda varsayılan olarak koşmaz 👤 |
| K-072 | 118 | Sağlayıcı adı deseni regex değil elle karakter denetimiyle doğrulanır |
| K-073 | 119 | Sağlık denetimi hata detayında `HttpRequestException.Message` değil `HttpRequestError` kategorisi kullanılır |
| K-074 | 120 | Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi |
| K-075 | 121 | Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapAgentPrism()` çağrısında bir kez yapılır |
| K-076 | 122 | Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil |
| K-077 | 123 | Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır |
| K-078 | 124 | `/api/meta` yanıtına rol bilgisi (`roles`) eklendi |
| K-079 | 125 | `mcp.refresh` denetim izine uç katmanında yazılır, depo dekoratöründe değil |
| K-080 | 126 | Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil |
| K-081 | 127 | Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler |
| K-082 | 128 | Skill sürüm geçmişi tutulmaz 👤 |
| K-083 | 129 | `AllowedTools` yalnız saklanır, zorlanmaz 👤 |
| K-084 | 130 | Bir agent için varsayılan skill sınırı 10'dur 👤 |
| K-085 | 131 | Derlenmiş agent cache anahtarında skill parmak izi vardır |
| K-086 | 132 | Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz |
| K-087 | 133 | Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir 👤 |
| K-088 | 134 | Yorumlayıcı beyaz listesi varsayılan olarak boştur |
| K-089 | 135 | Denetim izine yazılamayan bir script çalıştırılmaz |
| K-090 | 136 | Argüman doğrulaması sığdır; tam JSON Schema doğrulayıcı eklenmedi |
| K-091 | 137 | Argümanlar script sürecine stdin ile geçirilir |
| K-092 | 138 | İzin kaydı silinmez, `revoked_at` ile iptal edilir |
| K-093 | 139 | Alt çalıştırma ayrı bir `runs` satırıdır |
| K-094 | 140 | `root_run_id` denormalize edilir |
| K-095 | 141 | `runs.parent_run_id` için yabancı anahtar konmaz |
| K-096 | 142 | Bütçe ağaç boyunca tek nesnedir; `AgentRunBudget` bilerek `class`'tır |
| K-097 | 143 | Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır |
| K-098 | 144 | Alt agent gec (lazy) çözülür; derleme anında çözülmez |
| K-099 | 145 | Trace tamponunun sahibi yalnız kök çalıştırmadır |
| K-100 | 146 | `RunQuery.OnlyRootRuns` varsayılanı `true` 👤 |
| K-101 | 147 | Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma 👤 |
| K-102 | 148 | Alt çalıştırmalar kök akışa yalnız özet olay yazar 👤 |
| K-103 | 149 | Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur |
| K-104 | 150 | Sıkıştırma bağımlılığı için yeni paket alınmadı |
| K-105 | 151 | `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında 👤 |
| K-106 | 152 | Sıkıştırma varsayılan kapalıdır 👤 |
| K-107 | 153 | Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez 👤 |
| K-108 | 154 | Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir |
| K-109 | 155 | `Pipeline` sırası sabittir: ToolResult → SlidingWindow → Summarization |
| K-110 | 156 | `TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır; bu faz `InMemoryAgentFileStore` |
| K-111 | 157 | İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans |
| K-112 | 158 | `attachments.session_id` yabancı anahtar DEĞİLDİR |
| K-113 | 159 | Ek türü sihirli bayta göre doğrulanır; istemcinin bildirdiği `Content-Type` yok sayılır |
| K-114 | 160 | Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur |
| K-115 | 161 | `IFormFile` alan minimal API uçları `.DisableAntiforgery()` gerektirir |
| K-116 | 162 | `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez |
| K-117 | 163 | Kalıcı agent dosya belleği (`agent_files`) aynı migration'da (0006) eklendi 👤 |
| K-118 | 164 | Workflow yürütmesi ayrı pakette (`AgentPrism.Workflows`), `AspNetCore` bu pakete referans vermez |
| K-119 | 165 | Beş desenin tamamı Faz 15'te uygulandı 👤 |
| K-120 | 166 | Workflow çalıştırmaları `runs` tablosunda yaşar; `workflow_runs` açılmadı |
| K-121 | 167 | Kontrol noktası durumu `json` sütununda, `jsonb` DEĞİL |
| K-122 | 168 | 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir |
| K-123 | 169 | Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar |
| K-124 | 170 | `Magentic` plan onayı Faz 15'te kapalı (`RequirePlanSignoff(false)`) |
| K-125 | 171 | `GroupChat` yönetici agent kabul etmez; `managerAgentName` yalnız `Magentic` içindir |
| K-126 | 172 | Workflow tanımlarında sürüm GEÇMİŞİ tutulmaz |
| K-127 | 173 | 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır |
| K-128 | 174 | Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar |
| K-129 | 175 | `Microsoft.Agents.AI.Workflows.Declarative` alınmadı 👤 |
| K-130 | 176 | Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar |
| K-131 | 177 | Graf tanımdan değil, DERLENMİŞ workflow'dan çıkarılır |
| K-132 | 178 | Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı |
| K-133 | 179 | Graf hatası çalıştırmayı `Failed` yapar |
| K-134 | 180 | `WorkflowJobHandler` ayrı bir pakete değil, `AgentPrism.Core`'a konur |
| K-135 | 181 | Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil |
| K-136 | 182 | Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır |
| K-137 | 183 | İş iptali ayrı bir `cancel_requested` sütunu gerektirmez; `jobs.status` tek gerçek kaynaktır |
| K-138 | 184 | `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti |
| K-139 | 185 | Eval (Faz 18) için yeni bir NuGet paketi eklenmedi |
| K-140 | 186 | AI yargıç (`AIJudgeLoopEvaluator`/`LoopAgent`) Faz 18 kapsamı dışında bırakıldı — (yeniden açıldı: 2026-08-07, Faz 49 ile karşılandı, bkz. K-327) 🔁 |
| K-141 | 187 | Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi 👤 |
| K-142 | 188 | `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir |
| K-143 | 189 | Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context) |
| K-144 | 190 | Deney atama anahtarı varsayılan olarak oturum kimliğidir; `Experiment.AssignmentKey` bu fazda rezerve 👤 |
| K-145 | 191 | `EvalRunTriggerRequest.AgentVersion` eklendi; eval, A/B deneyinden tamamen bağımsız bir mekanizmadır 👤 |
| K-146 | 192 | `agentprism.agent.version` metrik/span etiketi varsayılan açıktır (`IncludeAgentVersionTag = true`); `experiment_id`/`variant` hiçbir zaman etiket olmaz 👤 |
| K-147 | 193 | A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür |
| K-148 | 194 | Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi |
| K-149 | 195 | `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı |
| K-150 | 196 | Maliyette para birimi dönüşümü yapılmaz 👤 |
| K-151 | 197 | Ağaç maliyeti kendi maliyetiyle toplanmaz; iki ayrı alan 👤 |
| K-152 | 198 | `/api/stats` Eval/Workflow'u hariç tutmaya devam eder (K-141); yeni `/api/stats/timeseries` bilerek hariç TUTMAZ 👤 |
| K-153 | 199 | `POST /api/stats/recalculate-costs` eklendi: Admin + denetim izi 👤 |
| K-154 | 200 | Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır |
| K-155 | 201 | `AgentPrism:Pricing` `AgentPrismOptions`'ta (Core), bir saglayici paketinde değil |
| K-156 | 202 | Dashboard'un devre kesici uyarısı mevcut `/api/models/health` `Unhealthy` durumunu kullanır; yeni uç eklenmedi |
| K-157 | 203 | `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı |
| K-158 | 204 | Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında |
| K-159 | 205 | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir |
| K-160 | 206 | Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi 👤 |
| K-161 | 207 | Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez 👤 |
| K-162 | 208 | Kota aşımında devam eden çalıştırma KESİLMEZ; yalnızca yeni çalıştırma reddedilir 👤 |
| K-163 | 209 | Webhook imzası zaman damgasını İÇERİR |
| K-164 | 210 | SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz |
| K-165 | 211 | Hız sınırının varsayılanı KAPALIDIR 👤 |
| K-166 | 212 | `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür |
| K-167 | 213 | `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil |
| K-168 | 214 | MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok |
| K-169 | 215 | MCP OAuth geri dönüş adresi (`OAuthCallbackBaseUri`) sabit bir ayardır, istekten türetilmez |
| K-170 | 216 | MCP OAuth token'ları `(kiracı, sunucu)` başına tek bellek içi önbellekte, iki tüketici arasında paylaşılır |
| K-171 | 217 | Arka plandaki (etkileşimsiz) OAuth denemesi hemen başarısız olur, beklemez |
| K-172 | 218 | `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir |
| K-173 | 219 | Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi |
| K-174 | 220 | Mod A kaynak okuması `AgentDefinitionCompiler`'a `IMcpResourceContextProviderFactory` soyutlamasıyla bağlanır |
| K-175 | 221 | MCP kaynak toplu okuma önbelleği `ConcurrentDictionary`'e geçirildi |
| K-176 | 222 | SQL kalıcılık mantığı `AgentPrism.Sql.Shared` altında PAYLAŞILAN KAYNAK olarak yaşar 👤 |
| K-177 | 223 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ |
| K-178 | 224 | Migration numaraları sağlayıcı başına bağımsızdır |
| K-179 | 225 | Şema adı kuralı iki sağlayıcıda AYNIDIR |
| K-180 | 226 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda |
| K-181 | 227 | `AgentPrism.SqlServer` AOT uyumlu olarak İŞARETLENMEZ |
| K-182 | 228 | Diziler SQL Server'a JSON metni olarak taşınır |
| K-183 | 229 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır |
| K-184 | 230 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez |
| K-185 | 231 | `AgentPrism` meta paketi `AgentPrism.SqlServer`'ı İÇERMEZ |
| K-186 | 232 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 |
| K-187 | 233 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti |
| K-188 | 234 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor |
| K-189 | 235 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu |
| K-190 | 236 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır |
| K-191 | 237 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ |
| K-192 | 238 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ |
| K-193 | 239 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR |
| K-194 | 240 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING` |
| K-195 | 241 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir |
| K-196 | 242 | `AgentPrism.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi) |
| K-197 | 243 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni) |
| K-198 | 244 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz |
| K-199 | 245 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı) |
| K-200 | 246 | Parti silme her sağlayıcıda farklı teknik kullanır |
| K-201 | 247 | `MaxRows` var ama uygulanmıyor (ertelendi → kapandı K-258) |
| K-202 | 248 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır |
| K-203 | 249 | `sessions`/`conversations` ayrı hedeftir |
| K-204 | 250 | Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil 👤 |
| K-205 | 251 | `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi 👤 |
| K-206 | 252 | İçerik filtresi tespiti `AgentPrism.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil 👤 |
| K-207 | 253 | Paket adı `AgentPrism.Google`, sağlayıcı adı `google` 👤 |
| K-208 | 254 | `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır 👤 |
| K-209 | 255 | `AgentPrism` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ |
| K-210 | 256 | Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı 👤 |
| K-211 | 257 | `azure-openai` hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırıktır |
| K-212 | 258 | Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık 👤 |
| K-213 | 259 | Azure'ın Responses yüzeyi desteklenmiyor |
| K-214 | 260 | `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı |
| K-215 | 261 | Ses sözleşmeleri `AgentPrism.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır |
| K-216 | 262 | `AgentPrism.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır |
| K-217 | 263 | `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir |
| K-218 | 264 | Tool bağımlılıkları KURULUM anında alınır; `AIFunctionArguments.Services` MAF boru hattında boştur |
| K-219 | 265 | `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz |
| K-220 | 266 | `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz |
| K-221 | 267 | Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir |
| K-222 | 268 | Konuşma katmanı Seçenek A ile ve `AgentPrism.Core`'da |
| K-223 | 269 | `MapAgentPrism` `UseWebSockets()`'i koşullu olarak kendisi kurar |
| K-224 | 270 | WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez |
| K-225 | 271 | `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz 👤 |
| K-226 | 272 | Artımlı (geçici) transkript yok; çözüm tek atımlıdır 👤 |
| K-227 | 273 | Ses dakikası bir kota birimi değildir 👤 |
| K-228 | 274 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı |
| K-229 | 275 | `t` fonksiyonu modül düzeyindedir ve kimliği hiç değişmez |
| K-230 | 276 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır (K-047) |
| K-231 | 277 | Varsayılan dil tarayıcıdan gelir 👤 |
| K-232 | 278 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir |
| K-233 | 279 | Rozet metni küçük harf, süzgeç/başlık metni büyük harf: iki ayrı anahtar kümesi |
| K-234 | 280 | Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu |
| K-235 | 281 | Konuşma çözümlemesine dil kodu gönderilmez |
| K-236 | 282 | `--ap-subtle` ve `--ap-muted` WCAG AA'ya göre düzeltildi |
| K-237 | 283 | Klavye kısayolu metin alanında tetiklenmez; `Ctrl+Enter` yereldir |
| K-238 | 284 | Komut paleti istemci tarafında arar |
| K-239 | 285 | `run_scores`: `author` NULL'ı sağlayıcı bazlı işlenir |
| K-240 | 286 | `ScoredRuns`/`PositiveRate` iki yolla hesaplanır |
| K-241 | 287 | `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi |
| K-242 | 288 | `FeedbackControl` ikili puan gösterir, yıldız YAZILMADI |
| K-243 | 289 | Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ |
| K-244 | 290 | `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok |
| K-245 | 291 | `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır |
| K-246 | 292 | İptal isteği `run.cancel` eylemiyle denetim izine yazılır |
| K-247 | 293 | K-183'ün isareti `AgentPrism.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `AgentPrism.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı |
| K-248 | 294 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok |
| K-249 | 295 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `AgentPrism:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir |
| K-250 | 296 | `AgentPrismDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `AgentPrism.AspNetCore.AgentPrismHealthCheck` içindedir |
| K-251 | 297 | `AddAgentPrismHealthChecks()` `AddAgentPrism()`'in önceden çağrıldığını KAYIT ANINDA denetlemez |
| K-252 | 298 | Döngü denetimi `AgentCallGraph.ValidateDetailed`'e taşındı, yeni dosya yok |
| K-253 | 299 | `AgentPrismValidationOptions.McpTimeout` Core'a eklendi, AspNetCore'a değil |
| K-254 | 300 | Kota ölçerine `quota.metric` eklendi 👤 |
| K-255 | 301 | Kota ölçeri `usage`+`limit` için AYRI iki `ObservableGauge`'dur 👤 |
| K-256 | 302 | `QuotaUsageObserver` senkron kapılı önbellektir, zamanlayıcı değil |
| K-257 | 303 | Kota ölçeri yalnız KAYITLI kiracıları tarar |
| K-258 | 304 | `MaxRows` sıra, silme adımından çıkarılarak uygulandı (K-201 kapandı) |
| K-259 | 305 | Saklama korelasyonları BARE hedef adı değil, TAM NİTELENDİRİLMİŞ ad kullanır (Faz 25 hatası düzeltildi) |
| K-260 | 306 | `MaxRows` kiracı genelinde uygulanır, kiracı başına DEĞİL (Faz 36 planının Açık Soru 3'ünden sapma) |
| K-261 | 307 | `eval_case_results`/`workflow_checkpoints` için `MaxRows` eşiği İLİŞKİLİ tablo üzerinden hesaplanır (Açık Soru 2 çözüldü) |
| K-262 | 308 | Şablonda `IncludeSymbols=false` zorunlu |
| K-263 | 309 | Şablonda `TargetFrameworks` boşaltılır |
| K-264 | 310 | Şablon içeriği `<None Pack>` ile paketlenir |
| K-265 | 311 | Şablon paket sürümü varsayılanı kayan `*-*` |
| K-266 | 312 | Üretilen `OrderTools.cs` `using AgentPrism;` taşır |
| K-267 | 313 | `ModelBinding.ResponseFormat` yetenek denetimi yalnız `Json`/`JsonSchema` kiplerinde çalışır |
| K-268 | 314 | `TemplateFixture` sablon testleri icin tam cozum yerine `AgentPrism.src.slnf` (yalniz `src/` paketlerini listeleyen bir cozum filtresi) paketler |
| K-269 | 315 | `AgentPrism.Testing.FakeModelProvider` modele ozel, BIR KEZ tuketilen bir yanit kuyrugu tutar; mesaj gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz |
| K-270 | 316 | `AgentPrism.Testing` yalniz `net10.0` hedefler (cogul `TargetFrameworks` ozelligiyle ezilerek) |
| K-271 | 317 | `tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs` SILINMEDI (plandan sapma) |
| K-272 | 318 | Uç etiketleri TEK `.WithTags("AgentPrism", "<Alan>")` çağrısıyla verilir |
| K-273 | 319 | SSE/ikili yanıtlar `Produces<T>` ile tiple bildirilir; `responseType: null` içerik tipini tamamen düşürür |
| K-274 | 320 | Aynı statü koduna birden fazla `.Produces` çağrısı yapılmaz; çoklu içerik tipi TEK çağrıya `additionalContentTypes` ile yazılır |
| K-275 | 321 | On bir ham `Task<IResult>` ucunun tamamı yol B (`.Produces`/`.ProducesProblem` üstverisi) ile belgelendi; hiçbiri yol A'ya (`Results<...>` imza değişikliği) taşınmadı |
| K-276 | 322 | `docs/openapi/agentprism.json` üretim kaynağı `AgentPrism.AspNetCore.FunctionalTests`'tir, `samples/AgentPrism.Api` DEĞİL |
| K-277 | 323 | Bellek içi depolar `ITenantContext` alır; kiracı süzgeci artık isteğe bağlı değildir |
| K-278 | 324 | `sessions` birincil anahtarı `(tenant_id, id)`; oturum kimliği kiracı içinde benzersizdir |
| K-279 | 325 | Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu `tenantId` alır 👤 |
| K-280 | 326 | Çalıştırmanın alt yazmaları ambient kiracıyla süzülmez; `[TenantAgnostic]` ile gerekçesi yazılır |
| K-281 | 327 | `TenantAgnosticAttribute` `AgentPrism.Sql.Shared` içinde ve `internal`'dir |
| K-282 | 328 | Kiracı yalıtımı iki depo örneğiyle değil, değiştirilebilir tek bir kiracı bağlamıyla sınanır |
| K-283 | 329 | Görünmeyen bir oturum YOK sayılır; "başkasının oturumu" reddi kaldırıldı |
| K-284 | 330 | Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil |
| K-285 | 331 | `SingletonGuard` `public`tir; "internal yardımcı" planı uygulanamadı |
| K-286 | 332 | Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn taban); gerçek devralma ölçüldü |
| K-287 | 333 | Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil |
| K-288 | 334 | `/api/agents/{name}/run` `Idempotency-Key` varsa akışsız (JSON) çalışır; plandan sapma (kullanıcı kararı) 👤 |
| K-289 | 335 | `AgentPrismIdempotencyOptions` `AgentPrism.Core`'dadır, plandaki gibi `AgentPrism.AspNetCore`'da değil |
| K-290 | 336 | `idempotency_keys` için ayrı bir `Retention: TimeSpan` alanı yerine standart `RetentionTargets`/`AgentPrismRetentionOptions` üçlüsü kullanıldı |
| K-291 | 337 | Idempotency desteği varsayılan AÇIKTIR (`Enabled = true`); K1'in "varsayılan kapalı" kuralının bilinçli bir yorumu |
| K-292 | 338 | Ham govde, filtreden ÖNCE `MapAgentPrism` içine eklenen koşullu bir ara yazılımla tamponlanır |
| K-293 | 339 | `RunError` sınıf/parmak izini doğrudan taşır; ayrı bir arama tablosu açılmadı |
| K-294 | 340 | Sınıflandırma TEK bir noktada, `RunRecordingAgent.CompleteAsync` içinde, `error` `null` değilse çalışır |
| K-295 | 341 | `ByErrorClass` ayrı bir depo metodu değil, `GetStatisticsAsync`'in genişletilmiş sonucu; `/api/stats/errors` o sonucun dar bir dilimi |
| K-296 | 342 | Hata sınıflandırıcının SDK istisna adları örnek uygulamada gerçek bir OpenAI hatasıyla ölçüldü; `ClientResultException` eksikti |
| K-297 | 343 | `quota_exceeded` sınıfı otomatik sınıflandırıcı için YAPISAL olarak ulaşılamazdır; taksonomide kalır ama örnek uygulamada uçtan uca gösterilemedi |
| K-298 | 344 | Parmak izi normalleştirmesi tırnak içi metni SİLMEZ (Açık Soru 3 → C) |
| K-299 | 345 | Sınıf başına en sık üç küme, pencere fonksiyonlarıyla (`ROW_NUMBER()`/`COUNT() OVER`) tek geçişte hesaplanır — bu desenin kod tabanındaki İLK kullanımı |
| K-300 | 346 | Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur; plan taslağının "run_events zaten kullanıcı girdisini taşır" iddiası yanlıştı ve düzeltildi (kullanıcı kararı) 👤 |
| K-301 | 347 | Çok turluluk, "bu oturumda DAHA ÖNCE başlamış başka bir çalıştırma var mı" sorusuyla belirlenir; tam konuşma geçmişi okunmaz |
| K-302 | 348 | `AddCaseAsync`'in `seq`/`source_run_id` eşzamanlılığı `ON CONFLICT`/`MERGE` değil, düz `INSERT` + `SqlDialect.IsUniqueViolation` yakalama + yeniden deneme ile çözülür |
| K-303 | 349 | "Olumsuz puan" otomatik terfi tetikleyicisi olarak `Binary` için `Value == 0`, `Stars` için `Value <= 2` (5 üzerinden) tanımlandı |
| K-304 | 350 | Kuyruğa alınan (`Prefer: respond-async`) bir çalıştırmanın `runs` satırı, işçinin gerçek yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync` bu yüzden bir UPSERT'tir |
| K-305 | 351 | Kuyruğa alınan bir çalıştırmada `JobRecord.Id` ile `RunRecord.Id` bilinçli olarak AYNI değeri taşır |
| K-306 | 352 | `AgentPrismAsyncRunOptions.MaxAttempts` varsayılanı `1`'dir (kullanıcı kararı) 👤 |
| K-307 | 353 | `AgentPrismAsyncRunOptions.Enabled` varsayılanı `true`'dur (kullanıcı kararı) 👤 |
| K-308 | 354 | Çalıştırmanın girdisi AYRI bir `run_inputs` tablosunda ve `json` sütununda saklanır; `runs`'a sütun EKLENMEZ |
| K-309 | 355 | Varsayılan tool modu `ReplayTools`; kayıtlı sonucu olmayan bir çağrı yeniden oynatmayı DURDURUR ve `422` döner |
| K-310 | 356 | `LiveTools` `Admin` rolü ister ve onay gerektiren bir tool taşıyan agent bu modda çalıştırılamaz (`409`) |
| K-311 | 357 | Dallanma öğeleri KOPYALAR; işaretçi zinciri reddedildi |
| K-312 | 358 | `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ |
| K-313 | 359 | Konusma dallandırma yalnız SQL sağlayıcısı açıkken çalışır; bellek içi kurulumda uç `501` döner (kullanıcı kararı) 👤 |
| K-314 | 360 | Kod kaynaklı agent'lar model bindirmesi ve `NoTools`/`ReplayTools` ile oynatılamaz; `400` döner (kullanıcı kararı) 👤 |
| K-315 | 361 | Yeniden oynatma OTURUMSUZDUR |
| K-316 | 362 | Yeniden oynatma yalnız SENKRON ve akışsızdır; kuyruğa alma bu fazın kapsamı dışındadır |
| K-317 | 363 | `azure-sql-edge` artık güvenilir bir yerel doğrulama ikamesidir; hazır-olma denetimi `sqlcmd` yerine ADO.NET ile yazılmalıdır (kullanıcı kararı) 👤 |
| K-318 | 364 | SQL Server migration'larında `ALTER TABLE ADD` ile eklenen sütunu AYNI toplu işlemde `CREATE INDEX`'te kullanmak "Invalid column name" verir; dört migration dosyası etkiliydi |
| K-319 | 365 | `EvalCaseResult.Scores` ayarlanmamışken (varsayılan `JsonValueKind.Undefined`) SQL Server'a `"[]"` yazılır, `"null"` DEĞİL — `ISJSON` kısıtı bare `null`'ı reddeder |
| K-320 | 366 | Model çağrı boru hattının TAMAMINI `ModelProviderRegistry` kurar; `IModelProvider` HAM istemci döndürür (kullanıcı kararı) 👤 |
| K-321 | 367 | İçerik guard'ı `IChatClient` katmanındadır ve tool çağrı döngüsünün İÇİNDEDİR |
| K-322 | 368 | Devre kesici içerik engellemesini ardışık hata SAYMAZ |
| K-323 | 369 | Yeni bir genişleme noktasının "varsayılan kapalı" kapısı KAYITTIR, bir `Enabled` bayrağı değildir (kullanıcı kararı) 👤 |
| K-324 | 370 | `422` yalnızca AKIŞSIZ çalıştırma dalında dönebilir (kullanıcı kararı) 👤 |
| K-325 | 371 | Engellenen veya maskelenen içerik HİÇBİR yere yazılmaz |
| K-326 | 372 | `RunErrorClass.ContentBlocked` `ContentFiltered`'dan AYRIDIR |
| K-327 | 373 | `AIJudgeLoopEvaluator` KULLANILMADI; `IRunJudge` sıfırdan yazıldı (Faz 49) |
| K-328 | 374 | Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrılır; yeni bir sütun açılmadı (Faz 49) |
| K-329 | 375 | İki kapılı varsayılan: `OnlineEvaluationOptions.Enabled = false` VE `SampleRate = 0.0` (Faz 49) |
| K-330 | 376 | Yargıcın `IChatClient`'ı guard boru hattından GEÇER; engelleme özel olarak ele alınmadı (Faz 49, D1) |
| K-331 | 377 | `RunScore.Author` yargıç puanlarında `judge:{ad}` ile BİLEREK DOLU yazılır (Faz 49) |
| K-332 | 378 | Cevrimiçi değerlendirme pencere özeti BELLEK İÇİDİR; yeni bir SQL sorgu yüzeyi açılmadı (Faz 49) |
| K-333 | 379 | `OnlineEvalJobHandler` DI'da hem `IJobHandler` hem KENDİ somut tipiyle kayıtlıdır (Faz 49) |
| K-334 | 380 | K-057 güncellendi: AgentPrism artık MCP istemcisi VE sunucusudur (Faz 50) |
| K-335 | 381 | A2A sunucu maliyeti yeniden ölçüldü: "+2 paket" değil "+4 paket"; iki paket plan taslağında hiç yoktu (Faz 50) |
| K-336 | 382 | A2A her disa acik agent icin AYRI bir alt yol ve AYRI bir agent karti kullanir; tekil kart varsayimi terk edildi (Faz 50) |
| K-337 | 383 | MCP/A2A dış çağrısı `ChildAgentInvoker`'ı KULLANMAZ; `ExternalAgentProxy`/`CatalogToolCallHandler` her zaman YENİ bir kök çalıştırma açar (Faz 50) |
| K-338 | 384 | MCP/A2A dış yüzeyleri erişim ayarlarını `IApplicationBuilder.Properties` üzerinden `MapAgentPrism`'den DEVRALIR; `MapAgentPrism` önce çağrılmalıdır (Faz 50) |
| K-339 | 385 | `ChildRunApproval` public yapıldı: üçüncü tüketici MCP/A2A dış çağrı katmanıdır (Faz 50) |
| K-340 | 386 | Dışa açılan bir agent'ın `AgentRunBudget.MaxDepth` değeri, kaç seviye TORUN çağrısına izin verildiğidir; "0" = hiç, "1" = bir seviye (Faz 50) |
| K-341 | 387 | Vektör gömüsü metin biçiminde (`::vector` cast) yazılır, hiçbir vektör paketi alınmaz (Faz 51) |
| K-342 | 388 | K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş (Faz 51) |
| K-343 | 389 | Anlamsal arama yalnız PostgreSQL'de uygulanır (Faz 51) 👤 |
| K-344 | 390 | Sql.Shared'in cross-provider katmanı `IVectorSearchStore` için kullanılmaz (Faz 51) |
| K-345 | 391 | `document_embeddings.metadata` sütunu `jsonb`'dir, `json` değil (Faz 51) |
| K-346 | 392 | Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues` (Faz 51) |
| K-347 | 393 | K-218 kapatıldı: `ToolMethodScanner` örnek metotları TARAMA ANINDA reddeder (Faz 52) |
| K-348 | 394 | Kaynak üreteci ayrı bir NuGet paketi değildir; `AgentPrism.Core` nupkg'sinde `analyzers/dotnet/cs/` altında taşınır (Faz 52) |
| K-349 | 395 | Kaynak üretecinin Roslyn sürümü `Microsoft.CodeAnalysis.CSharp` `4.8.0`'dır (Faz 52) |
| K-350 | 396 | `AddGeneratedTools()` `IAgentPrismBuilder`'a EKLENMEDİ; derlemeye özel üretilmiş bir uzantı metodudur (Faz 52) |
| K-351 | 397 | Parametre tipi beyaz listesi `record`/`class` (composite) tipleri KAPSAMAZ; `APG0003` ile reddedilir (Faz 52) |
| K-352 | 399 | F-76 (OpenAPI 500) YALNIZ `ProjectReference` tüketicisini etkiler; düzeltme kütüphanede değil, örnek uygulamanın derlemesindedir (2026-08-08 denetimi) |
| K-353 | 400 | `.UseMcp()` yapılandırmayı AÇIKÇA alır; AgentPrism kendiliğinden `IConfiguration` okumaz (2026-08-08 denetimi) |
| K-354 | 401 | SQL'e dokunan arka plan servisleri `SchemaReadyGate`'i bekler; hosted service kayıt sırası ZORLANMAZ (2026-08-08 denetimi) |
| K-355 | 402 | Çalıştırmanın alt yazmaları BEKLENEN kiracıyı taşır; ambient kiracı KULLANILMAZ ve alan HTTP sözleşmesine girmez (2026-08-08 denetimi) |
| K-356 | 403 | API anahtarı ozeti SHA-256'dır; Argon2 DEĞİL (Faz 53, Açık Soru 2) |
| K-357 | 404 | API anahtarı doğrulaması önbelleklenmez; her istekte `key_hash` ile SQL aranır (Faz 53, Açık Soru 3, seçenek A) |
| K-358 | 405 | `last_used_at` her istekte değil, en az bir dakikada bir yazılır (Faz 53, Açık Soru 4, seçenek B) |
| K-359 | 406 | `Authorization` başlığı sunulduğunda, statik `AuthToken` tanımsız olsa bile doğrulanmalıdır; eşleşmezse 401 (Faz 53, bilinçli davranış değişikliği) |
| K-360 | 407 | API anahtarı kapsam (`scope`) denetimi yalnız agents/runs/external-invoke uçlarına uygulandı; tam taksonomi ERTELENDİ (Faz 53) |
| K-361 | 408 | `docs/MIMARI.md` sıcak yol bütçesi 42.000 → 44.000 bayt (Faz 53) |
| K-362 | 409 | Heartbeat toplu yazılır: `IRunStore.TouchHeartbeatAsync` tek `Guid` değil `IReadOnlyCollection<Guid>` alır (Faz 54, Açık Soru 1, seçenek B) |
| K-363 | 410 | `RunErrorClass.Infrastructure` yeni değer olarak eklendi (Faz 54, Açık Soru 3, seçenek A'nın düzeltilmiş hâli) |
| K-364 | 411 | Oksuz hata parmak izi SABİT bir dize (`"orphaned"`); `ErrorFingerprint.Compute` ÇAĞRILMAZ (Faz 54) |
| K-365 | 412 | Heartbeat/oksuz-kapama sorguları dizi parametresi (`WHERE id IN (@array)`) KULLANMAZ; tekil `UPDATE` döngüsü ve alt-sorgulu tek `UPDATE` tercih edildi (Faz 54) |
| K-366 | 413 | `ClaimOrphanedRunsAsync` kapattığı çalıştırmanın `RunFailed` olayını da KENDİSİ yazar; ayrı bir yazma turu yok (Faz 54) |
| K-367 | 414 | `MapAgentPrismMcpServer`/`MapAgentPrismA2A` onay-yüzeyi denetimi, `Map*()` sırasında senkron çalışan bir kontrolden istek-bazlı `IEndpointFilter`e taşındı (kullanıcı kararı) 👤 |
| K-368 | 415 | `RunStatus.AwaitingApproval` eklendi; onay kararından sonra AYNI `RunId` devam ETMEZ, `AwaitingInput` emsaliyle birebir aynı şekilde YENİ bir çalıştırma açılır (Faz 55, kullanıcı kararı) 👤 |
| K-369 | 416 | `pending_approvals.run_id`, `runs(id)`e `ON DELETE CASCADE` ile bağlı; sözleşme testleri gerçek bir `runs` satırı önceden kuran bir `PrepareRunAsync` kancası kazandı (Faz 55) |
| K-370 | 417 | `ApprovalEndpoints.DecideAsync`, `audit` kaydını mutasyondan ÖNCE ve `AuditRecorder.WriteAsync` (hataları yutan sarmalayıcı) DEĞİL doğrudan `IAuditLog.WriteAsync` ile yazar (Faz 55) |
| K-371 | 418 | `IPendingApprovalStore.ExpireAsync`, planın taslak imzası `ValueTask<int>` yerine `ValueTask<IReadOnlyList<PendingApproval>>` döner (Faz 55, plandan sapma) |
| K-372 | 419 | Senkron/MCP/A2A çalıştırma yolu `pending_approvals`'a HİÇ yazmaz; yalnız kuyruktan koşan (`Prefer: respond-async`) çalıştırmalar yazar (Faz 55, plandan sapma — planın Açık Soru 1 önerisi "B: her ikisi" idi, uygulanan "A: yalnız kuyruk") |
| K-373 | 420 | Kanarya kuralı yalnızca İKİ kollu deneylerde tanımlanabilir; planın "kalan kollar kontrol sayılır" (çoğul) ifadesi UYGULANMADI (Faz 56, plandan sapma) |
| K-374 | 421 | `ExperimentAssignmentResolver.SelectVariant`, kanarya kuralı tanımlıyken kanarya kolunu HER ZAMAN `[0, ağırlık)` aralığına yerleştirir — bu, `Experiment.Variants`'ın FİZİKSEL sırasından bağımsızdır (Faz 56) |
| K-375 | 422 | `CanaryPolicy`'ye ayrı bir `RampRequiresSampleSize` alanı AÇILMADI; kademeli artırma da `MinSampleSize`'ı AYNEN kullanır (Faz 56, plandan sapma) |
| K-376 | 423 | `CanaryPolicy`'ye Faz 49'unkine benzer ayrı bir `EvaluationWindow` eklenmedi; kanarya kararı `ExperimentVariantResult`'ın TÜM-ZAMANLI (deney başından beri biriken) sonuçlarına dayanır (Faz 56, plandan sapma, Açık Soru 5) |
| K-377 | 424 | `ExperimentVariantResult`e `AverageScore` eklendi; `SelectExperimentResults` sorgusu `run_scores`'a (önce çalıştırma başına ortalama, sonra kol başına o ortalamaların ortalaması) genişletildi (Faz 56) |
| K-378 | 425 | Kademeli artırma adımı denetim izine YAZILMAZ; yalnız otomatik GERİ ALMA `IAuditLog.WriteAsync` ile mutasyondan ÖNCE (K-089 emsali) yazılır (Faz 56) |
| K-379 | 426 | `IExperimentStore.SetCanaryPolicyAsync`, `SaveAsync`'in Draft-yalnız kısıtından MUAFTIR; kanarya kuralı deney Running iken de tanımlanabilir veya kaldırılabilir (Faz 56) |
| K-380 | 427 | `CompiledAgentCache`'in anahtarına `TenantId` eklendi; iki farklı kiracının aynı ad+sürüm+bağımlılık parmak izinde bir tanımı olması artık BİRİNCİ kiracının derlenmiş agent'ını İKİNCİ kiracıya sızdırmaz (manuel kabul testi hazırlığı, kullanıcı talebiyle bulundu) |
| K-381 | 428 | Paylaşılan `AgentFileStore` (dosya belleği/metin araması) her kiracı için `TenantPrefixingAgentFileStore` ile sarmalanır; kiracı sınırı MAF'ın kendi deposuna dokunmadan bir vekil (proxy) katmanında uygulanır (manuel kabul testi hazırlığı) |
| K-382 | 429 | `AgentPrismEndpointFilter`'a `CheckTenancyWhitelist` eklendi; `AllowedTenants` doluyken listede olmayan bir aday artık istek endpoint'e ulaşmadan 403 ile reddedilir, varsayılan kiracıya SESSİZCE düşmez (manuel kabul testi hazırlığı) |
| K-383 | 430 | `AgentPrism.Core.csproj`'un üreteç-paketleme hedefi `@(Analyzer)` yerine Generators projesinin `GetTargetPath` çıktısını MSBuild görevi ile okur; `dotnet pack --no-build` artık `analyzers/dotnet/cs/AgentPrism.Generators.dll`'i İÇERİR (manuel kabul testi hazırlığı — daha önce Kritik olarak not düşülmüştü) |
| K-384 | 431 | SSE akış uçlarındaki (`AgentEndpoints`, `OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`) dar istisna filtresi kaldırıldı; `OperationCanceledException` dışındaki HER istisna artık bir `error` çerçevesine dönüşür (K-296'nın tamamlanması, manuel kabul testi hazırlığı) |
| K-385 | 432 | K-302 güncellendi: `AddCaseAsync`'in yeniden deneme döngüsüne jitter eklendi, üst sınır 5 → 10'a çıkarıldı |
| K-386 | 433 | K-317 kapandı: Docker Desktop 4.29.0 → 4.86.0 güncellemesi gerçek `mssql/server`'daki Rosetta hatasını çözdü; SQL Server sözleşme testleri bu makinede artık `azure-sql-edge` ikamesi OLMADAN, gerçek imajla koşuyor |
| K-387 | 434 | `SqlServerTestContext.DisposeAsync` artık test şemasını GERÇEKTEN bırakır (dokümantasyon iddia ediyordu, kod yapmıyordu); deadlock'a çarparsa 5 denemeli backoff ile yeniden dener |
| K-388 | 435 | `MigrationRunner.ApplyOneAsync` migration SQL'ini ve `INSERT INTO __migrations` kaydını TEK round-trip'te birleştirir; `CreateSchema`+`CreateMigrationsTable` birleşimi ve `SET XACT_ABORT ON` ile tam tek-round-trip BİLEREK yapılmadı |
| K-389 | 436 | Migration kilidi VERİTABANI genelinden SEMAYA/ONEĞE kapsandı: `SqlServerDialect`, `PostgresDialect`, `SqliteDialect` artık `sp_getapplock`/`pg_advisory_lock`/kilit dosyası anahtarını sema (veya SQLite'ta tablo öneki) adından türetir |
| K-390 | 437 | Sözleşme test izolasyonu "her test kendi şeması" modelinden "her test SINIFI kendi şeması + her test verisini `ResetDataAsync` ile sıfırlar" modeline geçti (K-389 ile birlikte) |
| K-391 | 438 | PostgreSQL: `CREATE EXTENSION IF NOT EXISTS vector` içeren `0024_vector.sql`, K-389 sonrası eş zamanlı ilk-kez migrasyonlarda benzersizlik ihlaline (`pg_extension_name_index`, SQLSTATE 23505) düşebilir — `MigrationRunner.ApplyOneAsync` bunu jitter'lı yeniden deneme ile karşılar, test fixture'ı ise uzantıyı bir kez ÖNCEDEN kurarak yarışı tamamen önler |
| K-392 | 439 | `InvariantGlobalization` hem örnek uygulamadan (`samples/AgentPrism.Api`) hem paket şablonundan (`AgentPrism.Starter`) KALDIRILDI; SQL Server desteğiyle bağdaşmıyor |
| K-393 | 440 | `A2AApprovalGuardFilter`/`McpApprovalGuardFilter`: şema hazır değilken (`AutoApplyMigrations=false`) katalog sorgusu HEMEN uygulamayı durdurmak yerine sınırsız sayıda, üstel gecikmeli (5 sn'de tavanlanan) yeniden dener; deneme SAYISI değil yalnız uygulama kapanışı sınırlar (HATA-S1-002, manuel kabul testi S1) |
| K-394 | 441 | Workflow çalıştırmaları artık kota muhasebesinden geçiyor: `WorkflowEndpoints.RunAsync` agent'larla AYNI `QuotaGate.CheckAsync` 429 kapısından geçer, `WorkflowRunner.CompleteAsync` workflow'un TAMAMINI (Depth 0, `TreeUsage`/`TreeCost` toplamından) TEK bir "run" olarak `QuotaEnforcer.RecordAsync`'e yazar (HATA-S1-006, Kritik, manuel kabul testi S1) |
| K-395 | 442 | `AgentPrismEndpointFilter`: `requireBearerToken:false` gruplarına (eşlenmemiş `api/*` yolları, gerçek zamanlı ses ucu) AgentPrism'in kendi statik `AuthToken`'ıyla eşleşen bir başlık artık REDDEDİLMİYOR — başlık YOKMUŞ gibi nötr davranılıyor (HATA-S1-014, manuel kabul testi S1) |
| K-396 | 443 | `AgentDefinitionCompiler.SearchFileStoreAsync`: `TextSearchProvider`'ın doğal dil sorgusu artık `AgentFileStore.SearchAsync`'e ham regex olarak DEĞİL, boşluğa göre ayrılmış ≥3 karakterlik tokenlerin kaçışlanıp "VEYA" ile birleştirildiği bir desen olarak geçiyor (HATA-S1-009, manuel kabul testi S1) |
| K-397 | 444 | `ApiKeyScope`'a `KnowledgeRead`/`KnowledgeAdmin` eklendi; `KnowledgeEndpoints`'in tüm uçları artık `RequireApiKeyScope` çağırıyor (HATA-S1-011, Yüksek, manuel kabul testi S1) |
| K-398 | 445 | `RunRecordingAgent.RunCoreStreamingAsync`: terminal durum artık tüketicinin ERKEN `DisposeAsync()`'i (dogal bitiş değil, istisna da değil) durumunda da yazılıyor — `Canceled`, o ana kadar biriken kısmi `usage` ile (HATA-S1-015, Yüksek, manuel kabul testi S1) |
