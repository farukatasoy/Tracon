# KARARLAR — İndeks Arşivi

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme). Yeni kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).

## Arşivlenen Kararlar (736 kalem)

| K | Satır | Karar |
|---|---|---|
| K-001 | 48 | Modüler paket ailesi + meta paket 👤 |
| K-002 | 49 | Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü 👤 |
| K-003 | 50 | Hibrit agent tanımı: kod + çalışma anı veritabanı 👤 |
| K-004 | 51 | Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner 👤 |
| K-005 | 52 | Hedef framework `net8.0;net9.0;net10.0` 👤 |
| K-006 | 53 | Trim/AOT uyumu katman bazlı |
| K-007 | 54 | Geçişli sabitleme kapalı |
| K-008 | 55 | Ön sürüm MAF bağımlılığı yalnız `Tracon.AspNetCore` içinde |
| K-009 | 56 | Sırlar `dotnet user-secrets` ile 👤 |
| K-010 | 57 | Arayüz erişimi üç katmanlı 👤 |
| K-011 | 58 | Klasör düzeni `src/` + `samples/` + `tests/` 👤 |
| K-012 | 59 | Tool'lar yalnız kodda tanımlanır |
| K-013 | 60 | Ayrı `tracon` PostgreSQL şeması |
| K-014 | 61 | `run_events` append-only |
| K-015 | 62 | Birincil anahtarlar `uuid` v7 |
| K-016 | 63 | Public API takibi Faz 7'ye ertelendi 🔁 |
| K-017 | 64 | Sürümleme MinVer ile git etiketinden |
| K-018 | 65 | Bellek içi store'lar birinci sınıf implementasyon |
| K-019 | 66 | Agent kaynakları `IAgentSource` ile soyutlandı |
| K-020 | 67 | `MAAI001` bastırması tek dosyada toplandı |
| K-021 | 68 | Yapılandırma elle bağlanır, `Bind()` kullanılmaz |
| K-022 | 69 | Çalıştırma olayı sıra numarasını yazıcı üretir, depo değil |
| K-023 | 70 | Kendi UUIDv7 üretecimiz (`TraconId`) |
| K-024 | 71 | `IAgentDecorator` genişleme noktası |
| K-025 | 72 | `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil |
| K-026 | 73 | Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil 👤 |
| K-027 | 74 | Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil |
| K-028 | 75 | Sağlayıcı ve depolama ayarları kendi alt bölümlerinde 👤 |
| K-029 | 76 | Şema adı SQL metnine gömülür, katı doğrulamadan sonra |
| K-030 | 77 | Responses API her zaman sunucu tarafı depolama kapalı çalışır |
| K-031 | 78 | `OPENAI001` bastırması tek dosyada toplandı |
| K-032 | 79 | Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur 👤 |
| K-033 | 80 | Tool taraması açık işaretleme ister (`[TraconTool]`) 👤 |
| K-034 | 81 | `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir 👤 |
| K-035 | 82 | Ayar sınıfları `record` olamaz |
| K-036 | 83 | OpenAI uyumlu uçlar Tracon tarafından yazılır, MAF'ın `Map*` uçlarıyla değil 👤 |
| K-037 | 84 | `ChatHistoryProvider` `AddTracon()` içinde açıkça kaydedilir |
| K-038 | 85 | `/v1/*` uçları OpenAI hata biçimini kullanır, `ProblemDetails` değil |
| K-039 | 86 | Kütüphane OpenAPI üretimini dayatmaz, yalnız üstveri taşır |
| K-040 | 87 | Enum'lar JSON'da ad olarak yazılır |
| K-041 | 88 | Çalıştırma özeti depoda hesaplanır |
| K-042 | 89 | `MapTracon()` yalnız korumalı grubun convention builder'ını döndürür |
| K-043 | 90 | Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur |
| K-044 | 91 | Çalıştırma kimliğini çağıran üretir (`TraconRunOptions`) 👤 |
| K-045 | 92 | Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı 👤 |
| K-046 | 93 | Arayüz kabuğu bearer token katmanından muaftır 👤 |
| K-047 | 94 | Arayüz token'ı `sessionStorage`'da tutulur 👤 |
| K-048 | 95 | Arayüz varlıkları Brotli sıkıştırılmış gömülür |
| K-049 | 96 | `ITraconUiProvider` tek metotlu tutuldu |
| K-050 | 97 | Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır |
| K-051 | 98 | `Tracon.UI.csproj` SDK'yı açık `Import` ile yükler |
| K-052 | 99 | Frontend birim testleri `npm run build` içinde koşar |
| K-053 | 100 | `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı |
| K-054 | 101 | Faz 6 kapsamı daraltıldı: Workflows ertelendi 👤 |
| K-055 | 102 | Span'ler `ActivityListener` ile toplanır, exporter ile değil |
| K-056 | 103 | Örnekleme kararı çalıştırma bittiğinde verilir |
| K-057 | 104 | `ModelContextProtocol.Core`, tam `ModelContextProtocol` paketi değil |
| K-058 | 105 | MCP'de yalnızca uzak HTTP aktarımı; stdio yok |
| K-059 | 106 | MCP kimlik doğrulama değeri veritabanında saklanmaz |
| K-060 | 107 | MCP tool adları `{sunucu}_{tool}`; nokta kullanılmaz |
| K-061 | 108 | "Bir daha sorma" MAF'ın biçimiyle değil Tracon deposunda tutulur |
| K-062 | 109 | Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı |
| K-063 | 110 | `run_events` partition'ı açılmadı (kapandı → K-199) |
| K-064 | 111 | İkinci faz önceliği: yetenek derinliği 👤 |
| K-065 | 112 | Ses: hedef gerçek zamanlı konuşma katmanı 👤 |
| K-066 | 113 | Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası 👤 |
| K-067 | 114 | Alt agent çalıştırması ayrı bir `runs` satırıdır 👤 |
| K-068 | 115 | Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz 👤🔁 |
| K-069 | 116 | `UseOpenAICompatible(ad, ...)` ayrı ad, `UseOpenAI` aşırı yüklemesi değil 👤 |
| K-070 | 117 | Model sağlayıcı devre kesici varsayılan olarak açık 👤 |
| K-071 | 118 | Sağlayıcı sağlık denetimi arka planda varsayılan olarak koşmaz 👤 |
| K-072 | 119 | Sağlayıcı adı deseni regex değil elle karakter denetimiyle doğrulanır |
| K-073 | 120 | Sağlık denetimi hata detayında `HttpRequestException.Message` değil `HttpRequestError` kategorisi kullanılır |
| K-074 | 121 | Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi |
| K-075 | 122 | Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapTracon()` çağrısında bir kez yapılır |
| K-076 | 123 | Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil |
| K-077 | 124 | Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır |
| K-078 | 125 | `/api/meta` yanıtına rol bilgisi (`roles`) eklendi |
| K-079 | 126 | `mcp.refresh` denetim izine uç katmanında yazılır, depo dekoratöründe değil |
| K-080 | 127 | Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil |
| K-081 | 128 | Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler |
| K-082 | 129 | Skill sürüm geçmişi tutulmaz 👤 |
| K-083 | 130 | `AllowedTools` yalnız saklanır, zorlanmaz 👤 |
| K-084 | 131 | Bir agent için varsayılan skill sınırı 10'dur 👤 |
| K-085 | 132 | Derlenmiş agent cache anahtarında skill parmak izi vardır |
| K-086 | 133 | Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz |
| K-087 | 134 | Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir 👤 |
| K-088 | 135 | Yorumlayıcı beyaz listesi varsayılan olarak boştur |
| K-089 | 136 | Denetim izine yazılamayan bir script çalıştırılmaz |
| K-090 | 137 | Argüman doğrulaması sığdır; tam JSON Schema doğrulayıcı eklenmedi |
| K-091 | 138 | Argümanlar script sürecine stdin ile geçirilir |
| K-092 | 139 | İzin kaydı silinmez, `revoked_at` ile iptal edilir |
| K-093 | 140 | Alt çalıştırma ayrı bir `runs` satırıdır |
| K-094 | 141 | `root_run_id` denormalize edilir |
| K-095 | 142 | `runs.parent_run_id` için yabancı anahtar konmaz |
| K-096 | 143 | Bütçe ağaç boyunca tek nesnedir; `AgentRunBudget` bilerek `class`'tır |
| K-097 | 144 | Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır |
| K-098 | 145 | Alt agent gec (lazy) çözülür; derleme anında çözülmez |
| K-099 | 146 | Trace tamponunun sahibi yalnız kök çalıştırmadır |
| K-100 | 147 | `RunQuery.OnlyRootRuns` varsayılanı `true` 👤 |
| K-101 | 148 | Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma 👤 |
| K-102 | 149 | Alt çalıştırmalar kök akışa yalnız özet olay yazar 👤 |
| K-103 | 150 | Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur |
| K-104 | 151 | Sıkıştırma bağımlılığı için yeni paket alınmadı |
| K-105 | 152 | `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında 👤 |
| K-106 | 153 | Sıkıştırma varsayılan kapalıdır 👤 |
| K-107 | 154 | Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez 👤 |
| K-108 | 155 | Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir |
| K-109 | 156 | `Pipeline` sırası sabittir: ToolResult → SlidingWindow → Summarization |
| K-110 | 157 | `TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır; bu faz `InMemoryAgentFileStore` |
| K-111 | 158 | İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans |
| K-112 | 159 | `attachments.session_id` yabancı anahtar DEĞİLDİR |
| K-113 | 160 | Ek türü sihirli bayta göre doğrulanır; istemcinin bildirdiği `Content-Type` yok sayılır |
| K-114 | 161 | Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur |
| K-115 | 162 | `IFormFile` alan minimal API uçları `.DisableAntiforgery()` gerektirir |
| K-116 | 163 | `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez |
| K-117 | 164 | Kalıcı agent dosya belleği (`agent_files`) aynı migration'da (0006) eklendi 👤 |
| K-118 | 165 | Workflow yürütmesi ayrı pakette (`Tracon.Workflows`), `AspNetCore` bu pakete referans vermez |
| K-119 | 166 | Beş desenin tamamı Faz 15'te uygulandı 👤 |
| K-120 | 167 | Workflow çalıştırmaları `runs` tablosunda yaşar; `workflow_runs` açılmadı |
| K-121 | 168 | Kontrol noktası durumu `json` sütununda, `jsonb` DEĞİL |
| K-122 | 169 | 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir |
| K-123 | 170 | Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar |
| K-124 | 171 | `Magentic` plan onayı Faz 15'te kapalı (`RequirePlanSignoff(false)`) |
| K-125 | 172 | `GroupChat` yönetici agent kabul etmez; `managerAgentName` yalnız `Magentic` içindir |
| K-126 | 173 | Workflow tanımlarında sürüm GEÇMİŞİ tutulmaz |
| K-127 | 174 | 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır |
| K-128 | 175 | Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar |
| K-129 | 176 | `Microsoft.Agents.AI.Workflows.Declarative` alınmadı 👤 |
| K-130 | 177 | Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar |
| K-131 | 178 | Graf tanımdan değil, DERLENMİŞ workflow'dan çıkarılır |
| K-132 | 179 | Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı |
| K-133 | 180 | Graf hatası çalıştırmayı `Failed` yapar |
| K-134 | 181 | `WorkflowJobHandler` ayrı bir pakete değil, `Tracon.Core`'a konur |
| K-135 | 182 | Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil |
| K-136 | 183 | Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır |
| K-137 | 184 | İş iptali ayrı bir `cancel_requested` sütunu gerektirmez; `jobs.status` tek gerçek kaynaktır |
| K-138 | 185 | `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti |
| K-139 | 186 | Eval (Faz 18) için yeni bir NuGet paketi eklenmedi |
| K-140 | 187 | AI yargıç (`AIJudgeLoopEvaluator`/`LoopAgent`) Faz 18 kapsamı dışında bırakıldı — (yeniden açıldı: 2026-08-07, Faz 49 ile karşılandı, bkz. K-327) 🔁 |
| K-141 | 188 | Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi 👤 |
| K-142 | 189 | `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir |
| K-143 | 190 | Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context) |
| K-144 | 191 | Deney atama anahtarı varsayılan olarak oturum kimliğidir; `Experiment.AssignmentKey` bu fazda rezerve 👤 |
| K-145 | 192 | `EvalRunTriggerRequest.AgentVersion` eklendi; eval, A/B deneyinden tamamen bağımsız bir mekanizmadır 👤 |
| K-146 | 193 | `tracon.agent.version` metrik/span etiketi varsayılan açıktır (`IncludeAgentVersionTag = true`); `experiment_id`/`variant` hiçbir zaman etiket olmaz 👤 |
| K-147 | 194 | A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür |
| K-148 | 195 | Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi |
| K-149 | 196 | `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı |
| K-150 | 197 | Maliyette para birimi dönüşümü yapılmaz 👤 |
| K-151 | 198 | Ağaç maliyeti kendi maliyetiyle toplanmaz; iki ayrı alan 👤 |
| K-152 | 199 | `/api/stats` Eval/Workflow'u hariç tutmaya devam eder (K-141); yeni `/api/stats/timeseries` bilerek hariç TUTMAZ 👤 |
| K-153 | 200 | `POST /api/stats/recalculate-costs` eklendi: Admin + denetim izi 👤 |
| K-154 | 201 | Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır |
| K-155 | 202 | `Tracon:Pricing` `TraconOptions`'ta (Core), bir saglayici paketinde değil |
| K-156 | 203 | Dashboard'un devre kesici uyarısı mevcut `/api/models/health` `Unhealthy` durumunu kullanır; yeni uç eklenmedi |
| K-157 | 204 | `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı |
| K-158 | 205 | Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında |
| K-159 | 206 | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir |
| K-160 | 207 | Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi 👤 |
| K-161 | 208 | Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez 👤 |
| K-162 | 209 | Kota aşımında devam eden çalıştırma KESİLMEZ; yalnızca yeni çalıştırma reddedilir 👤 |
| K-163 | 210 | Webhook imzası zaman damgasını İÇERİR |
| K-164 | 211 | SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz |
| K-165 | 212 | Hız sınırının varsayılanı KAPALIDIR 👤 |
| K-166 | 213 | `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür |
| K-167 | 214 | `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil |
| K-168 | 215 | MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok |
| K-169 | 216 | MCP OAuth geri dönüş adresi (`OAuthCallbackBaseUri`) sabit bir ayardır, istekten türetilmez |
| K-170 | 217 | MCP OAuth token'ları `(kiracı, sunucu)` başına tek bellek içi önbellekte, iki tüketici arasında paylaşılır |
| K-171 | 218 | Arka plandaki (etkileşimsiz) OAuth denemesi hemen başarısız olur, beklemez |
| K-172 | 219 | `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir |
| K-173 | 220 | Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi |
| K-174 | 221 | Mod A kaynak okuması `AgentDefinitionCompiler`'a `IMcpResourceContextProviderFactory` soyutlamasıyla bağlanır |
| K-175 | 222 | MCP kaynak toplu okuma önbelleği `ConcurrentDictionary`'e geçirildi |
| K-176 | 223 | SQL kalıcılık mantığı `Tracon.Sql.Shared` altında PAYLAŞILAN KAYNAK olarak yaşar 👤 |
| K-177 | 224 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ |
| K-178 | 225 | Migration numaraları sağlayıcı başına bağımsızdır |
| K-179 | 226 | Şema adı kuralı iki sağlayıcıda AYNIDIR |
| K-180 | 227 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda |
| K-181 | 228 | `Tracon.SqlServer` AOT uyumlu olarak İŞARETLENMEZ |
| K-182 | 229 | Diziler SQL Server'a JSON metni olarak taşınır |
| K-183 | 230 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır |
| K-184 | 231 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez |
| K-185 | 232 | `Tracon` meta paketi `Tracon.SqlServer`'ı İÇERMEZ |
| K-186 | 233 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 |
| K-187 | 234 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti |
| K-188 | 235 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor |
| K-189 | 236 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu |
| K-190 | 237 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır |
| K-191 | 238 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ |
| K-192 | 239 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ |
| K-193 | 240 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR |
| K-194 | 241 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING` |
| K-195 | 242 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir |
| K-196 | 243 | `Tracon.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi) |
| K-197 | 244 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni) |
| K-198 | 245 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz |
| K-199 | 246 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı) |
| K-200 | 247 | Parti silme her sağlayıcıda farklı teknik kullanır |
| K-201 | 248 | `MaxRows` var ama uygulanmıyor (ertelendi → kapandı K-258) |
| K-202 | 249 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır |
| K-203 | 250 | `sessions`/`conversations` ayrı hedeftir |
| K-204 | 251 | Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil 👤 |
| K-205 | 252 | `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi 👤 |
| K-206 | 253 | İçerik filtresi tespiti `Tracon.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil 👤 |
| K-207 | 254 | Paket adı `Tracon.Google`, sağlayıcı adı `google` 👤 |
| K-208 | 255 | `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır 👤 |
| K-209 | 256 | `Tracon` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ |
| K-210 | 257 | Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı 👤 |
| K-211 | 258 | `azure-openai` hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırıktır |
| K-212 | 259 | Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık 👤 |
| K-213 | 260 | Azure'ın Responses yüzeyi desteklenmiyor |
| K-214 | 261 | `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı |
| K-215 | 262 | Ses sözleşmeleri `Tracon.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır |
| K-216 | 263 | `Tracon.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır |
| K-217 | 264 | `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir |
| K-218 | 265 | Tool bağımlılıkları KURULUM anında alınır; `AIFunctionArguments.Services` MAF boru hattında boştur |
| K-219 | 266 | `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz |
| K-220 | 267 | `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz |
| K-221 | 268 | Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir |
| K-222 | 269 | Konuşma katmanı Seçenek A ile ve `Tracon.Core`'da |
| K-223 | 270 | `MapTracon` `UseWebSockets()`'i koşullu olarak kendisi kurar |
| K-224 | 271 | WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez |
| K-225 | 272 | `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz 👤 |
| K-226 | 273 | Artımlı (geçici) transkript yok; çözüm tek atımlıdır 👤 |
| K-227 | 274 | Ses dakikası bir kota birimi değildir 👤 |
| K-228 | 275 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı |
| K-229 | 276 | `t` fonksiyonu modül düzeyindedir ve kimliği hiç değişmez |
| K-230 | 277 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır (K-047) |
| K-231 | 278 | Varsayılan dil tarayıcıdan gelir 👤 |
| K-232 | 279 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir |
| K-233 | 280 | Rozet metni küçük harf, süzgeç/başlık metni büyük harf: iki ayrı anahtar kümesi |
| K-234 | 281 | Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu |
| K-235 | 282 | Konuşma çözümlemesine dil kodu gönderilmez |
| K-236 | 283 | `--ap-subtle` ve `--ap-muted` WCAG AA'ya göre düzeltildi |
| K-237 | 284 | Klavye kısayolu metin alanında tetiklenmez; `Ctrl+Enter` yereldir |
| K-238 | 285 | Komut paleti istemci tarafında arar |
| K-239 | 286 | `run_scores`: `author` NULL'ı sağlayıcı bazlı işlenir |
| K-240 | 287 | `ScoredRuns`/`PositiveRate` iki yolla hesaplanır |
| K-241 | 288 | `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi |
| K-242 | 289 | `FeedbackControl` ikili puan gösterir, yıldız YAZILMADI |
| K-243 | 290 | Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ |
| K-244 | 291 | `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok |
| K-245 | 292 | `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır |
| K-246 | 293 | İptal isteği `run.cancel` eylemiyle denetim izine yazılır |
| K-247 | 294 | K-183'ün isareti `Tracon.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `Tracon.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı |
| K-248 | 295 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok |
| K-249 | 296 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `Tracon:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir |
| K-250 | 297 | `TraconDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `Tracon.AspNetCore.TraconHealthCheck` içindedir |
| K-251 | 298 | `AddTraconHealthChecks()` `AddTracon()`'in önceden çağrıldığını KAYIT ANINDA denetlemez |
| K-252 | 299 | Döngü denetimi `AgentCallGraph.ValidateDetailed`'e taşındı, yeni dosya yok |
| K-253 | 300 | `TraconValidationOptions.McpTimeout` Core'a eklendi, AspNetCore'a değil |
| K-254 | 301 | Kota ölçerine `quota.metric` eklendi 👤 |
| K-255 | 302 | Kota ölçeri `usage`+`limit` için AYRI iki `ObservableGauge`'dur 👤 |
| K-256 | 303 | `QuotaUsageObserver` senkron kapılı önbellektir, zamanlayıcı değil |
| K-257 | 304 | Kota ölçeri yalnız KAYITLI kiracıları tarar |
| K-258 | 305 | `MaxRows` sıra, silme adımından çıkarılarak uygulandı (K-201 kapandı) |
| K-259 | 306 | Saklama korelasyonları BARE hedef adı değil, TAM NİTELENDİRİLMİŞ ad kullanır (Faz 25 hatası düzeltildi) |
| K-260 | 307 | `MaxRows` kiracı genelinde uygulanır, kiracı başına DEĞİL (Faz 36 planının Açık Soru 3'ünden sapma) |
| K-261 | 308 | `eval_case_results`/`workflow_checkpoints` için `MaxRows` eşiği İLİŞKİLİ tablo üzerinden hesaplanır (Açık Soru 2 çözüldü) |
| K-262 | 309 | Şablonda `IncludeSymbols=false` zorunlu |
| K-263 | 310 | Şablonda `TargetFrameworks` boşaltılır |
| K-264 | 311 | Şablon içeriği `<None Pack>` ile paketlenir |
| K-265 | 312 | Şablon paket sürümü varsayılanı kayan `*-*` |
| K-266 | 313 | Üretilen `OrderTools.cs` `using Tracon;` taşır |
| K-267 | 314 | `ModelBinding.ResponseFormat` yetenek denetimi yalnız `Json`/`JsonSchema` kiplerinde çalışır |
| K-268 | 315 | `TemplateFixture` sablon testleri icin tam cozum yerine `Tracon.src.slnf` (yalniz `src/` paketlerini listeleyen bir cozum filtresi) paketler |
| K-269 | 316 | `Tracon.Testing.FakeModelProvider` modele ozel, BIR KEZ tuketilen bir yanit kuyrugu tutar; mesaj gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz |
| K-270 | 317 | `Tracon.Testing` yalniz `net10.0` hedefler (cogul `TargetFrameworks` ozelligiyle ezilerek) 🔁 |
| K-271 | 318 | `tests/Tracon.Core.UnitTests/Fakes/FakeModelProvider.cs` SILINMEDI (plandan sapma) |
| K-272 | 319 | Uç etiketleri TEK `.WithTags("Tracon", "<Alan>")` çağrısıyla verilir |
| K-273 | 320 | SSE/ikili yanıtlar `Produces<T>` ile tiple bildirilir; `responseType: null` içerik tipini tamamen düşürür |
| K-274 | 321 | Aynı statü koduna birden fazla `.Produces` çağrısı yapılmaz; çoklu içerik tipi TEK çağrıya `additionalContentTypes` ile yazılır |
| K-275 | 322 | On bir ham `Task<IResult>` ucunun tamamı yol B (`.Produces`/`.ProducesProblem` üstverisi) ile belgelendi; hiçbiri yol A'ya (`Results<...>` imza değişikliği) taşınmadı |
| K-276 | 323 | `docs/openapi/tracon.json` üretim kaynağı `Tracon.AspNetCore.FunctionalTests`'tir, `samples/Tracon.Api` DEĞİL |
| K-277 | 324 | Bellek içi depolar `ITenantContext` alır; kiracı süzgeci artık isteğe bağlı değildir |
| K-278 | 325 | `sessions` birincil anahtarı `(tenant_id, id)`; oturum kimliği kiracı içinde benzersizdir |
| K-279 | 326 | Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu `tenantId` alır 👤 |
| K-280 | 327 | Çalıştırmanın alt yazmaları ambient kiracıyla süzülmez; `[TenantAgnostic]` ile gerekçesi yazılır |
| K-281 | 328 | `TenantAgnosticAttribute` `Tracon.Sql.Shared` içinde ve `internal`'dir |
| K-282 | 329 | Kiracı yalıtımı iki depo örneğiyle değil, değiştirilebilir tek bir kiracı bağlamıyla sınanır |
| K-283 | 330 | Görünmeyen bir oturum YOK sayılır; "başkasının oturumu" reddi kaldırıldı |
| K-284 | 331 | Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil |
| K-285 | 332 | `SingletonGuard` `public`tir; "internal yardımcı" planı uygulanamadı 🔁 |
| K-286 | 333 | Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn taban); gerçek devralma ölçüldü |
| K-287 | 334 | Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil |
| K-288 | 335 | `/api/agents/{name}/run` `Idempotency-Key` varsa akışsız (JSON) çalışır; plandan sapma (kullanıcı kararı) 👤 |
| K-289 | 336 | `TraconIdempotencyOptions` `Tracon.Core`'dadır, plandaki gibi `Tracon.AspNetCore`'da değil |
| K-290 | 337 | `idempotency_keys` için ayrı bir `Retention: TimeSpan` alanı yerine standart `RetentionTargets`/`TraconRetentionOptions` üçlüsü kullanıldı |
| K-291 | 338 | Idempotency desteği varsayılan AÇIKTIR (`Enabled = true`); K1'in "varsayılan kapalı" kuralının bilinçli bir yorumu |
| K-292 | 339 | Ham govde, filtreden ÖNCE `MapTracon` içine eklenen koşullu bir ara yazılımla tamponlanır |
| K-293 | 340 | `RunError` sınıf/parmak izini doğrudan taşır; ayrı bir arama tablosu açılmadı |
| K-294 | 341 | Sınıflandırma TEK bir noktada, `RunRecordingAgent.CompleteAsync` içinde, `error` `null` değilse çalışır |
| K-295 | 342 | `ByErrorClass` ayrı bir depo metodu değil, `GetStatisticsAsync`'in genişletilmiş sonucu; `/api/stats/errors` o sonucun dar bir dilimi |
| K-296 | 343 | Hata sınıflandırıcının SDK istisna adları örnek uygulamada gerçek bir OpenAI hatasıyla ölçüldü; `ClientResultException` eksikti |
| K-297 | 344 | `quota_exceeded` sınıfı otomatik sınıflandırıcı için YAPISAL olarak ulaşılamazdır; taksonomide kalır ama örnek uygulamada uçtan uca gösterilemedi |
| K-298 | 345 | Parmak izi normalleştirmesi tırnak içi metni SİLMEZ (Açık Soru 3 → C) |
| K-299 | 346 | Sınıf başına en sık üç küme, pencere fonksiyonlarıyla (`ROW_NUMBER()`/`COUNT() OVER`) tek geçişte hesaplanır — bu desenin kod tabanındaki İLK kullanımı |
| K-300 | 347 | Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur; plan taslağının "run_events zaten kullanıcı girdisini taşır" iddiası yanlıştı ve düzeltildi (kullanıcı kararı) 👤 |
| K-301 | 348 | Çok turluluk, "bu oturumda DAHA ÖNCE başlamış başka bir çalıştırma var mı" sorusuyla belirlenir; tam konuşma geçmişi okunmaz |
| K-302 | 349 | `AddCaseAsync`'in `seq`/`source_run_id` eşzamanlılığı `ON CONFLICT`/`MERGE` değil, düz `INSERT` + `SqlDialect.IsUniqueViolation` yakalama + yeniden deneme ile çözülür |
| K-303 | 350 | "Olumsuz puan" otomatik terfi tetikleyicisi olarak `Binary` için `Value == 0`, `Stars` için `Value <= 2` (5 üzerinden) tanımlandı |
| K-304 | 351 | Kuyruğa alınan (`Prefer: respond-async`) bir çalıştırmanın `runs` satırı, işçinin gerçek yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync` bu yüzden bir UPSERT'tir |
| K-305 | 352 | Kuyruğa alınan bir çalıştırmada `JobRecord.Id` ile `RunRecord.Id` bilinçli olarak AYNI değeri taşır |
| K-306 | 353 | `TraconAsyncRunOptions.MaxAttempts` varsayılanı `1`'dir (kullanıcı kararı) 👤 |
| K-307 | 354 | `TraconAsyncRunOptions.Enabled` varsayılanı `true`'dur (kullanıcı kararı) 👤 |
| K-308 | 355 | Çalıştırmanın girdisi AYRI bir `run_inputs` tablosunda ve `json` sütununda saklanır; `runs`'a sütun EKLENMEZ |
| K-309 | 356 | Varsayılan tool modu `ReplayTools`; kayıtlı sonucu olmayan bir çağrı yeniden oynatmayı DURDURUR ve `422` döner |
| K-310 | 357 | `LiveTools` `Admin` rolü ister ve onay gerektiren bir tool taşıyan agent bu modda çalıştırılamaz (`409`) |
| K-311 | 358 | Dallanma öğeleri KOPYALAR; işaretçi zinciri reddedildi |
| K-312 | 359 | `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ |
| K-313 | 360 | Konusma dallandırma yalnız SQL sağlayıcısı açıkken çalışır; bellek içi kurulumda uç `501` döner (kullanıcı kararı) 👤 |
| K-314 | 361 | Kod kaynaklı agent'lar model bindirmesi ve `NoTools`/`ReplayTools` ile oynatılamaz; `400` döner (kullanıcı kararı) 👤 |
| K-315 | 362 | Yeniden oynatma OTURUMSUZDUR |
| K-316 | 363 | Yeniden oynatma yalnız SENKRON ve akışsızdır; kuyruğa alma bu fazın kapsamı dışındadır |
| K-317 | 364 | `azure-sql-edge` artık güvenilir bir yerel doğrulama ikamesidir; hazır-olma denetimi `sqlcmd` yerine ADO.NET ile yazılmalıdır (kullanıcı kararı) 👤 |
| K-318 | 365 | SQL Server migration'larında `ALTER TABLE ADD` ile eklenen sütunu AYNI toplu işlemde `CREATE INDEX`'te kullanmak "Invalid column name" verir; dört migration dosyası etkiliydi |
| K-319 | 366 | `EvalCaseResult.Scores` ayarlanmamışken (varsayılan `JsonValueKind.Undefined`) SQL Server'a `"[]"` yazılır, `"null"` DEĞİL — `ISJSON` kısıtı bare `null`'ı reddeder |
| K-320 | 367 | Model çağrı boru hattının TAMAMINI `ModelProviderRegistry` kurar; `IModelProvider` HAM istemci döndürür (kullanıcı kararı) 👤 |
| K-321 | 368 | İçerik guard'ı `IChatClient` katmanındadır ve tool çağrı döngüsünün İÇİNDEDİR |
| K-322 | 369 | Devre kesici içerik engellemesini ardışık hata SAYMAZ |
| K-323 | 370 | Yeni bir genişleme noktasının "varsayılan kapalı" kapısı KAYITTIR, bir `Enabled` bayrağı değildir (kullanıcı kararı) 👤 |
| K-324 | 371 | `422` yalnızca AKIŞSIZ çalıştırma dalında dönebilir (kullanıcı kararı) 👤 |
| K-325 | 372 | Engellenen veya maskelenen içerik HİÇBİR yere yazılmaz |
| K-326 | 373 | `RunErrorClass.ContentBlocked` `ContentFiltered`'dan AYRIDIR |
| K-327 | 374 | `AIJudgeLoopEvaluator` KULLANILMADI; `IRunJudge` sıfırdan yazıldı (Faz 49) |
| K-328 | 375 | Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrılır; yeni bir sütun açılmadı (Faz 49) |
| K-329 | 376 | İki kapılı varsayılan: `OnlineEvaluationOptions.Enabled = false` VE `SampleRate = 0.0` (Faz 49) |
| K-330 | 377 | Yargıcın `IChatClient`'ı guard boru hattından GEÇER; engelleme özel olarak ele alınmadı (Faz 49, D1) |
| K-331 | 378 | `RunScore.Author` yargıç puanlarında `judge:{ad}` ile BİLEREK DOLU yazılır (Faz 49) |
| K-332 | 379 | Cevrimiçi değerlendirme pencere özeti BELLEK İÇİDİR; yeni bir SQL sorgu yüzeyi açılmadı (Faz 49) |
| K-333 | 380 | `OnlineEvalJobHandler` DI'da hem `IJobHandler` hem KENDİ somut tipiyle kayıtlıdır (Faz 49) |
| K-334 | 381 | K-057 güncellendi: Tracon artık MCP istemcisi VE sunucusudur (Faz 50) |
| K-335 | 382 | A2A sunucu maliyeti yeniden ölçüldü: "+2 paket" değil "+4 paket"; iki paket plan taslağında hiç yoktu (Faz 50) |
| K-336 | 383 | A2A her disa acik agent icin AYRI bir alt yol ve AYRI bir agent karti kullanir; tekil kart varsayimi terk edildi (Faz 50) |
| K-337 | 384 | MCP/A2A dış çağrısı `ChildAgentInvoker`'ı KULLANMAZ; `ExternalAgentProxy`/`CatalogToolCallHandler` her zaman YENİ bir kök çalıştırma açar (Faz 50) |
| K-338 | 385 | MCP/A2A dış yüzeyleri erişim ayarlarını `IApplicationBuilder.Properties` üzerinden `MapTracon`'den DEVRALIR; `MapTracon` önce çağrılmalıdır (Faz 50) |
| K-339 | 386 | `ChildRunApproval` public yapıldı: üçüncü tüketici MCP/A2A dış çağrı katmanıdır (Faz 50) |
| K-340 | 387 | Dışa açılan bir agent'ın `AgentRunBudget.MaxDepth` değeri, kaç seviye TORUN çağrısına izin verildiğidir; "0" = hiç, "1" = bir seviye (Faz 50) |
| K-341 | 388 | Vektör gömüsü metin biçiminde (`::vector` cast) yazılır, hiçbir vektör paketi alınmaz (Faz 51) |
| K-342 | 389 | K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş (Faz 51) |
| K-343 | 390 | Anlamsal arama yalnız PostgreSQL'de uygulanır (Faz 51) 👤 |
| K-344 | 391 | Sql.Shared'in cross-provider katmanı `IVectorSearchStore` için kullanılmaz (Faz 51) |
| K-345 | 392 | `document_embeddings.metadata` sütunu `jsonb`'dir, `json` değil (Faz 51) |
| K-346 | 393 | Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues` (Faz 51) |
| K-347 | 394 | K-218 kapatıldı: `ToolMethodScanner` örnek metotları TARAMA ANINDA reddeder (Faz 52) |
| K-348 | 395 | Kaynak üreteci ayrı bir NuGet paketi değildir; `Tracon.Core` nupkg'sinde `analyzers/dotnet/cs/` altında taşınır (Faz 52) |
| K-349 | 396 | Kaynak üretecinin Roslyn sürümü `Microsoft.CodeAnalysis.CSharp` `4.8.0`'dır (Faz 52) |
| K-350 | 397 | `AddGeneratedTools()` `ITraconBuilder`'a EKLENMEDİ; derlemeye özel üretilmiş bir uzantı metodudur (Faz 52) |
| K-351 | 398 | Parametre tipi beyaz listesi `record`/`class` (composite) tipleri KAPSAMAZ; `TRC0003` ile reddedilir (Faz 52) |
| K-352 | 399 | F-76 (OpenAPI 500) YALNIZ `ProjectReference` tüketicisini etkiler; düzeltme kütüphanede değil, örnek uygulamanın derlemesindedir (2026-08-08 denetimi) |
| K-353 | 400 | `.UseMcp()` yapılandırmayı AÇIKÇA alır; Tracon kendiliğinden `IConfiguration` okumaz (2026-08-08 denetimi) |
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
| K-367 | 414 | `MapTraconMcpServer`/`MapTraconA2A` onay-yüzeyi denetimi, `Map*()` sırasında senkron çalışan bir kontrolden istek-bazlı `IEndpointFilter`e taşındı (kullanıcı kararı) 👤 |
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
| K-382 | 429 | `TraconEndpointFilter`'a `CheckTenancyWhitelist` eklendi; `AllowedTenants` doluyken listede olmayan bir aday artık istek endpoint'e ulaşmadan 403 ile reddedilir, varsayılan kiracıya SESSİZCE düşmez (manuel kabul testi hazırlığı) |
| K-383 | 430 | `Tracon.Core.csproj`'un üreteç-paketleme hedefi `@(Analyzer)` yerine Generators projesinin `GetTargetPath` çıktısını MSBuild görevi ile okur; `dotnet pack --no-build` artık `analyzers/dotnet/cs/Tracon.Generators.dll`'i İÇERİR (manuel kabul testi hazırlığı — daha önce Kritik olarak not düşülmüştü) |
| K-384 | 431 | SSE akış uçlarındaki (`AgentEndpoints`, `OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`) dar istisna filtresi kaldırıldı; `OperationCanceledException` dışındaki HER istisna artık bir `error` çerçevesine dönüşür (K-296'nın tamamlanması, manuel kabul testi hazırlığı) |
| K-385 | 432 | K-302 güncellendi: `AddCaseAsync`'in yeniden deneme döngüsüne jitter eklendi, üst sınır 5 → 10'a çıkarıldı |
| K-386 | 433 | K-317 kapandı: Docker Desktop 4.29.0 → 4.86.0 güncellemesi gerçek `mssql/server`'daki Rosetta hatasını çözdü; SQL Server sözleşme testleri bu makinede artık `azure-sql-edge` ikamesi OLMADAN, gerçek imajla koşuyor |
| K-387 | 434 | `SqlServerTestContext.DisposeAsync` artık test şemasını GERÇEKTEN bırakır (dokümantasyon iddia ediyordu, kod yapmıyordu); deadlock'a çarparsa 5 denemeli backoff ile yeniden dener |
| K-388 | 435 | `MigrationRunner.ApplyOneAsync` migration SQL'ini ve `INSERT INTO __migrations` kaydını TEK round-trip'te birleştirir; `CreateSchema`+`CreateMigrationsTable` birleşimi ve `SET XACT_ABORT ON` ile tam tek-round-trip BİLEREK yapılmadı |
| K-389 | 436 | Migration kilidi VERİTABANI genelinden SEMAYA/ONEĞE kapsandı: `SqlServerDialect`, `PostgresDialect`, `SqliteDialect` artık `sp_getapplock`/`pg_advisory_lock`/kilit dosyası anahtarını sema (veya SQLite'ta tablo öneki) adından türetir |
| K-390 | 437 | Sözleşme test izolasyonu "her test kendi şeması" modelinden "her test SINIFI kendi şeması + her test verisini `ResetDataAsync` ile sıfırlar" modeline geçti (K-389 ile birlikte) |
| K-391 | 438 | PostgreSQL: `CREATE EXTENSION IF NOT EXISTS vector` içeren `0024_vector.sql`, K-389 sonrası eş zamanlı ilk-kez migrasyonlarda benzersizlik ihlaline (`pg_extension_name_index`, SQLSTATE 23505) düşebilir — `MigrationRunner.ApplyOneAsync` bunu jitter'lı yeniden deneme ile karşılar, test fixture'ı ise uzantıyı bir kez ÖNCEDEN kurarak yarışı tamamen önler |
| K-392 | 439 | `InvariantGlobalization` hem örnek uygulamadan (`samples/Tracon.Api`) hem paket şablonundan (`Tracon.Starter`) KALDIRILDI; SQL Server desteğiyle bağdaşmıyor |
| K-393 | 440 | `A2AApprovalGuardFilter`/`McpApprovalGuardFilter`: şema hazır değilken (`AutoApplyMigrations=false`) katalog sorgusu HEMEN uygulamayı durdurmak yerine sınırsız sayıda, üstel gecikmeli (5 sn'de tavanlanan) yeniden dener; deneme SAYISI değil yalnız uygulama kapanışı sınırlar (HATA-S1-002, manuel kabul testi S1) |
| K-394 | 441 | Workflow çalıştırmaları artık kota muhasebesinden geçiyor: `WorkflowEndpoints.RunAsync` agent'larla AYNI `QuotaGate.CheckAsync` 429 kapısından geçer, `WorkflowRunner.CompleteAsync` workflow'un TAMAMINI (Depth 0, `TreeUsage`/`TreeCost` toplamından) TEK bir "run" olarak `QuotaEnforcer.RecordAsync`'e yazar (HATA-S1-006, Kritik, manuel kabul testi S1) |
| K-395 | 442 | `TraconEndpointFilter`: `requireBearerToken:false` gruplarına (eşlenmemiş `api/*` yolları, gerçek zamanlı ses ucu) Tracon'in kendi statik `AuthToken`'ıyla eşleşen bir başlık artık REDDEDİLMİYOR — başlık YOKMUŞ gibi nötr davranılıyor (HATA-S1-014, manuel kabul testi S1) |
| K-396 | 443 | `AgentDefinitionCompiler.SearchFileStoreAsync`: `TextSearchProvider`'ın doğal dil sorgusu artık `AgentFileStore.SearchAsync`'e ham regex olarak DEĞİL, boşluğa göre ayrılmış ≥3 karakterlik tokenlerin kaçışlanıp "VEYA" ile birleştirildiği bir desen olarak geçiyor (HATA-S1-009, manuel kabul testi S1) |
| K-397 | 444 | `ApiKeyScope`'a `KnowledgeRead`/`KnowledgeAdmin` eklendi; `KnowledgeEndpoints`'in tüm uçları artık `RequireApiKeyScope` çağırıyor (HATA-S1-011, Yüksek, manuel kabul testi S1) |
| K-398 | 445 | `RunRecordingAgent.RunCoreStreamingAsync`: terminal durum artık tüketicinin ERKEN `DisposeAsync()`'i (dogal bitiş değil, istisna da değil) durumunda da yazılıyor — `Canceled`, o ana kadar biriken kısmi `usage` ile (HATA-S1-015, Yüksek, manuel kabul testi S1) |
| K-399 | 446 | `TraconRetentionOptions`'a `RunInputs`/`VoiceSessions`/`RunScores` (varsayılan sırasıyla 30/30/180 gün) ve `DocumentEmbeddings` (varsayılan KAPALI, `MaxAgeDays=null`) eklendi — dört hedef daha önce `ForTarget`'ta `_ => null` dalına düşüp config varsayılanını SESSİZCE yok sayıyordu (HATA-S1-005, Düşük, manuel kabul testi S1) |
| K-400 | 447 | Skill script çalıştırma iki ayrı kök nedenle TAMAMEN çalışmıyordu: (1) resolver'sız `JsonSerializerOptions`, (2) MAF'ın nullable-ama-required arguman semasını `null` ile reddetmesi (HATA-K-002, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-401 | 448 | `WorkflowRunner.ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException` sarmalayıcılarını soyar; gerçek neden `RunError.Message`'a yazılır (HATA-K-003, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-402 | 449 | `UseWorkflows()` artık `TraconWorkflowOptions`'ı `Tracon:Workflows` bölümünden `IConfiguration`'a BAĞLIYOR (HATA-K-004, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-403 | 450 | `WorkflowRunner.RunStreamingAsync` gerçek bir `async IAsyncEnumerable` yineleyicisi yapıldı; `sessionId` doğrulaması artık SSE `event: error` üretiyor, düz `HTTP 500`'e düşmüyor (HATA-K-005, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-404 | 451 | `POST/PUT /api/agents` artık `AgentDefinitionValidator.ValidateAsync`'i SAVE ZAMANINDA çağırıyor; bilinmeyen skill/tool/callable-agent adı `400` ile reddediliyor (HATA-K-001, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-405 | 452 | `WorkflowEndpoints` artık `ApiKeyScope.WorkflowsRead`/`WorkflowsAdmin`/`RunsRead`/`RunsWrite` uyguluyor (HATA-K-006, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-406 | 453 | `BindRunRecording` artık `RecordRunInput`'ı config'ten okuyor (HATA-K-007, Yüksek, manuel kabul testi Ortak Kuyruk — aynı kök neden HATA-S2-002/HATA-S4-015'te de bağımsız bulunmuştu) |
| K-407 | 454 | `EvalEndpoints`/`ExperimentEndpoints`/`RunEndpoints` artık Eval/Experiment/`RunsRead`/`RunsWrite` kapsamlarını uyguluyor (HATA-K-008, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-408 | 455 | Kaynak dili sınırı: pakete giren veya çalışma anında çalışan her şey İngilizce'dir; geliştirme aparatı (`docs/`, `.agents/skills/`, `scripts/`) Türkçe kalır 👤 |
| K-409 | 456 | Migration `.sql` yorumları İngilizce'ye çevrildi; 61 dosyanın tamamının checksum'ı değişti — var olan bir veritabanına karşı çalıştırmadan önce şema düşürülüp yeniden kurulmalıdır 👤 |
| K-410 | 457 | `SourceLanguageTests` eklendi: taban çizgisi güdümlü, kelime sınırlı Türkçe sözlük + aksan taraması; `ProblemDetailsLanguageTests`in genelleştirilmiş kardeşi |
| K-411 | 458 | Senkronizasyon kopyası (`<ad> 2.<uzantı>`) taraması `faz-tamamlama` skill'ine KAPI olarak eklendi; kapsam `src tests samples` |
| K-412 | 459 | Doküman dizin bütçesi `docs/arsiv/` ve `docs/manuel-test/kosumlar/` HARİÇ ölçülür 👤 |
| K-413 | 460 | `docs/YOL-HARITASI.md` ÜRETİLEN dosyadır; kaynak her fazın kendi `> **Durum:**` satırıdır |
| K-414 | 461 | Manuel test koşum kaydı ile spesifikasyon AYRI dosyalarda yaşar; ikinci koşum `kosumlar/<tarih>/` kardeşi açar, üzerine yazmaz |
| K-415 | 462 | Ürün dokümantasyonu ayrı bir Astro Starlight sitesindedir (`docs-site/`), İngilizce'dir ve `farukatasoy.github.io/Tracon` adresinde yayınlanır 👤 |
| K-416 | 463 | API referansı DocFX'in `outputFormat: markdown` çıktısından üretilir ve Starlight içine gömülür; DocFX'in kendi HTML sitesi kullanılmadı |
| K-417 | 464 | HTTP API sayfaları OpenAPI belgesinden ÜRETİLİR; Scalar gömülmedi |
| K-418 | 465 | 🚨 `AddOpenApi()` ÇIPLAK çağrılmalıdır; yapılandırma `Configure<OpenApiOptions>("v1", ...)` ile AYRI kaydedilir — aksi hâlde ŞEMA XML DOKÜMANI SESSİZCE DÜŞER |
| K-419 | 466 | Doküman sitesinin ekran görüntüleri E2E koşumundan üretilir ve commit edilir; elle alınan görsel kabul edilmez |
| K-420 | 467 | Yayınlanan `description` metinleri iç doküman referansı taşımaz |
| K-421 | 468 | Public API takibi (`EnablePublicApiTracking`) Faz 60'ta, yayın kararından (Faz 7, K-068) bağımsız olarak açıldı 👤 |
| K-422 | 469 | RS0026'nın 10 kalemi üç ayrı stratejiyle çözüldü: birleştirme yalnız `ResolveAsync` içindir, geri kalanı ayrıştırma veya statik fabrikadır |
| K-423 | 470 | RS0041 (oblivious reference type) `Tracon.Abstractions.csproj`'da tek satırlık `NoWarn` ile bastırıldı; kök neden ölçüldü |
| K-424 | 471 | `Tracon.Generators` ve `Tracon.Templates` public API takibinden yeni bir `TraconPublicApiTrackingEnabled` MSBuild özelliğiyle hariç tutuldu |
| K-425 | 472 | Aday yeteneği üretimi `aday-kesfi` skill'iyle yazılı bir protokole bağlandı; oturum elemeli diyalog olarak koşar ve her bulgu üç kanala ayrışır 👤 |
| K-426 | 473 | Keşif turu kaydı `docs/kesif/<tarih>-<konu>.md` altında yaşar ve doküman dizin bütçesinden HARİÇ tutulur 👤 |
| K-427 | 474 | `docs/` kökü YALNIZ canlı dokümanı taşır; kapanmış her kayıt `docs/arsiv/`'e taşınır 👤 |
| K-428 | 475 | Aday listesi tur numarası taşımaz: `UCUNCU-FAZ-ADAYLARI.md` → `ADAYLAR.md` 👤 |
| K-429 | 476 | Manuel kabul testi PROTOKOLÜ `manuel-test-kosumu` skill'ine taşındı; `docs/manuel-test/` yalnız spec + koşum kaydı taşır 👤 |
| K-430 | 477 | `00-INDEKS.md` §7 `Koşum` sütunu koşum kaydından ÖLÇÜLÜR, elle işaretlenmez |
| K-431 | 478 | Örnek uygulama rol politikalarını `Tracon:Demo:Roles:Enabled` ile kaydeder ve o anda `RequireRolePolicies`'i AÇAR; şema gösterim amaçlı bir başlık okuyucusudur |
| K-432 | 479 | Workflow çalıştırması iptal istendiğinde `Completed` yerine `Canceled` yazılır; zorlama süper-adım sınırında VE pompa çıkışında yapılır |
| K-433 | 480 | Pompa, yanıtlanmış bir istekten sonra akışı yeniden açmadan ÖNCE MAF'ın kendi çalıştırma durumunu sorar |
| K-434 | 481 | Dosya belleği/metin araması alt ağacı kiracı VE agent adıyla öneklenir; oturumlar arası paylaşım BİLEREK korunur |
| K-435 | 482 | `IToolRegistry`/`TraconToolRegistration` `AITool`'a değil `AIFunctionDeclaration`'a genişledi |
| K-436 | 483 | İstemci tool'u bildirimi `AIFunctionFactory.Create(...).AsDeclarationOnly()` değil `AIFunctionFactory.CreateDeclaration(...)` ile kurulur |
| K-437 | 484 | İstemciden gelen tool sonucu modele `ChatRole.Tool` altında gönderilir, `ChatRole.User` değil |
| K-438 | 485 | CORS, `services.AddCors()` OLMADAN `CorsService`/`CorsMiddleware`'in elle kurulmasıyla uygulanır |
| K-439 | 486 | `toolResults` eşleştirmesi bilerek İKİ KEZ yapılır: akış başlamadan önce doğrulama, sonra gerçek mesaj kurulumu |
| K-440 | 487 | `ClientToolResult.Result`/`ErrorMessage` 65.536 karakterle sınırlanır |
| K-441 | 488 | Gömülebilir bileşen yalnız akışsız (`Idempotency-Key`) istek kullanır; SSE ayrıştırıcı taşımaz |
| K-442 | 489 | Gömülebilir bileşen `wwwroot/embed/` altına ayrı bir Vite girişiyle derlenir ve VAR OLAN genel varlık sunum mekanizmasıyla, sıfır yeni C# `endpoint` koduyla sunulur |
| K-443 | 490 | `FallbackChatClient` devre kesicinin DIŞINDA, `ContentFilterDetectingChatClient`'ın İÇİNDE durur (K-320'nin yerleştirme kuralının Faz 62'ye uygulanışı) |
| K-444 | 491 | `ModelFallback` yalnız `Provider`+`Model` taşır; birincil bağlamanın diğer alanları (sıcaklık, `ProviderSettings`, ...) yedeğe DEVRETMEZ |
| K-445 | 492 | Sağlayıcı başına giden eşzamanlılık sınırı DOLDUĞUNDA isteği REDDETMEZ, kendi `CancellationToken`'ıyla sınırlı olarak BEKLER |
| K-446 | 493 | Ön uçuş reddi `400 Bad Request` döner, `413` DEĞİL |
| K-447 | 494 | `ModelFallbackUsed` olayı HEM `run_events`'e HEM kök span'in `tracon.model.id` etiketine yazılır |
| K-448 | 495 | Ön uçuş token sayımı için `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.O200kBase` AÇIK `PackageReference` ile eklendi (Faz 62 Açık Soru 1, seçenek A); `Microsoft.Bcl.Memory` CVE zorlamasıyla sabitlendi |
| K-449 | 496 | Yedek zincirinde retryable OLMAYAN bir hata (401/403, iptal) HANGİ HALKADA olursa olsun ANINDA ve SARMALANMADAN fırlatılır; zincir yalnız TÜM halkalar retryable hatayla tükendiğinde `TraconProviderUnavailableException` ile "ilk hata" özetine sarılır |
| K-450 | 497 | `FallbackRetryClassifier.IsRetryable` istisnanın TAMAMINI (`InnerException` zinciri + her `AggregateException` kolu) gezer; yalnız en dıştaki istisnaya bakmaz |
| K-451 | 498 | Argüman-koşulu onay kuralı `POST /api/approvals/rules` YENİ bir uçtur; Faz 63 planı bunu yanlışlıkla "mevcut uç" sanıyordu (Faz 63, plandan sapma) |
| K-452 | 499 | `POST /api/approvals/rules` gövdesi `ArgumentsHash` alanı TAŞIMAZ; yalnız `toolName`/`agentName`/`argumentConditions` |
| K-453 | 500 | Plandaki `ToolApprovalDecision` adı `ToolApprovalPolicyDecision` olarak gerçekleşti (Faz 63, plandan sapma) |
| K-454 | 501 | `POST /api/approvals/rules`'ta "aynı kapsam + aynı koşul" çakışması, depoyu değiştirmeden ÜRETİLEN id ile DÖNEN id'yi karşılaştırarak tespit edilir |
| K-455 | 502 | `conditions_hash` kanonikleştirmesi sayısal normalizasyon YAPMAZ (`100` ile `100.0` farklı hash üretebilir); sıralama + `JsonElement.GetRawText()` yeterli sayıldı |
| K-456 | 503 | Denetim izi (`audit_log`) veri konusu silmesinin kapsamı dışındadır; silme yalnız İÇERİK verisinde (oturum, çalıştırma, konuşma, ek, puan, ses, çalıştırma girdisi) uygulanır (Faz 64, kullanıcı kararı) 👤 |
| K-457 | 504 | `DataSubjectScope`'a plandaki taslağın öngörmediği üçüncü bir alan (`ConversationIds`) eklendi (Faz 64, plandan sapma) |
| K-458 | 505 | `DocumentEmbeddings` (bilgi tabanı gömüleri) veri konusu silme/dışa aktarım kapsamının DIŞINDA bırakıldı (Faz 64, plandan sapma) |
| K-459 | 506 | Denetim zinciri "tenant'ın son yazılan satırı" sorgusu `ORDER BY created_at, id` YERİNE ayrı bir sıra sütunuyla (`chain_seq`: PostgreSQL `GENERATED ... AS IDENTITY`, SQL Server `SEQUENCE` + `DEFAULT NEXT VALUE FOR`, SQLite yerleşik `rowid`) bulunur |
| K-460 | 507 | `audit_log.before`/`after` PostgreSQL'de `jsonb`'den `json`'a değiştirildi (Faz 64, K-027'nin beşinci uygulaması) |
| K-461 | 508 | Denetim zinciri yazımında eşzamanlılık, oturum/advisory kilit YERİNE benzersiz dizin + yeniden deneme + rastgele gecikme (jitter) ile çözülür |
| K-462 | 509 | Veri konusu önizleme/silme AYNI SQL işlemi (transaction) üzerinden yürür: önizleme her zaman geri alınır (`ROLLBACK`), gerçek silme yalnız çağıranın denetim yazımı (`beforeCommitAsync`) başarılı olursa `COMMIT` edilir (Faz 64, K-370 emsali) |
| K-463 | 510 | `SessionConversationResolver` (Core) sıradan bir `AgentSession`'ın dahili sohbet geçmişini veri konusu kapsamına ekler; `DataSubjectScope.SessionIds` tek başına bunun için YETERSİZDİR (Faz 64, bağımsız denetim 🔴 #1) |
| K-464 | 511 | `DataSubjectTargetRegistry`'deki her `ArrayContains` çağrısı sütunu TAM NİTELİKLİ (`{table}.column`) verir, bare ad değil (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur) |
| K-465 | 512 | `SqliteDialect.AddUuidArray` Guid'leri BÜYÜK harf metin olarak yazar (`System.Text.Json`'ın varsayılan küçük harf biçimi DEĞİL) (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur) |
| K-466 | 513 | Kiracı kimlik bilgisi/egress çözümlemesi ASYNC bir ikinci yol olarak eklendi; mevcut SENKRON `AgentDefinitionCompiler.Compile`/`ModelProviderRegistry.CreateChatClient`/`CompiledAgentCache.GetOrAdd` üçlüsü DEĞİŞTİRİLMEDİ (Faz 65) |
| K-467 | 514 | Kiracı egress politikası kontrolü, kimlik bilgisi çözümlemesinden ÖNCE ve TEK bir noktada (`ModelProviderRegistry.CreateChatClientAsync`) çalışır — bu nokta hem gerçek `run` derlemesini hem `AgentDefinitionValidator`'ın ön-uçuş kontrolünü kapsar (Faz 65) |
| K-468 | 515 | Dört sağlayıcı paketinin (`OpenAI`/`Anthropic`/`Google`/`Azure`) kiracı-kimlik-bilgisi başına istemci önbelleği TEK bir paylaşılan `Tracon.Core.ProviderCredentialClientCache<TFactory>` sınıfıyla yapılır; dört ayrı kopya YAZILMADI (Faz 65) |
| K-469 | 516 | Kiracı sağlayıcı bağlama/egress uçları (`/api/tenants/{tenantId}/providers`, `/api/tenants/{tenantId}/egress`) kiracıyı AMBIYANS `ITenantContext`'ten değil, ROTA parametresinden alır — `ApiKeyEndpoints`'in aksine, `GovernanceEndpoints`'in `PUT /api/tenants/{slug}` deseniyle AYNI (Faz 65) |
| K-470 | 517 | `FallbackChatClient` bir fallback'i tetiklediğinde kiracı/egress'ten HABERSİZ senkron `CreateChatClient` metot grubunu değil, `ModelProviderRegistry.CreateChatClientAsync`'i çağırır (Faz 65, bağımsız denetim 🔴 #1) |
| K-471 | 518 | `CompiledAgentCache`, kiracı credential'ı gömülü bir `AIAgent`'ı credential'dan HABERSİZ bir anahtarla asla saklamaz; bağlama varken üç kaynak (`DefinitionStoreAgentSource`, `CodeAgentSource`) önbelleği TAMAMEN atlar (Faz 65, bağımsız denetim 🔴 #2) |
| K-472 | 519 | `InboundTriggerDispatcher`, "tetikleyici yok" ile "imza yanlış"ı TEK bir `InboundTriggerOutcome.Unauthorized`'a (HTTP `401`) birleştirir; ayrı bir `NotFound`/`404` yolu YOKTUR (Faz 66, bağımsız denetim 🔴 #1) |
| K-473 | 520 | `InboundTriggerDispatcher.ValidateAsync`, imza doğrulamasını hız sınırından ÖNCE çalıştırır (Faz 66, bağımsız denetim 🔴 #2) |
| K-474 | 521 | `InboundTriggerDispatcher.EnqueueAsync`, hedef agent/workflow'un GERÇEKTEN var olup olmadığını kabul anında DOĞRULAMAZ; kontrolü işleyiciye (`AgentRunJobHandler`/`WorkflowJobHandler`) bırakır (Faz 66, bağımsız denetim ile onaylandı) |
| K-475 | 522 | Migration ledger'ın `set_name`/`(set_name, id)` şema yükseltmesi numaralı bir migration DOSYASI değil, `SqlDialect.UpgradeMigrationsTableAsync` bootstrap adımıdır (Faz 67, plandan sapma) |
| K-476 | 523 | `IVectorSearchStore`, `EnableKnowledge = false` iken KAYITSIZ bırakılmaz; fabrikası `null` döner (Faz 67) |
| K-477 | 524 | `SqlPersistenceDiagnosticsSnapshot.PendingMigrations`'a yeni alan eklenmedi; çekirdek-dışı bir setin bekleyen adı `"{set}:{ad}"` önekiyle yazılır (Faz 67, açık soru 2'nin kapanışı) |
| K-478 | 525 | Çalıştırma kimliği `IRunAttributionContext`'ten gelir; istek GÖVDESİNDEN asla alınmaz (Faz 68) |
| K-479 | 526 | Etiketler ayrı tabloya değil `runs.labels` JSON sütununa yazılır (Faz 68, açık soru 1, kullanıcı kararı) 👤 |
| K-480 | 527 | Sınır aşımı KIRPILMAZ, REDDEDİLİR; gürültülü sınır HTTP'de (`400`), sessiz düşürme kayıt yolundadır (Faz 68) |
| K-481 | 528 | Kullanıcı kimliği ve etiketler METRİK etiketi OLMAZ; yalnız sorgu boyutudur (Faz 68) |
| K-482 | 529 | Token kırılımı toplamların İÇİNDE sayılır; bildirilmeyen sayaç `null` kalır, `0` OLMAZ (Faz 68) |
| K-483 | 530 | Cache fiyatı ÇIKARMALI hesaplanır ve HER maliyet toplamının ÜÇÜNCÜ terimidir; tanımsız oran `Unknown`'a DÜŞÜRMEZ (Faz 68, açık soru 2/3, kullanıcı kararı) 👤 |
| K-484 | 531 | `UsageDetails`'in ses sayaçları için `MEAI001` bastırması TEK dosyada (`UsageBreakdown`) toplandı (Faz 68) |
| K-485 | 532 | `/api/stats`'a `groupBy` EKLENMEDİ; `byUser`/`byLabel` her zaman döner (Faz 68, plandan sapma) |
| K-486 | 533 | Attribution UPSERT'te `COALESCE` ile KORUNUR, üzerine yazılmaz (Faz 68) |
| K-487 | 534 | Tool sarmalama sırası Authorizing (dış) → Timeout → ApprovalRequired (iç) → gerçek fonksiyon; MCP yolunda AYNI mantık TEKRARLANIR (Faz 69) |
| K-488 | 535 | Yetki reddi İSTİSNA fırlatmaz, normal sonuç döner; kanca hata fırlatırsa fail-closed (Faz 69) |
| K-489 | 536 | `RunErrorClass.ToolTimeout` yeni değer (13); `TraconToolTimeoutException` stabil kimliği `tool_timeout` (Faz 69) |
| K-490 | 537 | MAF `FunctionInvokingChatClient`'ın onay kısa devresi `AITool.GetService<T>()` pipeline'ı üzerinden çalışır; `ApprovalRequiredAIFunction.InvokeCoreAsync`'i DOĞRUDAN çağırmak defer ETMEZ, gerçek gövdeyi çalıştırır (ÖLÇÜLDÜ, Faz 69) |
| K-491 | 538 | Tool yürütme varsayılan timeout'u 30 saniye; ÖLÇÜLMEDİ, ilk gerçek koşumdan sonra gözden geçirilecek (Faz 69, F-114, açık soru 5) |
| K-492 | 539 | `RunEventType.ReasoningDelta` değeri 23, plandaki 22 DEĞİL (Faz 70, F-115) |
| K-493 | 540 | `IRunEventSink` fan-out'u depo başarısından TAM bağımsız; `RunEventWriter.CompleteAsync` artık `IsDisabled` iken erken dönmez (Faz 70, F-115, Açık Sorular 1/2/4) |
| K-494 | 541 | Fonksiyon düğümü yalnız `Sequential`'da desteklenir; `WorkflowDefinition.Nodes` `AgentNames` ile karşılıklı dışlanır (Faz 71, F-116) |
| K-495 | 542 | Karışık zincirde agent düğümü `AIAgentBinding` değil `WorkflowAgentStepExecutor` (bir `FunctionExecutor` alt sınıfı) olarak bağlanır (Faz 71, F-116) |
| K-496 | 543 | Fonksiyon adı kaydetme anında da doğrulanır (agent adının aksine, yalnız derleme anında); kayıt süreç ömrü boyunca sabittir (Faz 71, F-116) |
| K-497 | 544 | Kod düğümünün kendi zaman aşımı bu fazda ele alınmadı; Faz 69'un tool timeout sözleşmesi tek aday olarak bırakıldı (Faz 71, F-116, Açık Soru 2, ÇÖZÜLMEDİ) |
| K-498 | 545 | Kontrol noktasından devam sözleşmesi ÖLÇÜLDÜ: EN SON kontrol noktasından sürdürme kod düğümünü YENİDEN ÇAĞIRMAZ, DAHA ERKEN bir kontrol noktasından sürdürme ÇAĞIRIR — `AddWorkflowFunction` işleyicisi bu yüzden İDEMPOTENT olmak ZORUNDADIR (Faz 71, F-116, planın "en riskli hata modu") |
| K-499 | 546 | Talimat sözlüğü için migration YAZILMADI (Faz 72, F-117); plan yanlıştı |
| K-500 | 547 | Kültür sürümün İÇİNDEDİR; dil başına ayrı sürüm hattı açılmadı (Faz 72, F-117, plan Açık Soru 1, Seçenek A, kullanıcı kararı) 👤 |
| K-501 | 548 | `SpeechAlignment` KARAKTER bazlıdır, kelime bazlı DEĞİL (Faz 72, F-118) |
| K-502 | 549 | Akışlı sentez + `IncludeTimestamps` kombinasyonu istisna ile REDDEDİLİR, sessizce yok sayılmaz (Faz 72, F-118, K1) |
| K-503 | 550 | Alt agent çağrıları ebeveynin `culture`'ını MİRAS ALMAZ (Faz 72, F-117, denetimde bulundu) |
| K-504 | 551 | `speak` tool şeması `includeTimestamps` ALMAZ; yalnız `POST /api/voice/speak` (operatör HTTP yolu) destekler (Faz 72, F-118, plan sapması) |
| K-505 | 552 | Yetenek haritası `capabilities.md`'den ÜRETİLİR, commit edilir ve `dotnet pack` onu okur; Node zinciri `dotnet build`'e bağlanmaz (Faz 73, F-120, plan Açık Soru 4) |
| K-506 | 553 | `Tracon.Usage` tanıları `Warning`'dir; `Info` `dotnet build` çıktısına DÜŞMEZ (Faz 73, F-120, kullanıcı kararı, ölçümle) 👤 |
| K-507 | 554 | Harita sürüm işareti İÇERİK REVİZYONUDUR, paket sürümü değil (Faz 73, F-120, plan sapması) |
| K-508 | 555 | `TRC0302` sarmalayıcıyı değil, DEKORATÖRSÜZ VE FABRİKASIZ sarmalayıcıyı bildirir (Faz 73, F-120, plan Açık Soru 3) |
| K-509 | 556 | `CapabilityCoverageTests` kapsamı TÜM kayıt giriş noktalarıdır, yalnız `Use*`/`Map*` değil (Faz 73, F-120, kullanıcı kararı) 👤 |
| K-510 | 557 | Yerel referans dosyası PROJE başına yazılır, depo köküne değil (Faz 74, F-121, denetim bulgusu 3) |
| K-511 | 558 | `docs/openapi/tracon.json` `Tracon.AspNetCore` paketine KAYNAĞINDAN girer; K-039 yeniden açılmaz (Faz 74, F-121) |
| K-512 | 559 | `CapabilityExampleTests` DÖRT iddia taşır; yabancı üye listesi yalnız Microsoft üyelerini içerir (Faz 74, F-121) |
| K-513 | 560 | Bir `<example>`'ın doğruluğunu yalnız DERLEME kanıtlar; metin denetimi yapısal olarak yetersizdir (Faz 74, F-121, denetim bulgusu 4) |
| K-514 | 561 | Sevk edilen dokümantasyon KENDİ KENDİNE YETER: pakete giren bir metin yalnız tüketicinin elindeki şeylere gönderme yapar (Faz 75, F-126) 👤 |
| K-515 | 562 | Sevk edilen dokümanı denetleyen cırcır, satırı değil BLOĞU okur (Faz 75) |
| K-516 | 563 | Site üreteçleri artık ONARMAZ, HATA VERİR (Faz 75, F-126) |
| K-517 | 564 | `<see cref>` paketlenen OpenAPI belgesinde TAM İMZA olarak render edilir; sözleşme tiplerinde `<c>ÜyeAdı</c>` yazılır (Faz 75) |
| K-518 | 565 | Kök `README.md` İngilizce'dir (Faz 75) 👤 |
| K-519 | 566 | Doküman sitesinin rengi KAPALI bir token kümesidir ve kontrast derleme anında hesaplanır (Faz 76) |
| K-520 | 567 | Figürler (ekran görüntüsü ve diyagram) temadan BAĞIMSIZDIR; iki temada da açık plaka üzerinde durur (Faz 76) |
| K-521 | 568 | Kenar çubuğu `src/sidebar.mjs`'te tek kaynaktır; üç tüketici onu okur (Faz 76) |
| K-522 | 569 | Tüketici doküman standardı faz dokümanından ayrıştırılıp `tuketici-dokuman-senkronu` skill'ine taşındı (kullanıcı kararı) 👤 |
| K-523 | 570 | Kapanmış faz dokümanları `docs/arsiv/fazlar/` altında yaşar; `docs/**.md` bütçesi büyütülmez (kullanıcı kararı) 👤 |
| K-524 | 571 | `MIMARI.md` §7 "Güvenlik Modeli" kendi dosyasına ayrıldı |
| K-525 | 572 | Kiracı verisi tutan cache anahtarı TİPLİ olur; elle birleştirilen string anahtar yasaktır |
| K-526 | 573 | Denetim yükü `AuditPayload` ile kurulur; geri alınamaz eylem `AuditRecorder` kullanamaz |
| K-527 | 574 | Yürütülebilir yüzeyi genişleten ayar yapılandırmadan okunmaz |
| K-528 | 575 | `external:invoke` kapsamı istek anında zorlanır; anahtarın var olması yetmez |
| K-529 | 576 | Giden ağ hedefi TEK bir muhafızdan geçer; üç yüzey de kendi kopyasını taşımaz |
| K-530 | 577 | `Tracon:Egress:AllowPrivateNetworkTargets` varsayılanı KAPALI; üç yüzeyde de özel ağ reddedilir (kullanıcı kararı) 👤 |
| K-531 | 578 | Sağlayıcı istemcisine muhafız YALNIZ kiracı `Endpoint` override'ı varken takılır (kullanıcı kararı) 👤 |
| K-532 | 579 | Sağlayıcı SDK'larının muhafız kancası ÖLÇÜLDÜ; plan tahmini üçte ikisinde yanlıştı |
| K-533 | 580 | `secret` çözen HER yapılandırma anahtarı bir önek allow-list'ine bağlıdır |
| K-534 | 581 | MCP önek ayarı `Abstractions`'ta yaşar (`TraconMcpSecurityOptions`), `Tracon.Mcp`'de değil |
| K-535 | 582 | Webhook ek başlıkları Tracon'in kendi başlık adlarını taşıyamaz |
| K-536 | 583 | Giden ağ muhafızı ORTAM PROXY'sini kullanmaz (`UseProxy = false`) |
| K-537 | 584 | `llms.txt` yetenek haritasından AYRI bütçelenir: 10 240 B ve 20 480 B (Faz 78, F-135, ölçümle) |
| K-538 | 585 | `TRC0402` yalnız yerel referans dosyası GERÇEKTEN yazılırken öter (Faz 78, F-135, denetim 🔴 #1, kullanıcı kararı) 👤 |
| K-539 | 586 | Karar defterinin §2 tablosu YAPISAL olarak denetlenir: yinelenen numara · tabloyu kesen boş satır · sıra dışı numara |
| K-540 | 587 | Migration çakışmasının İKİ şekli vardır ve ikisi de geçicidir: unique ihlali VE deadlock |
| K-541 | 588 | Terminal `AwaitingApproval` durumu, ona bağlı kayıtlar GÖRÜNÜR olduktan sonra yayınlanır; sıra `TraconRunOptions.BeforePendingApprovalIsPublished` kancasıyla zorlanır |
| K-542 | 589 | Ürün dokümantasyonu `tracon.dev` alt alan adında kendi sunucumuzda yayınlanır; `base` KALICI olarak `/` oldu ve adres tek dosyada (`docs-site/site.config.mjs`) bildirilir 👤 |
| K-543 | 590 | Bağımlılık sürüklenmesini Dependabot fark eder; beş sabit gerekçeli `ignore` listesindedir 👤 |
| K-544 | 591 | MEAI 10.9.0, `Microsoft.Extensions.*` tabanını 10.0.11'e ZORLAR; "yalnız MEAI'yi yükselt" diye bir seçenek yoktur |
| K-545 | 592 | Geçici çakışma yeniden denemesi migration'ın BOOTSTRAP deyimlerini de kapsar; "bu yalnızca kurulum" muafiyeti yoktur |
| K-546 | 593 | `TraconToolAttribute.cs`'in `OrderTools` örneği artık `static` DEĞİL; tool metodu yine `static` |
| K-547 | 594 | `Azure.Identity` yalnızca test projesine (`Tracon.Generators.UnitTests`) `PackageReference` olarak eklendi; `Tracon.Azure`'un bağımlılık grafiği DEĞİŞMEDİ |
| K-548 | 595 | `SITE_KURALLARI` kural başına eşleşir; `--site-denetle` `git` hatasında artık çıkış kodu 1 verir (Faz 80, kullanıcı kararı) 👤 |
| K-549 | 596 | `kirik_baglantilar()` site-mutlak (`/...`) bağlantıları slug haritasıyla çözer; üretilen `api/`, `http-api/` ve `openapi/` hem kaynak hem hedef olarak hariç (Faz 80) |
| K-550 | 597 | Doküman bakım testleri `scripts/dokuman_bakim_test.py`dır (ALT ÇİZGİ); planın önerdiği `dokuman-bakim_test.py` (TİRE) DEĞİL (Faz 80, plandan sapma) |
| K-551 | 598 | Yanıt önbelleği anahtarı `record struct CacheKeyScope` (taban anahtar, kiracı, sağlayıcı, sıralanmış tool adları) üzerinden hesaplanır; MEAI'nin varsayılan `GetCacheKey`'ine güvenilmez (Faz 81, F-45, ölçümle) |
| K-552 | 599 | Yanıt önbelleği halkası tool çağrı döngüsünün İÇİNDE, telemetrinin VE içerik güvencesinin DIŞINDA durur (Faz 81, F-45, kullanıcı kararı) 👤 |
| K-553 | 600 | `AllowConcurrentToolCalls`, `ModelProviderRegistry`'nin kurduğu `FunctionInvokingChatClient` örneğine bağlanır; MEAI 1.18.0'ın getirdiği `ChatClientAgentOptions.AllowConcurrentInvocation`'a DEĞİL (Faz 81, F-134) |
| K-554 | 601 | `TraconResponseCachingChatClient` `internal sealed`dır; planın taslağı `public` diyordu (Faz 81, plandan sapma) |
| K-555 | 602 | Yanıt önbelleğinin okuma/yazma hatası `run`'ı DURDURMAZ; loglanır ve isabet/ıska sessizce devam eder (Faz 81, plan metninde açık değildi, repo-geneli ilkeden türetildi) |
| K-556 | 603 | `POST /api/agents/validate`'de eksik `IDistributedCache` `invalid_setting`/`model.providerSettings` olarak görünür; planın beklediği `compilation_error` DEĞİL (Faz 81, plandan sapma, ölçümle) |
| K-557 | 604 | 🚨 Önbellek isabeti hem `ChatResponse.Usage`'ı hem her mesajın `UsageContent`'ini SIYIRIR; aksi hâlde bir `run` kaydı hiç faturalanmamış token'ı ikinci kez sayar (Faz 81, gerçek `samples/Tracon.Api` koşumunda ölçülen kusur) |
| K-558 | 605 | `SqlConversationBranchStore` içerik korumasına dokunmaz: dallanma `conversation_items.item`'i BAYT BAYT kopyalar, zarf DOKUNULMADAN kalır (Faz 82, plandan sapma) |
| K-559 | 606 | `IContentProtector` varsayılanı `NullContentProtector`, `AddTracon()` içinde `TryAddSingleton` ile kaydedilir; `AddContentProtection(...)` `Replace` ile değiştirir (Faz 82, `IAuditLog`/`InMemoryAuditLog` deseninin aynısı) |
| K-560 | 607 | `SqlStoreContext.ContentProtector` `IContentProtector?` (nullable); `NullContentProtector.Instance`'a varsayılan DEĞERİ YOKTUR (Faz 82) |
| K-561 | 608 | `TraconContentProtectionOptions.Keys` bir kid'i anahtarın KENDİSİNE değil, yapılandırma anahtarının ADINA eşler (K-059 örüntüsünün tekrarı, Faz 82) |
| K-562 | 609 | Zarf biçimi metin sütunları için JSON (`$apEnc`/`kid`/`n`/`c`), ikili sütun için sabit başlıklı biçim (`APEB` + sürüm + kid uzunluğu + kid + nonce + şifreli+etiket) — tek yerde tanımlı (Faz 82) |
| K-563 | 610 | `TraconContentProtectionOptionsValidator` yalnız YAPISAL doğrulama yapar (`ActiveKeyId` set ve `Keys`'te var); anahtarın gerçek değerini `IConfiguration` üzerinden ÇÖZMEZ (Faz 82) |
| K-564 | 611 | `Tracon.Client` NSwag ile üretilir; üretilen kod repoya commit edilir ve davranışsal bir kapıyla (`ClientCoverageTests`) izlenir, byte-diff kapısı DEĞİL (Faz 83, kullanıcı kararı) 👤 |
| K-565 | 612 | `Tracon.Client` `Tracon.Abstractions`'ı REFERANSLAMAZ; DTO'lar OpenAPI belgesinden üretilir, sunucu tipleriyle aynı şekle sahip ama FARKLI CLR tipidir (Faz 83) |
| K-566 | 613 | `Tracon.Client` ve `Tracon.Cli` public API takibinin (`PublicApiAnalyzers`) DIŞINDADIR (Faz 83, Açık Soru 2 karar A) |
| K-567 | 614 | `Tracon.Client`'ın AOT sözünden VAZGEÇİLDİ: `TraconAotCompatible=false` (Faz 83, ölçülen risk gerçekleşti) |
| K-568 | 615 | `IMigrationApplier` arayüzü `Tracon.Abstractions`'a eklendi: linked-source `MigrationRunner` tipinin üç sağlayıcı derlemesinde belirsiz olması sorununu çözer (Faz 83) |
| K-569 | 616 | `AddTraconClient` `IServiceCollection` döner, `IHttpClientBuilder` DEĞİL; `IHttpClientFactory` kullanılmaz (Faz 83, plandan sapma) |
| K-570 | 617 | Üretim öncesi OpenAPI belgesinin bir kopyasında her şema `additionalProperties: false` ile kapatılır (Faz 83, ölçülen kusur) |
| K-571 | 618 | Enum'lar için `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` TİP DÜZEYİNDE her `enum` bildirimine eklenir; `JsonSourceGenerationOptions.Converters` listesi KULLANILMAZ (Faz 83, ölçülen STJ davranışı) |
| K-572 | 619 | `tracon health` `/api/models/health`'i okur; `MapTracon` içinde jenerik bir `/health` ucu YOKTUR (Faz 83) |
| K-573 | 620 | OpenAPI belgesi VARSAYILAN KAPALI uçları da tarif eder; `GET /api/diagnostics` belgeye girdi ama davranışı değişmedi (Faz 84, §84.3) |
| K-574 | 621 | `packages/` kök dizini dil sınırı kapısının (`SourceLanguageTests`) kapsamına girdi; `PackagedReadmePattern` regex'i `src/`'nin yanına `packages/`'ı da aldı (Faz 84, §84.7, denetim 🟡 bulgusu) |
| K-575 | 622 | `src/Tracon.UI/frontend/package.json`, `@tracon/client`'a `file:../../../packages/tracon-client` ile bağlanır; npm registry'den kurulmayı BEKLEMEZ (Faz 84, §84.8) |
| K-576 | 623 | npm yayın işi (`ci.yml`) NuGet'in `--skip-duplicate`'ine karşılık `npm view` ile elle idempotency kontrolü yapar; ikisi de AYNI `v*` git tag'inden türer, ayrı bir npm sürüm şeması YOKTUR (Faz 84, §84.11) |
| K-577 | 624 | Konsol göçü TAM göçtür; üretilen istemcinin üzerine adlandırılmış bir cephe (facade) katmanı EKLENMEDİ (Faz 84, planın kendi kaçış merdiveni kullanılmadı) |
| K-578 | 625 | OpenAPI üretecinin iki sistemik kusuru (eksik `required`, `number\|string` karışımı) VE paylaşılan-şema nullable sızıntısı frontend'de `Fix<T,K>` tek yardımcı tipiyle düzeltilir; sunucu şeması DEĞİŞTİRİLMEZ (Faz 84, §84.6, Açık Soru 4 karar A) |
| K-579 | 626 | `Microsoft.AspNetCore.Mvc.Testing` yalnız GERÇEK giriş noktalı örnek uygulamaları test eden projelerde kullanılır; kütüphane testleri `TestHost` kalır (Faz 85) |
| K-580 | 627 | SQL-tabanlı `jsonb`/`json` yükü taşıyan her ARA tip (`AgentDefinitionPayload` gibi), kaynak tipe (`AgentDefinition`) yeni alan eklendiğinde ELLE senkronize edilmelidir; derleyici bunu zorlamaz (Faz 86, ölçülen kusur) |
| K-581 | 628 | `CompiledAgentCache`'i atlayan bir çağıran, `IAgentCatalog.ResolveAsync`'in UYGULADIĞI `IAgentDecorator` zincirini de ELLE uygulamak zorundadır; yeni `AgentDecoratorPipeline.Apply` bunu tek bir yerde toplar (Faz 86, ölçülen kusur) |
| K-582 | 629 | `TraconOptions.MaxParameterValueLength` tek, üst-düzey bir sınırdır; parametre başına ayrı bir sınır YOKTUR (Faz 86, Açık Soru 2 kapatıldı, öneri A) |
| K-583 | 630 | Kesilen bir işi sürdürmek üçüncü, ayrı bir işlemdir (`JobKind.RunContinuation`, `runs.continued_from_run_id`); `Replay` (K-315) ve `ApprovalResume` (K-368) ile KARIŞTIRILMAZ (Faz 87, kullanıcı kararı) 👤 |
| K-584 | 631 | `Destructive`/`External` etkili bir tool taşıyan koşu varsayılan olarak devam ETMEZ; gevşetme bir AYARLA değil, tool'un kendi `SafeToRepeat` bildirimiyle yapılır (Faz 87, kullanıcı kararı) 👤 |
| K-585 | 632 | Devam koşusunun eşleşmeme politikası `RecordedToolPlayback`'in (internal) kurucusuna bir `ToolPlaybackMismatchPolicy` parametresi olarak eklendi; `ReplayToolMode`'a dördüncü bir üye veya ikinci bir sarmalayıcı tipi AÇILMADI (Faz 87, Açık Soru 1, öneri A) |
| K-586 | 633 | Skill veya çağrılabilir alt-agent kullanan bir agent DEVAM ETTİRİLEMEZ; bu Faz 87'nin planında YOKTU, uygulama sırasında ölçülüp keşfedilen bir sınırdır |
| K-587 | 634 | Workflow düğüm retry'ı yalnız `AddWorkflowFunction` ile kaydedilen fonksiyon düğümlerini kapsar; retry döngüsü düğümün KENDİ çağrısının içinde kalır ve `MaxSuperSteps` sayacını ETKİLEMEZ — ÖLÇÜLDÜ (Faz 87, Açık Soru 2 kapatıldı, öneri B) |
| K-588 | 635 | MEAI görsel üretim yüzeyi deneysel kaldığı sürece `MEAI001` bastırması yalnız görsel kayıt ve çağrı sınırlarında DAR tutulur (Faz 88, plan sapması) |
| K-589 | 636 | Birden çok image provider kaydı, `TraconImageOptions.Provider` adına göre KEYED çözülür; isimsiz kayıt yalnız özel consumer fallback'idir (Faz 88, denetim öncesi tasarım bulgusu) |
| K-590 | 637 | Görsel ek deposu yalnız doğrulanabilir baytı kalıcılaştırır; `HostedFileContent` fail-closed reddedilir (Faz 88, plan sapması) |
| K-591 | 638 | Google image adapter, `WIDTHxHEIGHT` isteklerini tahminden çevirmek yerine REDDEDER (Faz 88, plan sapması) |
| K-592 | 639 | Tool çıktısı boyut sınırı UTF-8 bayt biriminde ölçülür; token veya karakter DEĞİL (Faz 89, kullanıcı kararı) 👤 |
| K-593 | 640 | Kırpılan tool çıktısı `{"truncated","omittedBytes","content"}` alanlı bir JSON zarfına sarılır; zarf `Utf8JsonWriter` + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` ile üretilir (Faz 89, kullanıcı kararı + plan sapması) 👤 |
| K-594 | 641 | `TruncatingAIFunction` yalnız `string` ve `JsonElement` sonuçları ölçer/kırpar; başka bir ham CLR nesnesi HİÇ dokunulmadan geçer (Faz 89, bağımsız denetim bulgusu, plan Açık Soru 1 kapatıldı) 🔁 |
| K-595 | 642 | `TruncatingAIFunction`, zarfın asla sınırı AŞMAYACAĞINI kurucuda GARANTİ eder: `maxOutputBytes`, `MinimumEnvelopeBytes`'ın (bugün 57 B, `omittedBytes` için `int.MaxValue` worst-case ile hesaplanır) altındaysa `ArgumentOutOfRangeException` atar (Faz 89, bağımsız denetim bulgusu) |
| K-596 | 643 | `McpResourceTrimming` `Tracon.Mcp`'den `Tracon.Core`'a `TextTrimming` adıyla TAŞINDI (kopyalanmadı) ve `public` yapıldı (Faz 89, plan) |
| K-597 | 644 | Arşivlemek TAŞIMAK değil DAMITMAKTIR: kapanan fazın dokümanı tam metniyle değil, sabit boyutlu bir kayıtla arşivlenir (Faz 90) (kullanıcı kararı) 👤 |
| K-598 | 645 | Tam metin git geçmişinde yaşar ve çözülebilirliği HER denetimde kanıtlanır; `git log --follow` sözleşme DEĞİLDİR (Faz 90) |
| K-599 | 646 | `HARIC` muafiyeti KORUNUR; muaf tutulan her ağaç KENDİ bütçesini alır (Faz 90) (kullanıcı kararı) 👤 |
| K-600 | 647 | `KARARLAR.md` satırı 450 bayta indirilir; kesilen gerekçe `KARARLAR-GECMISI.md`'ye ÖNCE taşınır, satır SONRA kısaltılır (Faz 90) |
| K-601 | 648 | Public yüzey erişilebilirlik ölçütüyle daraltıldı: yaprak olup başka public imzada geçmeyen 96 tip `internal` yapıldı (Faz 96) |
| K-602 | 649 | Tek sürüm hattı: paketlenen projelerin hepsi `1.0.0-preview.N` olarak çıkar (Faz 97) (kullanıcı kararı) 👤 |
| K-603 | 650 | `PublicAPI.Shipped.txt` preview hattı boyunca boş kalır; 618 tipin `Unshipped` → `Shipped` dolumu `1.0.0` GA'ya ertelendi (Faz 97) (kullanıcı kararı) 👤 |
| K-604 | 651 | Yayın işleri (`publish`, `npm-publish`) yayın provası kapısına (`release-dryrun`) bağlandı; kapı her push'ta (PR dahil) koşar (Faz 97) |
| K-605 | 652 | Yeni paket `Tracon.Testing.Contracts.Xunit`: `IRunStore` ve 32 diğer store sözleşmesi xunit.v3 test taban sınıfı olarak sevk edilir; ad alanı `Tracon.Testing.Contracts.Storage` (Faz 98) (kullanıcı kararı — paket adı ve ad alanı) 👤 |
| K-606 | 653 | `ApiKeyGenerator`/`GeneratedApiKey` `Tracon.Core`'dan `Tracon.Abstractions`'a taşındı (Faz 98) |
| K-607 | 654 | `IRunStore.AppendEventAsync` yinelenen `Sequence`'i REDDEDER (dört implementasyonda: bellek içi + üç SQL sağlayıcı) (Faz 98) (kullanıcı kararı) 👤 |
| K-608 | 655 | `StartRunAsync`'in dönüş değeri dört sağlayıcıda da COALESCE uygulanmış kaydı taşır (Faz 98) |
| K-609 | 656 | Tracon, `IModelProvider.CreateChatClient`'ın döndürdüğü `IChatClient`'ı HİÇBİR ZAMAN dispose etmez; ömür sağlayıcınındır (Faz 99) (kullanıcı kararı) 👤 |
| K-610 | 657 | `ContractCoverage`'ın her çağrısı bir sözleşme AİLESİ adı alır; aile adı almayan aşırı yükleme YOKTUR (Faz 99) |
| K-611 | 658 | Sözleşme suite'inde isteğe bağlı davranış ATLANAN senaryo değil, AYRI bir opt-in sınıftır (Faz 99) |
| K-612 | 659 | Uygulanmış migration baytları değiştirilebilir checksum manifestine değil Git kaynak commit'lerine sabitlenir (Faz 99, F-151) |
| K-613 | 660 | Yargıç model istemcisi ayrı `CreateSetupChatClientAsync` üyesiyle kurulur (Faz 100) |
| K-614 | 661 | `IToolRegistry` public kalır; tanınmayan implementation `IVerifiedToolRegistry` marker'ı taşımadığı için startup'ta reddedilir; `AllowUnverifiedToolRegistry` bilerek yapan için opt-out'tur (Faz 102) |
| K-615 | 662 | Generator kendi ürettiği `JsonSerializerContext`'i kullanmaz; complex tool sonucu için tool sahibi kendi `[JsonSerializable]` context'ini `TraconToolAttribute.JsonSerializerContext`'e verir, aksi hâlde derleme `TRC0008` ile durur (Faz 102) |
| K-616 | 663 | `CustomToolContract` yalnız implementer'ın TEK BAŞINA sağlayabileceği isim/metadata/eşzamanlı çağrı sözleşmesini taşır; tenant/timeout/envelope/guard davranışı registry-DI sınırını geçtiği için pakete Core bağımlılığı EKLENMEDEN Core fonksiyonel testlerinde ve sample run'ında kanıtlanır (Faz 102) |
| K-617 | 664 | Content guard'ın normalize edemediği tool sonucu için fail-closed yolu, guard PATTERN eşleşmesinden TAMAMEN bağımsız, koşulsuz bir değiştirme olarak yeniden yazıldı (Faz 102, bağımsız denetim bulgusu — kapanmadan kapatılan 🔴) |
| K-618 | 665 | BYOK iki ayrı public interface'e bölündü: `IModelProvider.CreateChatClient(binding)` yalnız setup credential, yeni `ITenantCredentialModelProvider.CreateChatClient(binding, credential)` yalnız tenant credential alır; capability yok ise registry provider'ı hiç çağırmadan `provider_credential_unsupported` ile fail-closed olur (Faz 103) (kullanıcı kararı) 👤 |
| K-619 | 666 | Provider construction hatası normalizasyonunda Tracon'in KENDİ validation hatası, internal ve tek başına inşa edilemez (`InternalsVisibleTo` ile korunan) bir işaretçi tipiyle "foreign" sayılmaktan çıkarıldı (Faz 103) |
| K-620 | 667 | `TraconJudgeException` kaldırıldı; stable judge error code'ları (`judge_failed`/`judge_timeout`/`judge_contract`) internal sabitlere taşındı; `ITraconBuilder.AddRunJudge` `AddAgentSource` deseniyle üç overload (generic/instance/factory) olarak eklendi (Faz 103) (kullanıcı kararı) 👤 |
| K-621 | 668 | `JudgeTimeout` cooperative cancellation değil GERÇEK wait cutoff'tur; token'ı yok sayan judge gövdesi timeout'ta öldürülmez, arkada tamamlanır ve geç sonuç sessizce atılır (skor/summary/metric yazmaz, unobserved exception üretmez) (Faz 103) (kullanıcı kararı) 👤 |
| K-622 | 669 | `kapi.py yayin` extension sample'larını (+ Native AOT smoke) izole `NUGET_PACKAGES` cache'i ve tek exact packed version ile doğrulayan `release_extension_samples.py`'a bağlandı; AOT publish restore+publish TEK komutta birleştirildi (Faz 103) |
| K-623 | 670 | Kiracı yalıtımı UYGULAMA KATMANINDA tek hat kalır; veritabanı RLS'i (Row Level Security) eklenmez (Faz 104) 👤 |
| K-624 | 671 | `Production` ortamında kalıcı olmayan store BAŞLANGIÇTA UYARIR; hata fırlatılmaz ve susturma seçeneği eklenmez (Faz 104) 👤 |
| K-625 | 672 | Üç SQL sağlayıcısına `Options.DataSource` alanı eklendi; Tracon kendi kurduğu `NpgsqlDataSource`/`SqlServerDataSource`/`SqliteDataSource`'u artık PUBLIC bir DI servisi olarak kaydetmez; `DataSource` ve `ConnectionString` birlikte verilirse başlangıç hatası (sessiz öncelik yok) (Faz 110) |
| K-626 | 673 | `runs_v1` okuma sözleşmesi görünümü üç sağlayıcıda `EnableReadViews` ile isteğe bağlı yayımlandı; yayımlanan sürüm sütun kaybetmez/yeniden adlandırmaz/daraltmaz, kırıcı değişiklik `runs_v2` olarak açılır; görünüm bir kiracı sınırı DEĞİLDİR (Faz 111) |
| K-627 | 674 | `RunErrorClass.BudgetExceeded` kaldırıldı; sayısal değer `9` KALICI OLARAK EMEKLİ, asla yeniden kullanılmaz 👤 |
| K-628 | 675 | İstemci taraflı tool (`AddClientTool`) taşıyan bir agent, `POST /replay` ile HİÇBİR `toolMode`'da (`ReplayTools`/`LiveTools`/`NoTools` üçü de) yeniden oynatılamaz; ret `RunReplayService.PrepareAsync` içinde, run hiç başlamadan, agent TANIMININ tool listesi taranarak verilir (Faz 112) 👤 |
| K-629 | 676 | Sağlayıcı retry kararı için `IProviderRetryClassifier` (üç durumlu: `Unknown`/`Retry`/`DoNotRetry`) yeni public seam olarak `Tracon.Abstractions/Providers/` altında açıldı; `DefaultRunErrorClassifier` `internal sealed partial` → `public sealed partial`; `ErrorFingerprint`'in iç hesabı internal kalıp yeni public `RunErrorFingerprint.Compute(string?)` facade'ı eklendi (Faz 113, F-149) |
| K-630 | 677 | Çalıştırma-içi bütçe kesmesi (`TraconRunBudgetExceededException`, `run_budget_exceeded`) yeni bir `RunErrorClass` üyesi AÇMADAN mevcut `QuotaExceeded` (`4`)'e eşlenir; `9` K-627'nin kararıyla kalıcı emekli kalır (Faz 114, F-166) 👤 |
| K-631 | 678 | `ModelProviderRegistry`'nin kurucusu `IRunPricingResolver?` yerine `IServiceProvider?` alır; `RunBudgetChatClient` fiyat çözümleyiciyi boru hattı KURULUM anında (`BuildPipeline` içinde) GEÇ (lazy) çözer (Faz 114, F-166) |
| K-632 | 679 | `AgentRunBudget`'ın maliyet sayacı `decimal` değil, `long` sabit noktalı nano-birim (`1/1_000_000_000`) + `Interlocked.Add`'tir; `RunRecordingAgent.CompleteAsync`'in run sonu `scope.Budget?.RecordUsage(...)` satırı KALDIRILDI — bütçe muhasebesinin %100'ünü artık `RunBudgetChatClient` taşır (Faz 114, F-166) |
| K-633 | 680 | `Tracon.Client`'ın üretilmiş `System.Text.Json.JsonElement`/`ChatRole`-tipli alanları geriye dönük UYUMSUZ biçimde düzeltildi (`scripts/nswag-postprocess-client.py`); `TraconPublicApiTrackingEnabled=false` olduğu için derleyici bunu işaretlemedi (Faz 115, eval CLI komutu sırasında bulunan önceden var olan kusur) |
| K-634 | 681 | Performans tahsis kapısı yalnız TAHSİS EDİLEN BAYTI karşılaştırır (sıfır tolerans), süreyi bilgi olarak kaydeder ama hiçbir şeyi kırmaz; beşinci bağımsız bir kapı DEĞİL, `kapi.py kapanis`'in üç sıcak yol dosyasından biri değiştiğinde koşturduğu koşullu bir adımdır (Faz 116, F-67) 👤 |
| K-635 | 682 | BenchmarkDotNet 0.15.8 yalnız `bench/Tracon.Benchmarks` (`IsPackable=false`) için eklendi; K-007'nin "yeni paket gerekçe ister" barı gerçek restore ile ölçülerek karşılandı (Faz 116, F-67) |
| K-636 | 683 | `ModelContextProtocol.Extensions.Tasks` 2.2.0 yalnız `Tracon.AspNetCore`'a eklendi; net yeni geçişli paket sıfırdır, `Tracon.Mcp` (istemci) bu paketi görmez (Faz 117, F-167) |
| K-637 | 684 | MCP task kimliği Tracon'in run kimliğidir; SDK'nın `IMcpTaskStore.CreateTaskAsync()`'i çağrının hangi agent/kiracı olduğunu bilmediği için bu eşleşme kayıt-öncesi bir `AsyncLocal` sağlayıcı filtresiyle (`McpTaskRunProvisioningFilter`) kurulur; arka plan yürütmesi kendi `AmbientTenantScope` sarmalını taşır; kiracı-oblivious SDK-içi `tasks/cancel` müdahalesi kabul edilen, kapatılamayan bir sınır olarak belgelenir (Faz 117, F-167) (kullanıcı kararı) 👤 |
| K-638 | 685 | Yargıç başına retry checkpoint'i yeni bir tablo/migration AÇMADAN mevcut `run_scores` satırlarından okur; bu, K-621'in "sonraki adım" sütununun sorduğu soruya (F-152 timeout modeline yeni bir katman ekler mi) HAYIR cevabıdır (Faz 118, F-152) (kullanıcı kararı) 👤 |
| K-639 | 686 | Kiracı sağlayıcı bağlantısında (`BYOK`) `provider` adı, karşılaştırıcı değiştirilerek değil DEĞER NORMALLEŞTİRİLEREK case duyarsız yapılır; kural `TenantProviderBinding.NormalizeProviderName` olarak public'tir ve store hem yazarken hem sorgularken uygular (Yayın denetimi, BL-006) |
| K-640 | 687 | Yabancı (Tracon dışı) bir exception'ın mesajı hiçbir zaman kalıcı alana veya dışa açık yanıta yazılmaz; kural `Tracon.SafeErrorText` olarak `Tracon.Abstractions`'ta public'tir (Faz 119, BL-027/BL-037) |
| K-641 | 688 | `IJobHandler`'ın at-least-once yürütme sözleşmesi (aynı job'ın süzülmemiş item listesiyle yeniden çağrılabileceği) `Tracon.Abstractions`'ın XML dokümanına yazılır ve bir contract testiyle (`JobHandlerContract`, `Tracon.Testing.Contracts.Xunit`) kilitlenir; `IIdempotencyStore`'u job dispatch loop'una bağlamak gereksiz ikinci bir mekanizma olacağı için REDDEDİLİR (Faz 120, BL-041) |
| K-642 | 689 | `IAgentDecorator.Order`'ın sayısal yönü DEĞİŞMEZ (düşük değer dışta sarar); yanlış olan XML dokümanıydı ve düzeltildi. Sıralama sözleşmesi taşıyan her public üyenin yönünü SÖZCÜKLE belirtmesi `OrderingContractDocumentationTests` ile kalıcı kapıya bağlandı (BL-034) |
| K-643 | 690 | Seam sözleşme standardının dört boyutu (DI lifetime, tenant mode, delivery guarantee, guarantee limit) iki ayrı mekanizmayla kilitlenir: ilk üçü `SeamContractDocumentationTests`'in küçülen taban çizgisiyle, dördüncüsü `OrderingContractDocumentationTests` deseniyle ayrı TheoryData satırlarıyla (Faz 121, BL-003 kulvar 3) |
| K-644 | 691 | `IAuditLog`'un null-tenant sözleşmesi (boşsa çağıranın AMBIENT tenant'ına düşer, "her kiracı" değil) `ITenantContext` enjeksiyonuyla GERÇEK davranışa dönüştürüldü — `InMemoryAuditLog` öncesinde tüm kiracıları tarıyordu, `SqlAuditLog` sessizce boş dönüyordu; ikisi de `SqlRunStore`'un deseniyle hizalandı (Faz 121, BL-046) |
| K-645 | 692 | 60 tekil-registrasyon seam'ine dedicated `Add*()`/`Use*()` metodu EKLENMEZ; `ITraconBuilder.Services`'in XML dokümanına bağlı bir metin kapısı sözleşmesi yeterli sayıldı (Faz 122, BL-008/BL-019 kulvar 2) |
| K-646 | 693 | Tenant credential'la üretilen `IChatClient`, dört sevk edilen adaptörün (Anthropic, Azure, Google, OpenAI) hepsinde (credential, model, `ProviderSettings`) başına önbelleğe alınır; paylaşılan anahtar `Tracon.Core.TenantChatClientCacheKey` olarak eklendi 👤 |
| K-647 | 694 | `RunEventType` üyesinin payload hakkındaki her iddiası, o payload'ı OKUYAN bir testle eşleşmek zorundadır (`RunEventPayloadContractTests`, yalnız küçülen `uncovered` taban çizgisi); `ModelFallbackUsed` payload'ına eklenen `reason` KAPALI bir küme taşır ve sağlayıcının hata metnini asla taşımaz (tüketici raporu doğrulaması) |
| K-648 | 695 | Oturumun İLK yazımı gibi SONRAKİ her yazımı da eşzamanlılık denetiminden geçer: `ISessionStore.TryUpdateAsync` + `sessions.version` (üç SQL sağlayıcıda migration); çakışma `TraconSessionConflictException` fırlatır ve retry ÇAĞIRANA düşer, Tracon içeride sessizce denemez 👤 |
| K-649 | 696 | Kalıcı payload sürüm sözleşmesi: `sessions.schema_version` yeniden adlandırılarak `state_schema_version` oldu (veri korunarak), yeni `state_maf_version` sütunu `sessions` ve `workflow_checkpoints`'e eklendi; damgalama/doğrulama sorumluluğu SQL store'dan `AgentSessionManager`'a taşındı 👤 |
| K-650 | 697 | `POST /api/stats/recalculate-costs` DARALDI: artık yalnız `PricingSource.Unknown` (veya hiç fiyatlanmamış) satırları fiyatlar; bilinen fiyatlı bir satırı bir daha asla yeniden yazmaz (Faz 132, F-175) |
| K-651 | 698 | Manuel test bütçesi ölçüme yeniden bağlanabilir; formül `ölçülen / (1 − BOSLUK_ORANI)`, karar kullanıcınındır 👤 |
| K-652 | 699 | Paylaşılan bir SQL sorgusunda toplama fonksiyonunun dönüş tipi `CAST` ile sabitlenir |
| K-653 | 700 | Kuyruk derinliği kiracıdan bağımsızdır: `IJobStore.GetQueueDepthAsync` kiracı parametresi almaz ve `tracon.job.queue.depth` kiracı etiketi taşımaz |
| K-654 | 701 | Sınırlı yapısal yanıt onarımı (bounded repair) aynı `run` içinde kalır; yeni bir `RunKind`/child run açılmaz 👤 |
| K-655 | 702 | K-615 aynen genişler: bir tool'un OBJECT parametre grafındaki her tip de `JsonSerializerContext`'e `[JsonSerializable]` ile eklenmek zorundadır, yalnız complex SONUÇ tipi değil 👤 |
| K-656 | 703 | Bir testin `MeterListener`'ı `Meter` INSTANCE'ına bağlanır, meter ADINA değil |
| K-657 | 704 | Sevk edilen bir contract'a (`Tracon.Testing.Contracts.Xunit`) dokunan faz, kapanışta `kapi.py yayin --kuru`'yu da koşar 👤 |
| K-658 | 705 | Kalıcı (Tracon damgalı) oturum taşıyan bir `run`'da bounded repair AÇILMAZ; reddetme, onarım kapalıymış gibi `run`'ı düşürür 👤 |
| K-659 | 706 | Repo private kaldığı sürece pakete giren tüketiciye dönük URL'ler doküman sitesine bakar; `RepositoryUrl` gerçek repo'da kalır 👤 |
| K-660 | 707 | Duplex bir protokolde istemciyi bekleme durumuna sokan her istek bir çıkış çerçevesi hak eder; boş commit `idle` ile kapanır 👤 |
| K-661 | 708 | Bir NuGet `<id, version>` çifti tekil bir artifact'i adlandırır: `dotnet pack` kirli (commit'siz veya untracked) bir çalışma ağacında REDDEDER, ve `kapi.py yayin` aynı kimlikte farklı SHA-256'lı bir artifact'i asla sessizce ezmez 👤 |
| K-662 | 709 | `JobKind` KALDIRILDI; işin kimliği tek bir dizge alandır (`JobRecord.HandlerKey` / `JobSchedule.HandlerKey`), ikinci bir alan tutulmaz 👤 |
| K-663 | 710 | Handler anahtarı KAYITTA verilir (`AddJobHandler<T>(key)`), handler SCOPED kaydedilir ve execution başına yeni bir DI scope'undan çözülür; dispatch tam anahtar eşleşmesidir, kayıt sırası sonucu DEĞİŞTİRMEZ 👤 |
| K-664 | 711 | Kayıtsız handler anahtarı FAIL-CLOSED'dır: iş `Failed` kapanır, `ErrorMessage` kararlı `JobErrorCodes.UnknownHandlerKey` kodunu taşır ve HAM ANAHTARI TAŞIMAZ |
| K-665 | 712 | `PUT /api/schedules/{name}` yalnız `TraconSchedulingOptions.HttpSchedulableHandlerKeys` içindeki anahtarı kabul eder (boş liste = yalnız yerleşik dokuz anahtar); izinli anahtar listesi `GET /api/schedules/handler-keys` ile ADMIN ardında yayınlanır, kimlik doğrulamasız `/api/meta` ile DEĞİL 👤 |
| K-666 | 713 | SQLite'ta `kind` sütunu YERİNDE düşürülür (`ALTER TABLE ... DROP COLUMN`), repo'nun tablo-yeniden-kurma emsali (`0006_sessions_tenant_key.sql`) İZLENMEZ |
| K-667 | 714 | Aynı tipin aynı handler anahtarıyla ikinci kaydı NO-OP'tur; çakışma yalnız İKİ FARKLI tip aynı anahtarı paylaştığında vardır |
| K-668 | 715 | Handler'ın kurucusu çözülemezse iş `JobErrorCodes.HandlerActivationFailed` ile `Failed` kapanır; YENİDEN DENENMEZ |
| K-669 | 716 | `VoiceDescriptor.Attributes` sağlayıcı üstverisini typed alanlar değil, sınırlı bir `Dictionary<string,string>` olarak taşır |
| K-670 | 717 | `IRunAuthorizationHandler` dört run başlatan yüzeyin (agent run, workflow run, inbound trigger, OpenAI uyumlu `/v1/responses`) DÖRDÜNÜ de kapsar; kapı `QuotaGate` ile AYNI çağrı şeklini taşır (elle çağrı, ortak `IEndpointFilter` DEĞİL) 👤 |
| K-671 | 718 | Reddedilen session `List` erişimi `403` döner (asla filtrelenmez); reddedilen `Read`/`Delete`/`Branch` `404` döner ve gövdesi gerçekten var olmayan bir session'la BİREBİR AYNIDIR 👤 |
| K-672 | 719 | `ContentGuardContext.Source` içerik TİPİNE göre sınıflanır (`FunctionResultContent` → `ToolResult`, rolden BAĞIMSIZ), sonra mesajın ROLÜNE göre; `Direction`'a hiç bakılmaz — geçmiş bir turun yeniden gönderilen model metni `Input` yönünde de `ModelOutput` kalır. `PatternContentGuard` `Source`'u KASITLI okumaz |
| K-673 | 720 | `run_events.custom_type` AYRI bir nullable sütundur (üç dialect'e birer migration), `payload` metni içine gömülmez 👤 |
| K-674 | 721 | Geçersiz `CustomType` (`Custom` iken boş/yanlış biçim, ya da `Custom` DIŞINDA doluyken) `RunEventWriter.AppendAsync`'i `ArgumentException` ile REDDEDER, sessizce atlamaz veya loglamaz 👤 |
| K-675 | 722 | `IToolApprovalPresenter`'ın çözdüğü sunum `pending_approvals`'a KALICI bir sütun olarak yazılır, karar anında yeniden çözülmez 👤 |
| K-676 | 723 | `IToolApprovalPresenter` fail-OPEN'dır: kayıtlı değil, `null` döner, `throw` eder veya zaman aşımına uğrar — dördü de onay isteğinin yayımını ENGELLEMEZ |
| K-677 | 724 | Alt-agent çağrısının iki katmanlı bekleme sınırında katman 2'yi (sert kesme) `ChildAgentInvoker`'ın KENDİSİ uygular; MAF'ın `BackgroundAgentsProviderOptions.WaitTimeout`'una GÜVENİLMEZ |
| K-678 | 725 | Kayıtlı olay akışının (`GET /api/runs/{id}/events`) SSE çerçeve adları AÇIK bir eşleme tablosuyla verilir; `RunEventType` üyesinin adından MEKANİK türetilmez, ve tamlık bir kapıya (`RunEventFrameNameContractTests`) bağlanır 👤 |
| K-679 | 726 | `RunEventType.Custom`'ın kayıtlı olay akışındaki SSE çerçeve adı HER ZAMAN sabit `"custom"`dır; tüketicinin kendi `CustomType` dizgesi asla çerçeve adı OLMAZ 👤 |
| K-680 | 727 | Kota muhasebesi (`RecordQuotaAsync`) artık `run`'ın terminal olay yazımından ÖNCE çalışır; kota bir hata sonrası GERİ ALINMAZ 👤 |
| K-681 | 728 | `QuotaEnforcer.RecordAsync` geçilen eşikleri döner (`ValueTask` → `ValueTask<IReadOnlyList<QuotaThresholdCrossing>>`); `IQuotaStore`'a `TryClaimThresholdNotificationAsync` eklenir — ikisi de kırıcı |
| K-682 | 729 | Kota eşiği tekilliği `quota_usage.notified_thresholds` (`text`, `,metrik:yüzde,` sınırlayıcılı CSV) sütununda kalıcı hâle gelir; atomiklik koşullu `UPDATE`'in etkilenen satır sayısıyla sağlanır |
| K-683 | 730 | Kaynak yetkilendirmesi AYRI bir sözleşme açmaz: var olan `RunAccess`/`SessionAccess` enum'ları büyür, `RunAuthorizationRequest` `RunId` kazanır ve `AgentName` `required` olmaktan çıkar; `IRunAuthorizationHandler`'ın metot sayısı DEĞİŞMEZ. Enum'ların sayısal değeri bir persistence sözleşmesi DEĞİLDİR ve bu XML'e açıkça yazılır 👤 |
| K-684 | 731 | Reddedilen TEKİL kaynak `404` döner ve gövdesi gerçekten var olmayan kaynakla BİREBİR aynıdır; reddedilen LİSTE `403` döner. Kapı, kaynak bulunduktan ve kiracısı doğrulandıktan SONRA ve durum okumasından ÖNCE sorulur; ret yanıtını çağıran verir, kapı üretmez |
| K-685 | 732 | `GET /api/runs/{id}/tools` var olmayan bir `run` için artık `200 []` değil `404` döner; bu, handler kayıtlı olmasa bile geçerli bilinçli bir davranış değişikliğidir 👤 |
| K-686 | 733 | `POST /api/attachments` reddi `403` döner, `404` değil 👤 |
| K-687 | 734 | Ses WebSocket'inin yetkilendirme reddi `404` döner ve gövdesi erişilemeyen oturumunkiyle BİREBİR aynıdır (`401` veya `403` DEĞİL); var olmayan oturum reddedilmez, handler sorulur ve varsayılan cevap soketi açar (K-283 korunur) 👤 |
| K-688 | 735 | Oturum sahipliği KALICI bir sütundur (`sessions.owner_id`, üç migration), ayrı bir `session_owners` tablosu değil; `SessionQuery.OwnerId` süzgeci SQL `WHERE` yan tümcesinde, `Skip`/`Take`'ten ÖNCE yaşar 👤 |
| K-689 | 736 | Sahiplik BİR KEZ atanır: ilk yazımda çözülür, sonraki her yazımda KAYNAKTAN taşınır ve üç SQL `store` ile bellek içi `store` sütunu `COALESCE` eder — "set → unset" meşru bir geçiş DEĞİLDİR |
| K-690 | 737 | Mod açıkken `IRunAttributionContext` bir MUHASEBE değil bir YETKİLENDİRME girdisidir: çözülemeyen kimlik oturumu açtırmaz (`403`, `errorType` `session_owner_required`), `NULL` sütun bırakmaz 👤 |
| K-691 | 738 | Sahiplik sınırı `run` BAŞLATAN yüzeylerde de zorlanır (`403`), yalnız oturum uçlarında değil; ama sahipli LİSTE muafiyeti (`ManagementPolicy`) yalnız listeye uygulanır — tekil oturuk okumasında yönetim muafiyeti YOKTUR |
| K-692 | 739 | Sahip çözümünde AÇIK bir `AmbientRunAttributionScope` kayıtlı `IRunAttributionContext`'i EZER; bu öncelik yalnız SAHİPLİK içindir, attribution'ın kendi okuyucusu değişmez |
| K-693 | 740 | Sahipsiz eski satır tekil erişimde REDDEDİLMEZ (sahipli listede ise HİÇ görünmez); sahiplik geriye dönük DEĞİLDİR |
| K-694 | 741 | Katı modun yönetim muafiyeti yalnız OKUMA kapısındadır (`SessionOwnershipGate.DeniesAsync`), `run` BAŞLATMADA yoktur 👤 |
| K-695 | 742 | Sahipsiz satır reddi ile BAŞKASININ oturumu reddi aynı metni taşır; ayrı bir `errorType` icat edilmez 👤 |
| K-696 | 743 | `/v1/conversations`'ın üç OKUMA/SİLME ucu `IRunAuthorizationHandler`'a bağlandı; `POST` bağlanmadı |
| K-697 | 744 | `TraconEndpointOptions.MapOpenAIConversations` yalnız conversations ailesini yönetir; varsayılan `true` 👤 |
| K-698 | 745 | `RequireCustomBinding<T>()` serbest generic'tir; yedi sözleşmenin kapalı kümesi ÇALIŞMA ANINDA zorlanır, derlemede değil 👤 |
| K-699 | 746 | Zorunlu binding ihlali `InvalidOperationException` atar; `TraconException` ailesine yeni tip eklenmez 👤 |
| K-700 | 747 | Zorunluluk `/api/diagnostics`'te GÖRÜNMEZ; `ExtensionPointDiagnostic` bir `IsRequired` alanı almaz 👤 |
| K-701 | 748 | Kapı yalnız "yerleşik varsayılan mı" sorusunu yanıtlar; lifetime iddiası kapsam dışıdır 👤 |
| K-702 | 749 | Sözleşmenin non-nullable ilan ettiği bir koleksiyona AÇIK `null` `400`'dür, `500` değil; üretilen istemcinin non-nullable koleksiyonları da boş başlar 👤 |
| K-703 | 750 | `RunEventType.LoopIterationCompleted` 31'dir, planın yazdığı 30 DEĞİL; 30 zaten `ChildRunTimedOut`'tur |
| K-704 | 751 | Tracon MAF'a TEK bir composite `LoopEvaluator` verir, tanımın ölçüt listesini değil |
| K-705 | 752 | `LoopSettings.MaxIterations` `null` iken Tracon'in kendi `DefaultMaxIterations = 10` sabiti uygulanır |
| K-706 | 753 | `aiJudge` ölçütü `AddModelRunJudge` binding'ini kullanır; yoksa DERLEME HATASI verir, agent'ın modeline düşmez |
| K-707 | 754 | Değerlendirilemeyen bir döngü ölçütü döngüyü DURDURUR; `run`'ı düşürmez |
| K-708 | 755 | `AddLoopEvaluator` yerleşik bir `kind`'i gölgeleyemez; kayıt anında reddedilir |
| K-709 | 756 | Döngü ölçütü kaydı `TraconExtensionPoints` tablosuna GİRMEZ; sekizinci bir genişleme noktası değildir |
| K-710 | 757 | `RunScore.Name` tekillik anahtarına girer, `author` `COALESCE` EDİLMEZ 👤 |
| K-711 | 758 | `RunScore.Value` `required int` → `double?` (`null` = ölçüm yok); `TextValue` ve `Categorical` eklendi 👤 |
| K-712 | 759 | `RunScoreRules` PUBLIC'tir; invariant tek kaynaktan zorlanır |
| K-713 | 760 | `EvalCaseResult.Scores` değer/derece/tanı yazar; `Metadata` ve `Context` YAZILMAZ 👤 |
| K-714 | 761 | `IEvalStore` `DiffRunsAsync` üyesini kazanır; hizalama politikası `Tracon.Abstractions` içindeki PUBLIC `EvalRunDiffBuilder`'dadır, `Core`'da değil |
| K-715 | 762 | Karşılaştırılamayan iki koşum İSTİSNA atar (`EvalRunDiffUnavailableException`); uç `409`/`400` döner, BOŞ FARK asla dönmez |
| K-716 | 763 | `ContentChanged` bayrağı KAPSAM DIŞI; case içeriği koşum başına saklanmaz ve sınır sözleşmeye yazılır 👤 |
| K-717 | 764 | Yalnız `Completed` koşumlar karşılaştırılır; diğer her durum `400` döner 👤 |
| K-718 | 765 | `tracon eval` DÖRDÜNCÜ bir çıkış kodu kazanır: `4` = karşılaştırılamadı; `3` (kapı düştü) anlamı değişmez |
| K-719 | 766 | Bir koşumda bir case'in BİRDEN ÇOK sonuç satırı varsa case yalnız HEPSİ geçtiyse geçmiş sayılır |
| K-720 | 767 | Süreçten ÇIKAN metin sayılarını `CultureInfo.InvariantCulture` ile biçimler; risk yalnız ONDALIK ve YÜZDE belirteçlerindedir, analyzer bu sınıfı GÖRMEZ |
| K-721 | 768 | `KARARLAR.md` bütçesi 390.000 → 420.000; önce TAŞIMA denendi ve `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı |
| K-722 | 769 | Sevk edilen agent haritasının tavanı 10 KiB → 11 KiB 👤 |
| K-723 | 770 | `capabilities.md` tablosunda KAÇIRILMIŞ boru (`\|`) hücreyi bölmez |
| K-724 | 771 | SQL Server `run_scores` upsert'i artık ham `message_id`'yi değil, `message_key AS ISNULL(message_id, N'') PERSISTED` computed column'unu indeksler ve eşler; migration mevcut `NULL`/`''` çiftlerini en yeni `created_at` kalacak şekilde dedupe eder |
| K-725 | 772 | `AgentDefinitionRequest` tüketiciye ait HER `AgentDefinition` alanını taşımak ZORUNDADIR; `PUT /api/agents/{name}` tam değiştirme (full-replace) semantiğinde KALIR |
| K-726 | 773 | Approval kararında AYNI cevapla gelen tekrar `409` DEĞİL, handoff'u tamamlayan `200` döner; yalnız TERS cevap `409`'dur. Resume run kimliği approval'dan TÜRETİLİR (`TraconId.DeriveId`) |
| K-727 | 774 | `Microsoft.Extensions.AI.Evaluation.Quality` katalogu sevk edilen HİÇBİR pakete girmez; `Tracon.Core` yalnız `Microsoft.Extensions.AI.Evaluation`'ı AÇIK referanslar 👤 |
| K-728 | 775 | `RunJudgment.Score`/`Reason` KALDIRILDI; bir yargıç `IReadOnlyList<JudgeScore> Scores` döndürür 👤 |
| K-729 | 776 | Köprü metrik adını `{judge}.{metrik}` olarak önekler; ayırıcı `:` KULLANILAMAZ 👤 |
| K-730 | 777 | Yalnız MANŞET skor (adı yargıcın adına EŞİT olan) online değerlendirme penceresine ve `tracon.judge.score` histogramına girer |
| K-731 | 778 | `IEvalEvaluatorFactory` public'tir; eval suite seam'i çıplak bir `IAgentEvaluator` DEĞİL bir FABRİKADIR |
| K-732 | 779 | Sözleşmeyi ihlal eden bir yargıç FIRLATMAZ; `judge_contract` `JudgeFailure` olarak raporlanır ve doğrulama İLK YAZMADAN ÖNCE toplu yapılır |
| K-733 | 780 | Durum ön kontrolü AYRI bir salt okunur SQL yüzeyinden okur (`IStatePreflightReader`); `ISessionStore.QueryAsync` bu iş için YETMEZ 👤 |
| K-734 | 781 | Desteklenen upgrade penceresi: aynı ana sürüm içinde HER sürümden HER sürüme; söz yalnız Tracon'in KENDİ envelope'u içindir 👤 |
| K-735 | 782 | Ön kontrol ÇÖZEMEDİĞİ şifreli satırı hata SAYMAZ; "yapı kontrolü" olarak raporlar |
| K-736 | 783 | Ön kontrol "örneklem temiz" der, "hepsi okunabilir" DEMEZ; ayrım çıktıda kelimeyle kurulur |
