# KARARLAR — İndeks Arşivi

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme). Yeni kararlar için: [`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md).

## Arşivlenen Kararlar (305 kalem)

| K | Satır | Karar |
|---|---|---|
| K-001 | 46 | Modüler paket ailesi + meta paket 👤 |
| K-002 | 47 | Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü 👤 |
| K-003 | 48 | Hibrit agent tanımı: kod + çalışma anı veritabanı 👤 |
| K-004 | 49 | Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner 👤 |
| K-005 | 50 | Hedef framework `net8.0;net9.0;net10.0` 👤 |
| K-006 | 51 | Trim/AOT uyumu katman bazlı |
| K-007 | 52 | Geçişli sabitleme kapalı |
| K-008 | 53 | Ön sürüm MAF bağımlılığı yalnız `AgentPrism.AspNetCore` içinde |
| K-009 | 54 | Sırlar `dotnet user-secrets` ile 👤 |
| K-010 | 55 | Arayüz erişimi üç katmanlı 👤 |
| K-011 | 56 | Klasör düzeni `src/` + `samples/` + `tests/` 👤 |
| K-012 | 57 | Tool'lar yalnız kodda tanımlanır |
| K-013 | 58 | Ayrı `agentprism` PostgreSQL şeması |
| K-014 | 59 | `run_events` append-only |
| K-015 | 60 | Birincil anahtarlar `uuid` v7 |
| K-016 | 61 | Public API takibi Faz 7'ye ertelendi |
| K-017 | 62 | Sürümleme MinVer ile git etiketinden |
| K-019 | 63 | Agent kaynakları `IAgentSource` ile soyutlandı |
| K-020 | 64 | `MAAI001` bastırması tek dosyada toplandı |
| K-021 | 65 | Yapılandırma elle bağlanır, `Bind()` kullanılmaz |
| K-022 | 66 | Çalıştırma olayı sıra numarasını yazıcı üretir, depo değil |
| K-023 | 67 | Kendi UUIDv7 üretecimiz (`AgentPrismId`) |
| K-024 | 68 | `IAgentDecorator` genişleme noktası |
| K-018 | 69 | Bellek içi store'lar birinci sınıf implementasyon |
| K-025 | 70 | `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil |
| K-026 | 71 | Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil 👤 |
| K-027 | 72 | Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil |
| K-028 | 73 | Sağlayıcı ve depolama ayarları kendi alt bölümlerinde 👤 |
| K-029 | 74 | Şema adı SQL metnine gömülür, katı doğrulamadan sonra |
| K-030 | 75 | Responses API her zaman sunucu tarafı depolama kapalı çalışır |
| K-031 | 76 | `OPENAI001` bastırması tek dosyada toplandı |
| K-032 | 77 | Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur 👤 |
| K-033 | 78 | Tool taraması açık işaretleme ister (`[AgentPrismTool]`) 👤 |
| K-034 | 79 | `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir 👤 |
| K-035 | 80 | Ayar sınıfları `record` olamaz |
| K-036 | 81 | OpenAI uyumlu uçlar AgentPrism tarafından yazılır, MAF'ın `Map*` uçlarıyla değil 👤 |
| K-037 | 82 | `ChatHistoryProvider` `AddAgentPrism()` içinde açıkça kaydedilir |
| K-038 | 83 | `/v1/*` uçları OpenAI hata biçimini kullanır, `ProblemDetails` değil |
| K-039 | 84 | Kütüphane OpenAPI üretimini dayatmaz, yalnız üstveri taşır |
| K-040 | 85 | Enum'lar JSON'da ad olarak yazılır |
| K-041 | 86 | Çalıştırma özeti depoda hesaplanır |
| K-042 | 87 | `MapAgentPrism()` yalnız korumalı grubun convention builder'ını döndürür |
| K-043 | 88 | Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur |
| K-044 | 89 | Çalıştırma kimliğini çağıran üretir (`AgentPrismRunOptions`) 👤 |
| K-045 | 90 | Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı 👤 |
| K-046 | 91 | Arayüz kabuğu bearer token katmanından muaftır 👤 |
| K-047 | 92 | Arayüz token'ı `sessionStorage`'da tutulur 👤 |
| K-048 | 93 | Arayüz varlıkları Brotli sıkıştırılmış gömülür |
| K-049 | 94 | `IAgentPrismUiProvider` tek metotlu tutuldu |
| K-050 | 95 | Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır |
| K-051 | 96 | `AgentPrism.UI.csproj` SDK'yı açık `Import` ile yükler |
| K-052 | 97 | Frontend birim testleri `npm run build` içinde koşar |
| K-053 | 98 | `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı |
| K-054 | 99 | Faz 6 kapsamı daraltıldı: Workflows ertelendi 👤 |
| K-055 | 100 | Span'ler `ActivityListener` ile toplanır, exporter ile değil |
| K-056 | 101 | Örnekleme kararı çalıştırma bittiğinde verilir |
| K-057 | 102 | `ModelContextProtocol.Core`, tam `ModelContextProtocol` paketi değil |
| K-058 | 103 | MCP'de yalnızca uzak HTTP aktarımı; stdio yok |
| K-059 | 104 | MCP kimlik doğrulama değeri veritabanında saklanmaz |
| K-060 | 105 | MCP tool adları `{sunucu}_{tool}`; nokta kullanılmaz |
| K-061 | 106 | "Bir daha sorma" MAF'ın biçimiyle değil AgentPrism deposunda tutulur |
| K-062 | 107 | Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı |
| K-063 | 108 | `run_events` partition'ı açılmadı (kapandı → K-199) |
| K-064 | 109 | İkinci faz önceliği: yetenek derinliği 👤 |
| K-065 | 110 | Ses: hedef gerçek zamanlı konuşma katmanı 👤 |
| K-066 | 111 | Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası 👤 |
| K-067 | 112 | Alt agent çalıştırması ayrı bir `runs` satırıdır 👤 |
| K-068 | 113 | Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz 👤 |
| K-069 | 114 | `UseOpenAICompatible(ad, ...)` ayrı ad, `UseOpenAI` aşırı yüklemesi değil 👤 |
| K-070 | 115 | Model sağlayıcı devre kesici varsayılan olarak açık 👤 |
| K-071 | 116 | Sağlayıcı sağlık denetimi arka planda varsayılan olarak koşmaz 👤 |
| K-072 | 117 | Sağlayıcı adı deseni regex değil elle karakter denetimiyle doğrulanır |
| K-073 | 118 | Sağlık denetimi hata detayında `HttpRequestException.Message` değil `HttpRequestError` kategorisi kullanılır |
| K-074 | 119 | Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi |
| K-075 | 120 | Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapAgentPrism()` çağrısında bir kez yapılır |
| K-076 | 121 | Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil |
| K-077 | 122 | Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır |
| K-078 | 123 | `/api/meta` yanıtına rol bilgisi (`roles`) eklendi |
| K-079 | 124 | `mcp.refresh` denetim izine uç katmanında yazılır, depo dekoratöründe değil |
| K-080 | 125 | Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil |
| K-081 | 126 | Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler |
| K-082 | 127 | Skill sürüm geçmişi tutulmaz 👤 |
| K-083 | 128 | `AllowedTools` yalnız saklanır, zorlanmaz 👤 |
| K-084 | 129 | Bir agent için varsayılan skill sınırı 10'dur 👤 |
| K-085 | 130 | Derlenmiş agent cache anahtarında skill parmak izi vardır |
| K-086 | 131 | Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz |
| K-087 | 132 | Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir 👤 |
| K-088 | 133 | Yorumlayıcı beyaz listesi varsayılan olarak boştur |
| K-089 | 134 | Denetim izine yazılamayan bir script çalıştırılmaz |
| K-090 | 135 | Argüman doğrulaması sığdır; tam JSON Schema doğrulayıcı eklenmedi |
| K-091 | 136 | Argümanlar script sürecine stdin ile geçirilir |
| K-092 | 137 | İzin kaydı silinmez, `revoked_at` ile iptal edilir |
| K-093 | 138 | Alt çalıştırma ayrı bir `runs` satırıdır |
| K-094 | 139 | `root_run_id` denormalize edilir |
| K-095 | 140 | `runs.parent_run_id` için yabancı anahtar konmaz |
| K-096 | 141 | Bütçe ağaç boyunca tek nesnedir; `AgentRunBudget` bilerek `class`'tır |
| K-097 | 142 | Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır |
| K-098 | 143 | Alt agent gec (lazy) çözülür; derleme anında çözülmez |
| K-099 | 144 | Trace tamponunun sahibi yalnız kök çalıştırmadır |
| K-100 | 145 | `RunQuery.OnlyRootRuns` varsayılanı `true` 👤 |
| K-101 | 146 | Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma 👤 |
| K-102 | 147 | Alt çalıştırmalar kök akışa yalnız özet olay yazar 👤 |
| K-103 | 148 | Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur |
| K-104 | 149 | Sıkıştırma bağımlılığı için yeni paket alınmadı |
| K-105 | 150 | `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında 👤 |
| K-106 | 151 | Sıkıştırma varsayılan kapalıdır 👤 |
| K-107 | 152 | Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez 👤 |
| K-108 | 153 | Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir |
| K-109 | 154 | `Pipeline` sırası sabittir: ToolResult → SlidingWindow → Summarization |
| K-110 | 155 | `TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır; bu faz `InMemoryAgentFileStore` |
| K-111 | 156 | İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans |
| K-112 | 157 | `attachments.session_id` yabancı anahtar DEĞİLDİR |
| K-113 | 158 | Ek türü sihirli bayta göre doğrulanır; istemcinin bildirdiği `Content-Type` yok sayılır |
| K-114 | 159 | Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur |
| K-115 | 160 | `IFormFile` alan minimal API uçları `.DisableAntiforgery()` gerektirir |
| K-116 | 161 | `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez |
| K-117 | 162 | Kalıcı agent dosya belleği (`agent_files`) aynı migration'da (0006) eklendi 👤 |
| K-118 | 163 | Workflow yürütmesi ayrı pakette (`AgentPrism.Workflows`), `AspNetCore` bu pakete referans vermez |
| K-119 | 164 | Beş desenin tamamı Faz 15'te uygulandı 👤 |
| K-120 | 165 | Workflow çalıştırmaları `runs` tablosunda yaşar; `workflow_runs` açılmadı |
| K-121 | 166 | Kontrol noktası durumu `json` sütununda, `jsonb` DEĞİL |
| K-122 | 167 | 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir |
| K-123 | 168 | Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar |
| K-124 | 169 | `Magentic` plan onayı Faz 15'te kapalı (`RequirePlanSignoff(false)`) |
| K-125 | 170 | `GroupChat` yönetici agent kabul etmez; `managerAgentName` yalnız `Magentic` içindir |
| K-126 | 171 | Workflow tanımlarında sürüm GEÇMİŞİ tutulmaz |
| K-127 | 172 | 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır |
| K-128 | 173 | Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar |
| K-129 | 174 | `Microsoft.Agents.AI.Workflows.Declarative` alınmadı 👤 |
| K-130 | 175 | Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar |
| K-131 | 176 | Graf tanımdan değil, DERLENMİŞ workflow'dan çıkarılır |
| K-132 | 177 | Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı |
| K-133 | 178 | Graf hatası çalıştırmayı `Failed` yapar |
| K-134 | 179 | `WorkflowJobHandler` ayrı bir pakete değil, `AgentPrism.Core`'a konur |
| K-135 | 180 | Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil |
| K-136 | 181 | Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır |
| K-137 | 182 | İş iptali ayrı bir `cancel_requested` sütunu gerektirmez; `jobs.status` tek gerçek kaynaktır |
| K-138 | 183 | `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti |
| K-139 | 184 | Eval (Faz 18) için yeni bir NuGet paketi eklenmedi |
| K-140 | 185 | AI yargıç (`AIJudgeLoopEvaluator`/`LoopAgent`) Faz 18 kapsamı dışında bırakıldı — (yeniden açıldı: 2026-08-07, Faz 49 ile karşılandı, bkz. K-327) 🔁 |
| K-141 | 186 | Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi 👤 |
| K-142 | 187 | `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir |
| K-143 | 188 | Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context) |
| K-144 | 189 | Deney atama anahtarı varsayılan olarak oturum kimliğidir; `Experiment.AssignmentKey` bu fazda rezerve 👤 |
| K-145 | 190 | `EvalRunTriggerRequest.AgentVersion` eklendi; eval, A/B deneyinden tamamen bağımsız bir mekanizmadır 👤 |
| K-146 | 191 | `agentprism.agent.version` metrik/span etiketi varsayılan açıktır (`IncludeAgentVersionTag = true`); `experiment_id`/`variant` hiçbir zaman etiket olmaz 👤 |
| K-147 | 192 | A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür |
| K-148 | 193 | Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi |
| K-149 | 194 | `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı |
| K-150 | 195 | Maliyette para birimi dönüşümü yapılmaz 👤 |
| K-151 | 196 | Ağaç maliyeti kendi maliyetiyle toplanmaz; iki ayrı alan 👤 |
| K-152 | 197 | `/api/stats` Eval/Workflow'u hariç tutmaya devam eder (K-141); yeni `/api/stats/timeseries` bilerek hariç TUTMAZ 👤 |
| K-153 | 198 | `POST /api/stats/recalculate-costs` eklendi: Admin + denetim izi 👤 |
| K-154 | 199 | Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır |
| K-155 | 200 | `AgentPrism:Pricing` `AgentPrismOptions`'ta (Core), bir saglayici paketinde değil |
| K-156 | 201 | Dashboard'un devre kesici uyarısı mevcut `/api/models/health` `Unhealthy` durumunu kullanır; yeni uç eklenmedi |
| K-157 | 202 | `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı |
| K-158 | 203 | Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında |
| K-159 | 204 | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir |
| K-160 | 205 | Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi 👤 |
| K-161 | 206 | Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez 👤 |
| K-162 | 207 | Kota aşımında devam eden çalıştırma KESİLMEZ; yalnızca yeni çalıştırma reddedilir 👤 |
| K-163 | 208 | Webhook imzası zaman damgasını İÇERİR |
| K-164 | 209 | SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz |
| K-165 | 210 | Hız sınırının varsayılanı KAPALIDIR 👤 |
| K-166 | 211 | `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür |
| K-167 | 212 | `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil |
| K-168 | 213 | MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok |
| K-169 | 214 | MCP OAuth geri dönüş adresi (`OAuthCallbackBaseUri`) sabit bir ayardır, istekten türetilmez |
| K-170 | 215 | MCP OAuth token'ları `(kiracı, sunucu)` başına tek bellek içi önbellekte, iki tüketici arasında paylaşılır |
| K-171 | 216 | Arka plandaki (etkileşimsiz) OAuth denemesi hemen başarısız olur, beklemez |
| K-172 | 217 | `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir |
| K-173 | 218 | Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi |
| K-174 | 219 | Mod A kaynak okuması `AgentDefinitionCompiler`'a `IMcpResourceContextProviderFactory` soyutlamasıyla bağlanır |
| K-175 | 220 | MCP kaynak toplu okuma önbelleği `ConcurrentDictionary`'e geçirildi |
| K-176 | 221 | SQL kalıcılık mantığı `AgentPrism.Sql.Shared` altında PAYLAŞILAN KAYNAK olarak yaşar 👤 |
| K-177 | 222 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ |
| K-178 | 223 | Migration numaraları sağlayıcı başına bağımsızdır |
| K-179 | 224 | Şema adı kuralı iki sağlayıcıda AYNIDIR |
| K-180 | 225 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda |
| K-181 | 226 | `AgentPrism.SqlServer` AOT uyumlu olarak İŞARETLENMEZ |
| K-182 | 227 | Diziler SQL Server'a JSON metni olarak taşınır |
| K-183 | 228 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır |
| K-184 | 229 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez |
| K-185 | 230 | `AgentPrism` meta paketi `AgentPrism.SqlServer`'ı İÇERMEZ |
| K-186 | 231 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 |
| K-187 | 232 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti |
| K-188 | 233 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor |
| K-189 | 234 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu |
| K-190 | 235 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır |
| K-191 | 236 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ |
| K-192 | 237 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ |
| K-193 | 238 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR |
| K-194 | 239 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING` |
| K-195 | 240 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir |
| K-196 | 241 | `AgentPrism.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi) |
| K-197 | 242 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni) |
| K-198 | 243 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz |
| K-199 | 244 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı) |
| K-200 | 245 | Parti silme her sağlayıcıda farklı teknik kullanır |
| K-201 | 246 | `MaxRows` var ama uygulanmıyor (ertelendi → kapandı K-258) |
| K-202 | 247 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır |
| K-203 | 248 | `sessions`/`conversations` ayrı hedeftir |
| K-204 | 249 | Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil 👤 |
| K-205 | 250 | `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi 👤 |
| K-206 | 251 | İçerik filtresi tespiti `AgentPrism.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil 👤 |
| K-207 | 252 | Paket adı `AgentPrism.Google`, sağlayıcı adı `google` 👤 |
| K-208 | 253 | `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır 👤 |
| K-209 | 254 | `AgentPrism` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ |
| K-210 | 255 | Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı 👤 |
| K-211 | 256 | `azure-openai` hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırıktır |
| K-212 | 257 | Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık 👤 |
| K-213 | 258 | Azure'ın Responses yüzeyi desteklenmiyor |
| K-214 | 259 | `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı |
| K-215 | 260 | Ses sözleşmeleri `AgentPrism.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır |
| K-216 | 261 | `AgentPrism.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır |
| K-217 | 262 | `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir |
| K-218 | 263 | Tool bağımlılıkları KURULUM anında alınır; `AIFunctionArguments.Services` MAF boru hattında boştur |
| K-219 | 264 | `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz |
| K-220 | 265 | `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz |
| K-221 | 266 | Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir |
| K-222 | 267 | Konuşma katmanı Seçenek A ile ve `AgentPrism.Core`'da |
| K-223 | 268 | `MapAgentPrism` `UseWebSockets()`'i koşullu olarak kendisi kurar |
| K-224 | 269 | WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez |
| K-225 | 270 | `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz 👤 |
| K-226 | 271 | Artımlı (geçici) transkript yok; çözüm tek atımlıdır 👤 |
| K-227 | 272 | Ses dakikası bir kota birimi değildir 👤 |
| K-228 | 273 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı |
| K-229 | 274 | `t` fonksiyonu modül düzeyindedir ve kimliği hiç değişmez |
| K-230 | 275 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır (K-047) |
| K-231 | 276 | Varsayılan dil tarayıcıdan gelir 👤 |
| K-232 | 277 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir |
| K-233 | 278 | Rozet metni küçük harf, süzgeç/başlık metni büyük harf: iki ayrı anahtar kümesi |
| K-234 | 279 | Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu |
| K-235 | 280 | Konuşma çözümlemesine dil kodu gönderilmez |
| K-236 | 281 | `--ap-subtle` ve `--ap-muted` WCAG AA'ya göre düzeltildi |
| K-237 | 282 | Klavye kısayolu metin alanında tetiklenmez; `Ctrl+Enter` yereldir |
| K-238 | 283 | Komut paleti istemci tarafında arar |
| K-239 | 284 | `run_scores`: `author` NULL'ı sağlayıcı bazlı işlenir |
| K-240 | 285 | `ScoredRuns`/`PositiveRate` iki yolla hesaplanır |
| K-241 | 286 | `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi |
| K-242 | 287 | `FeedbackControl` ikili puan gösterir, yıldız YAZILMADI |
| K-243 | 288 | Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ |
| K-244 | 289 | `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok |
| K-245 | 290 | `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır |
| K-246 | 291 | İptal isteği `run.cancel` eylemiyle denetim izine yazılır |
| K-247 | 292 | K-183'ün isareti `AgentPrism.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `AgentPrism.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı |
| K-248 | 293 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok |
| K-249 | 294 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `AgentPrism:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir |
| K-250 | 295 | `AgentPrismDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `AgentPrism.AspNetCore.AgentPrismHealthCheck` içindedir |
| K-251 | 296 | `AddAgentPrismHealthChecks()` `AddAgentPrism()`'in önceden çağrıldığını KAYIT ANINDA denetlemez |
| K-252 | 297 | Döngü denetimi `AgentCallGraph.ValidateDetailed`'e taşındı, yeni dosya yok |
| K-253 | 298 | `AgentPrismValidationOptions.McpTimeout` Core'a eklendi, AspNetCore'a değil |
| K-254 | 299 | Kota ölçerine `quota.metric` eklendi 👤 |
| K-255 | 300 | Kota ölçeri `usage`+`limit` için AYRI iki `ObservableGauge`'dur 👤 |
| K-256 | 301 | `QuotaUsageObserver` senkron kapılı önbellektir, zamanlayıcı değil |
| K-257 | 302 | Kota ölçeri yalnız KAYITLI kiracıları tarar |
| K-258 | 303 | `MaxRows` sıra, silme adımından çıkarılarak uygulandı (K-201 kapandı) |
| K-259 | 304 | Saklama korelasyonları BARE hedef adı değil, TAM NİTELENDİRİLMİŞ ad kullanır (Faz 25 hatası düzeltildi) |
| K-260 | 305 | `MaxRows` kiracı genelinde uygulanır, kiracı başına DEĞİL (Faz 36 planının Açık Soru 3'ünden sapma) |
| K-261 | 306 | `eval_case_results`/`workflow_checkpoints` için `MaxRows` eşiği İLİŞKİLİ tablo üzerinden hesaplanır (Açık Soru 2 çözüldü) |
| K-262 | 307 | Şablonda `IncludeSymbols=false` zorunlu |
| K-263 | 308 | Şablonda `TargetFrameworks` boşaltılır |
| K-264 | 309 | Şablon içeriği `<None Pack>` ile paketlenir |
| K-265 | 310 | Şablon paket sürümü varsayılanı kayan `*-*` |
| K-266 | 311 | Üretilen `OrderTools.cs` `using AgentPrism;` taşır |
| K-267 | 312 | `ModelBinding.ResponseFormat` yetenek denetimi yalnız `Json`/`JsonSchema` kiplerinde çalışır |
| K-268 | 313 | `TemplateFixture` sablon testleri icin tam cozum yerine `AgentPrism.src.slnf` (yalniz `src/` paketlerini listeleyen bir cozum filtresi) paketler |
| K-269 | 314 | `AgentPrism.Testing.FakeModelProvider` modele ozel, BIR KEZ tuketilen bir yanit kuyrugu tutar; mesaj gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz |
| K-270 | 315 | `AgentPrism.Testing` yalniz `net10.0` hedefler (cogul `TargetFrameworks` ozelligiyle ezilerek) |
| K-271 | 316 | `tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs` SILINMEDI (plandan sapma) |
| K-272 | 317 | Uç etiketleri TEK `.WithTags("AgentPrism", "<Alan>")` çağrısıyla verilir |
| K-273 | 318 | SSE/ikili yanıtlar `Produces<T>` ile tiple bildirilir; `responseType: null` içerik tipini tamamen düşürür |
| K-274 | 319 | Aynı statü koduna birden fazla `.Produces` çağrısı yapılmaz; çoklu içerik tipi TEK çağrıya `additionalContentTypes` ile yazılır |
| K-275 | 320 | On bir ham `Task<IResult>` ucunun tamamı yol B (`.Produces`/`.ProducesProblem` üstverisi) ile belgelendi; hiçbiri yol A'ya (`Results<...>` imza değişikliği) taşınmadı |
| K-276 | 321 | `docs/openapi/agentprism.json` üretim kaynağı `AgentPrism.AspNetCore.FunctionalTests`'tir, `samples/AgentPrism.Api` DEĞİL |
| K-277 | 322 | Bellek içi depolar `ITenantContext` alır; kiracı süzgeci artık isteğe bağlı değildir |
| K-278 | 323 | `sessions` birincil anahtarı `(tenant_id, id)`; oturum kimliği kiracı içinde benzersizdir |
| K-279 | 324 | Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu `tenantId` alır 👤 |
| K-280 | 325 | Çalıştırmanın alt yazmaları ambient kiracıyla süzülmez; `[TenantAgnostic]` ile gerekçesi yazılır |
| K-281 | 326 | `TenantAgnosticAttribute` `AgentPrism.Sql.Shared` içinde ve `internal`'dir |
| K-282 | 327 | Kiracı yalıtımı iki depo örneğiyle değil, değiştirilebilir tek bir kiracı bağlamıyla sınanır |
| K-283 | 328 | Görünmeyen bir oturum YOK sayılır; "başkasının oturumu" reddi kaldırıldı |
| K-284 | 329 | Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil |
| K-285 | 330 | `SingletonGuard` `public`tir; "internal yardımcı" planı uygulanamadı |
| K-286 | 331 | Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn taban); gerçek devralma ölçüldü |
| K-287 | 332 | Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil |
| K-288 | 333 | `/api/agents/{name}/run` `Idempotency-Key` varsa akışsız (JSON) çalışır; plandan sapma (kullanıcı kararı) 👤 |
| K-289 | 334 | `AgentPrismIdempotencyOptions` `AgentPrism.Core`'dadır, plandaki gibi `AgentPrism.AspNetCore`'da değil |
| K-290 | 335 | `idempotency_keys` için ayrı bir `Retention: TimeSpan` alanı yerine standart `RetentionTargets`/`AgentPrismRetentionOptions` üçlüsü kullanıldı |
| K-291 | 336 | Idempotency desteği varsayılan AÇIKTIR (`Enabled = true`); K1'in "varsayılan kapalı" kuralının bilinçli bir yorumu |
| K-292 | 337 | Ham govde, filtreden ÖNCE `MapAgentPrism` içine eklenen koşullu bir ara yazılımla tamponlanır |
| K-293 | 338 | `RunError` sınıf/parmak izini doğrudan taşır; ayrı bir arama tablosu açılmadı |
| K-294 | 339 | Sınıflandırma TEK bir noktada, `RunRecordingAgent.CompleteAsync` içinde, `error` `null` değilse çalışır |
| K-295 | 340 | `ByErrorClass` ayrı bir depo metodu değil, `GetStatisticsAsync`'in genişletilmiş sonucu; `/api/stats/errors` o sonucun dar bir dilimi |
| K-296 | 341 | Hata sınıflandırıcının SDK istisna adları örnek uygulamada gerçek bir OpenAI hatasıyla ölçüldü; `ClientResultException` eksikti |
| K-297 | 342 | `quota_exceeded` sınıfı otomatik sınıflandırıcı için YAPISAL olarak ulaşılamazdır; taksonomide kalır ama örnek uygulamada uçtan uca gösterilemedi |
| K-298 | 343 | Parmak izi normalleştirmesi tırnak içi metni SİLMEZ (Açık Soru 3 → C) |
| K-299 | 344 | Sınıf başına en sık üç küme, pencere fonksiyonlarıyla (`ROW_NUMBER()`/`COUNT() OVER`) tek geçişte hesaplanır — bu desenin kod tabanındaki İLK kullanımı |
| K-300 | 345 | Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur; plan taslağının "run_events zaten kullanıcı girdisini taşır" iddiası yanlıştı ve düzeltildi (kullanıcı kararı) 👤 |
| K-301 | 346 | Çok turluluk, "bu oturumda DAHA ÖNCE başlamış başka bir çalıştırma var mı" sorusuyla belirlenir; tam konuşma geçmişi okunmaz |
| K-302 | 347 | `AddCaseAsync`'in `seq`/`source_run_id` eşzamanlılığı `ON CONFLICT`/`MERGE` değil, düz `INSERT` + `SqlDialect.IsUniqueViolation` yakalama + yeniden deneme ile çözülür |
| K-303 | 348 | "Olumsuz puan" otomatik terfi tetikleyicisi olarak `Binary` için `Value == 0`, `Stars` için `Value <= 2` (5 üzerinden) tanımlandı |
| K-304 | 349 | Kuyruğa alınan (`Prefer: respond-async`) bir çalıştırmanın `runs` satırı, işçinin gerçek yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync` bu yüzden bir UPSERT'tir |
| K-305 | 350 | Kuyruğa alınan bir çalıştırmada `JobRecord.Id` ile `RunRecord.Id` bilinçli olarak AYNI değeri taşır |
