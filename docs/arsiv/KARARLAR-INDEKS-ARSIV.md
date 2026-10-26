# KARARLAR — İndeks Arşivi

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme). Yeni kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).

## Arşivlenen Kararlar (1040 kalem)

| K | Karar |
|---|---|
| K-001 | Modüler paket ailesi + meta paket 👤 |
| K-002 | Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü 👤 |
| K-003 | Hibrit agent tanımı: kod + çalışma anı veritabanı 👤 |
| K-004 | Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner 👤 |
| K-005 | Hedef framework `net8.0;net9.0;net10.0` 👤 |
| K-006 | Trim/AOT uyumu katman bazlı |
| K-007 | Geçişli sabitleme kapalı |
| K-008 | Ön sürüm MAF bağımlılığı yalnız `Tracon.AspNetCore` içinde |
| K-009 | Sırlar `dotnet user-secrets` ile 👤 |
| K-010 | Arayüz erişimi üç katmanlı 👤 |
| K-011 | Klasör düzeni `src/` + `samples/` + `tests/` 👤 |
| K-012 | Tool'lar yalnız kodda tanımlanır |
| K-013 | Ayrı `tracon` PostgreSQL şeması |
| K-014 | `run_events` append-only |
| K-015 | Birincil anahtarlar `uuid` v7 |
| K-016 | Public API takibi Faz 7'ye ertelendi 🔁 |
| K-017 | Sürümleme MinVer ile git etiketinden |
| K-018 | Bellek içi store'lar birinci sınıf implementasyon |
| K-019 | Agent kaynakları `IAgentSource` ile soyutlandı |
| K-020 | `MAAI001` bastırması tek dosyada toplandı |
| K-021 | Yapılandırma elle bağlanır, `Bind()` kullanılmaz |
| K-022 | Çalıştırma olayı sıra numarasını yazıcı üretir, depo değil |
| K-023 | Kendi UUIDv7 üretecimiz (`TraconId`) |
| K-024 | `IAgentDecorator` genişleme noktası |
| K-025 | `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil |
| K-026 | Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil 👤 |
| K-027 | Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil |
| K-028 | Sağlayıcı ve depolama ayarları kendi alt bölümlerinde 👤 |
| K-029 | Şema adı SQL metnine gömülür, katı doğrulamadan sonra |
| K-030 | Responses API her zaman sunucu tarafı depolama kapalı çalışır |
| K-031 | `OPENAI001` bastırması tek dosyada toplandı |
| K-032 | Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur 👤 |
| K-033 | Tool taraması açık işaretleme ister (`[TraconTool]`) 👤 |
| K-034 | `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir 👤 |
| K-035 | Ayar sınıfları `record` olamaz |
| K-036 | OpenAI uyumlu uçlar Tracon tarafından yazılır, MAF'ın `Map*` uçlarıyla değil 👤 |
| K-037 | `ChatHistoryProvider` `AddTracon()` içinde açıkça kaydedilir |
| K-038 | `/v1/*` uçları OpenAI hata biçimini kullanır, `ProblemDetails` değil |
| K-039 | Kütüphane OpenAPI üretimini dayatmaz, yalnız üstveri taşır |
| K-040 | Enum'lar JSON'da ad olarak yazılır |
| K-041 | Çalıştırma özeti depoda hesaplanır |
| K-042 | `MapTracon()` yalnız korumalı grubun convention builder'ını döndürür |
| K-043 | Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur |
| K-044 | Çalıştırma kimliğini çağıran üretir (`TraconRunOptions`) 👤 |
| K-045 | Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı 👤 |
| K-046 | Arayüz kabuğu bearer token katmanından muaftır 👤 |
| K-047 | Arayüz token'ı `sessionStorage`'da tutulur 👤 |
| K-048 | Arayüz varlıkları Brotli sıkıştırılmış gömülür |
| K-049 | `ITraconUiProvider` tek metotlu tutuldu |
| K-050 | Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır |
| K-051 | `Tracon.UI.csproj` SDK'yı açık `Import` ile yükler |
| K-052 | Frontend birim testleri `npm run build` içinde koşar |
| K-053 | `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı |
| K-054 | Faz 6 kapsamı daraltıldı: Workflows ertelendi 👤 |
| K-055 | Span'ler `ActivityListener` ile toplanır, exporter ile değil |
| K-056 | Örnekleme kararı çalıştırma bittiğinde verilir |
| K-057 | `ModelContextProtocol.Core`, tam `ModelContextProtocol` paketi değil |
| K-058 | MCP'de yalnızca uzak HTTP aktarımı; stdio yok |
| K-059 | MCP kimlik doğrulama değeri veritabanında saklanmaz |
| K-060 | MCP tool adları `{sunucu}_{tool}`; nokta kullanılmaz |
| K-061 | "Bir daha sorma" MAF'ın biçimiyle değil Tracon deposunda tutulur |
| K-062 | Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı 👤🔁 |
| K-063 | `run_events` partition'ı açılmadı (kapandı → K-199) |
| K-064 | İkinci faz önceliği: yetenek derinliği 👤 |
| K-065 | Ses: hedef gerçek zamanlı konuşma katmanı 👤 |
| K-066 | Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası 👤 |
| K-067 | Alt agent çalıştırması ayrı bir `runs` satırıdır 👤 |
| K-068 | Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz 👤🔁 |
| K-069 | `UseOpenAICompatible(ad, ...)` ayrı ad, `UseOpenAI` aşırı yüklemesi değil 👤 |
| K-070 | Model sağlayıcı devre kesici varsayılan olarak açık 👤 |
| K-071 | Sağlayıcı sağlık denetimi arka planda varsayılan olarak koşmaz 👤 |
| K-072 | Sağlayıcı adı deseni regex değil elle karakter denetimiyle doğrulanır |
| K-073 | Sağlık denetimi hata detayında `HttpRequestException.Message` değil `HttpRequestError` kategorisi kullanılır |
| K-074 | Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi |
| K-075 | Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapTracon()` çağrısında bir kez yapılır |
| K-076 | Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil |
| K-077 | Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır |
| K-078 | `/api/meta` yanıtına rol bilgisi (`roles`) eklendi |
| K-079 | `mcp.refresh` denetim izine uç katmanında yazılır, depo dekoratöründe değil |
| K-080 | Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil |
| K-081 | Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler |
| K-082 | Skill sürüm geçmişi tutulmaz 👤 |
| K-083 | `AllowedTools` yalnız saklanır, zorlanmaz 👤 |
| K-084 | Bir agent için varsayılan skill sınırı 10'dur 👤 |
| K-085 | Derlenmiş agent cache anahtarında skill parmak izi vardır |
| K-086 | Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz |
| K-087 | Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir 👤 |
| K-088 | Yorumlayıcı beyaz listesi varsayılan olarak boştur |
| K-089 | Denetim izine yazılamayan bir script çalıştırılmaz |
| K-090 | Argüman doğrulaması sığdır; tam JSON Schema doğrulayıcı eklenmedi |
| K-091 | Argümanlar script sürecine stdin ile geçirilir |
| K-092 | İzin kaydı silinmez, `revoked_at` ile iptal edilir |
| K-093 | Alt çalıştırma ayrı bir `runs` satırıdır |
| K-094 | `root_run_id` denormalize edilir |
| K-095 | `runs.parent_run_id` için yabancı anahtar konmaz |
| K-096 | Bütçe ağaç boyunca tek nesnedir; `AgentRunBudget` bilerek `class`'tır |
| K-097 | Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır |
| K-098 | Alt agent gec (lazy) çözülür; derleme anında çözülmez |
| K-099 | Trace tamponunun sahibi yalnız kök çalıştırmadır |
| K-100 | `RunQuery.OnlyRootRuns` varsayılanı `true` 👤 |
| K-101 | Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma 👤 |
| K-102 | Alt çalıştırmalar kök akışa yalnız özet olay yazar 👤 |
| K-103 | Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur 🔁 |
| K-104 | Sıkıştırma bağımlılığı için yeni paket alınmadı |
| K-105 | `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında 👤🔁 |
| K-106 | Sıkıştırma varsayılan kapalıdır 👤 |
| K-107 | Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez 👤 |
| K-108 | Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir |
| K-109 | `Pipeline` sırası sabittir: ToolResult → SlidingWindow → Summarization |
| K-110 | `TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır; bu faz `InMemoryAgentFileStore` |
| K-111 | İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans |
| K-112 | `attachments.session_id` yabancı anahtar DEĞİLDİR |
| K-113 | Ek türü sihirli bayta göre doğrulanır; istemcinin bildirdiği `Content-Type` yok sayılır |
| K-114 | Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur |
| K-115 | `IFormFile` alan minimal API uçları `.DisableAntiforgery()` gerektirir |
| K-116 | `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez |
| K-117 | Kalıcı agent dosya belleği (`agent_files`) aynı migration'da (0006) eklendi 👤 |
| K-118 | Workflow yürütmesi ayrı pakette (`Tracon.Workflows`), `AspNetCore` bu pakete referans vermez |
| K-119 | Beş desenin tamamı Faz 15'te uygulandı 👤 |
| K-120 | Workflow çalıştırmaları `runs` tablosunda yaşar; `workflow_runs` açılmadı |
| K-121 | Kontrol noktası durumu `json` sütununda, `jsonb` DEĞİL |
| K-122 | 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir |
| K-123 | Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar |
| K-124 | `Magentic` plan onayı Faz 15'te kapalı (`RequirePlanSignoff(false)`) |
| K-125 | `GroupChat` yönetici agent kabul etmez; `managerAgentName` yalnız `Magentic` içindir |
| K-126 | Workflow tanımlarında sürüm GEÇMİŞİ tutulmaz 🔁 |
| K-127 | 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır |
| K-128 | Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar 🔁 |
| K-129 | `Microsoft.Agents.AI.Workflows.Declarative` alınmadı 👤 |
| K-130 | Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar |
| K-131 | Graf tanımdan değil, DERLENMİŞ workflow'dan çıkarılır |
| K-132 | Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı |
| K-133 | Graf hatası çalıştırmayı `Failed` yapar |
| K-134 | `WorkflowJobHandler` ayrı bir pakete değil, `Tracon.Core`'a konur |
| K-135 | Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil |
| K-136 | Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır |
| K-137 | İş iptali ayrı bir `cancel_requested` sütunu gerektirmez; `jobs.status` tek gerçek kaynaktır |
| K-138 | `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti |
| K-139 | Eval (Faz 18) için yeni bir NuGet paketi eklenmedi |
| K-140 | AI yargıç (`AIJudgeLoopEvaluator`/`LoopAgent`) Faz 18 kapsamı dışında bırakıldı — (yeniden açıldı: 2026-08-07, Faz 49 ile karşılandı, bkz. K-327) 🔁 |
| K-141 | Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi 👤 |
| K-142 | `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir |
| K-143 | Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context) |
| K-144 | Deney atama anahtarı varsayılan olarak oturum kimliğidir; `Experiment.AssignmentKey` bu fazda rezerve 👤 |
| K-145 | `EvalRunTriggerRequest.AgentVersion` eklendi; eval, A/B deneyinden tamamen bağımsız bir mekanizmadır 👤 |
| K-146 | `tracon.agent.version` metrik/span etiketi varsayılan açıktır (`IncludeAgentVersionTag = true`); `experiment_id`/`variant` hiçbir zaman etiket olmaz 👤 |
| K-147 | A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür |
| K-148 | Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi |
| K-149 | `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı |
| K-150 | Maliyette para birimi dönüşümü yapılmaz 👤 |
| K-151 | Ağaç maliyeti kendi maliyetiyle toplanmaz; iki ayrı alan 👤 |
| K-152 | `/api/stats` Eval/Workflow'u hariç tutmaya devam eder (K-141); yeni `/api/stats/timeseries` bilerek hariç TUTMAZ 👤 |
| K-153 | `POST /api/stats/recalculate-costs` eklendi: Admin + denetim izi 👤 |
| K-154 | Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır |
| K-155 | `Tracon:Pricing` `TraconOptions`'ta (Core), bir saglayici paketinde değil |
| K-156 | Dashboard'un devre kesici uyarısı mevcut `/api/models/health` `Unhealthy` durumunu kullanır; yeni uç eklenmedi |
| K-157 | `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı |
| K-158 | Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında |
| K-159 | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir |
| K-160 | Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi 👤 |
| K-161 | Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez 👤 |
| K-162 | Kota aşımında devam eden çalıştırma KESİLMEZ; yalnızca yeni çalıştırma reddedilir 👤 |
| K-163 | Webhook imzası zaman damgasını İÇERİR |
| K-164 | SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz |
| K-165 | Hız sınırının varsayılanı KAPALIDIR 👤 |
| K-166 | `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür |
| K-167 | `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil |
| K-168 | MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok |
| K-169 | MCP OAuth geri dönüş adresi (`OAuthCallbackBaseUri`) sabit bir ayardır, istekten türetilmez |
| K-170 | MCP OAuth token'ları `(kiracı, sunucu)` başına tek bellek içi önbellekte, iki tüketici arasında paylaşılır |
| K-171 | Arka plandaki (etkileşimsiz) OAuth denemesi hemen başarısız olur, beklemez |
| K-172 | `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir |
| K-173 | Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi |
| K-174 | Mod A kaynak okuması `AgentDefinitionCompiler`'a `IMcpResourceContextProviderFactory` soyutlamasıyla bağlanır |
| K-175 | MCP kaynak toplu okuma önbelleği `ConcurrentDictionary`'e geçirildi |
| K-176 | SQL kalıcılık mantığı `Tracon.Sql.Shared` altında PAYLAŞILAN KAYNAK olarak yaşar 👤 |
| K-177 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ |
| K-178 | Migration numaraları sağlayıcı başına bağımsızdır |
| K-179 | Şema adı kuralı iki sağlayıcıda AYNIDIR |
| K-180 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda |
| K-181 | `Tracon.SqlServer` AOT uyumlu olarak İŞARETLENMEZ |
| K-182 | Diziler SQL Server'a JSON metni olarak taşınır |
| K-183 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır |
| K-184 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez |
| K-185 | `Tracon` meta paketi `Tracon.SqlServer`'ı İÇERMEZ |
| K-186 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 |
| K-187 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti |
| K-188 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor |
| K-189 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu |
| K-190 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır |
| K-191 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ |
| K-192 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ |
| K-193 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR |
| K-194 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING` |
| K-195 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir |
| K-196 | `Tracon.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi) |
| K-197 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni) |
| K-198 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz |
| K-199 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı) |
| K-200 | Parti silme her sağlayıcıda farklı teknik kullanır |
| K-201 | `MaxRows` var ama uygulanmıyor (ertelendi → kapandı K-258) |
| K-202 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır |
| K-203 | `sessions`/`conversations` ayrı hedeftir |
| K-204 | Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil 👤 |
| K-205 | `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi 👤 |
| K-206 | İçerik filtresi tespiti `Tracon.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil 👤 |
| K-207 | Paket adı `Tracon.Google`, sağlayıcı adı `google` 👤 |
| K-208 | `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır 👤 |
| K-209 | `Tracon` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ |
| K-210 | Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı 👤 |
| K-211 | `azure-openai` hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırıktır |
| K-212 | Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık 👤 |
| K-213 | Azure'ın Responses yüzeyi desteklenmiyor |
| K-214 | `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı |
| K-215 | Ses sözleşmeleri `Tracon.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır |
| K-216 | `Tracon.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır |
| K-217 | `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir |
| K-218 | Tool bağımlılıkları KURULUM anında alınır; `AIFunctionArguments.Services` MAF boru hattında boştur |
| K-219 | `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz |
| K-220 | `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz |
| K-221 | Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir |
| K-222 | Konuşma katmanı Seçenek A ile ve `Tracon.Core`'da |
| K-223 | `MapTracon` `UseWebSockets()`'i koşullu olarak kendisi kurar |
| K-224 | WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez |
| K-225 | `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz 👤 |
| K-226 | Artımlı (geçici) transkript yok; çözüm tek atımlıdır 👤 |
| K-227 | Ses dakikası bir kota birimi değildir 👤 |
| K-228 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı |
| K-229 | `t` fonksiyonu modül düzeyindedir ve kimliği hiç değişmez |
| K-230 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır (K-047) |
| K-231 | Varsayılan dil tarayıcıdan gelir 👤 |
| K-232 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir |
| K-233 | Rozet metni küçük harf, süzgeç/başlık metni büyük harf: iki ayrı anahtar kümesi |
| K-234 | Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu |
| K-235 | Konuşma çözümlemesine dil kodu gönderilmez |
| K-236 | `--ap-subtle` ve `--ap-muted` WCAG AA'ya göre düzeltildi |
| K-237 | Klavye kısayolu metin alanında tetiklenmez; `Ctrl+Enter` yereldir |
| K-238 | Komut paleti istemci tarafında arar |
| K-239 | `run_scores`: `author` NULL'ı sağlayıcı bazlı işlenir |
| K-240 | `ScoredRuns`/`PositiveRate` iki yolla hesaplanır |
| K-241 | `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi |
| K-242 | `FeedbackControl` ikili puan gösterir, yıldız YAZILMADI |
| K-243 | Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ |
| K-244 | `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok |
| K-245 | `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır |
| K-246 | İptal isteği `run.cancel` eylemiyle denetim izine yazılır |
| K-247 | K-183'ün isareti `Tracon.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `Tracon.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı |
| K-248 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok |
| K-249 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `Tracon:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir |
| K-250 | `TraconDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `Tracon.AspNetCore.TraconHealthCheck` içindedir |
| K-251 | `AddTraconHealthChecks()` `AddTracon()`'in önceden çağrıldığını KAYIT ANINDA denetlemez |
| K-252 | Döngü denetimi `AgentCallGraph.ValidateDetailed`'e taşındı, yeni dosya yok |
| K-253 | `TraconValidationOptions.McpTimeout` Core'a eklendi, AspNetCore'a değil |
| K-254 | Kota ölçerine `quota.metric` eklendi 👤 |
| K-255 | Kota ölçeri `usage`+`limit` için AYRI iki `ObservableGauge`'dur 👤 |
| K-256 | `QuotaUsageObserver` senkron kapılı önbellektir, zamanlayıcı değil 👤 |
| K-257 | Kota ölçeri yalnız KAYITLI kiracıları tarar |
| K-258 | `MaxRows` sıra, silme adımından çıkarılarak uygulandı (K-201 kapandı) |
| K-259 | Saklama korelasyonları BARE hedef adı değil, TAM NİTELENDİRİLMİŞ ad kullanır (Faz 25 hatası düzeltildi) |
| K-260 | `MaxRows` kiracı genelinde uygulanır, kiracı başına DEĞİL (Faz 36 planının Açık Soru 3'ünden sapma) |
| K-261 | `eval_case_results`/`workflow_checkpoints` için `MaxRows` eşiği İLİŞKİLİ tablo üzerinden hesaplanır (Açık Soru 2 çözüldü) |
| K-262 | Şablonda `IncludeSymbols=false` zorunlu |
| K-263 | Şablonda `TargetFrameworks` boşaltılır |
| K-264 | Şablon içeriği `<None Pack>` ile paketlenir |
| K-265 | Şablon paket sürümü varsayılanı kayan `*-*` |
| K-266 | Üretilen `OrderTools.cs` `using Tracon;` taşır |
| K-267 | `ModelBinding.ResponseFormat` yetenek denetimi yalnız `Json`/`JsonSchema` kiplerinde çalışır |
| K-268 | `TemplateFixture` sablon testleri icin tam cozum yerine `Tracon.src.slnf` (yalniz `src/` paketlerini listeleyen bir cozum filtresi) paketler |
| K-269 | `Tracon.Testing.FakeModelProvider` modele ozel, BIR KEZ tuketilen bir yanit kuyrugu tutar; mesaj gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz |
| K-270 | `Tracon.Testing` yalniz `net10.0` hedefler (cogul `TargetFrameworks` ozelligiyle ezilerek) 🔁 |
| K-271 | `tests/Tracon.Core.UnitTests/Fakes/FakeModelProvider.cs` SILINMEDI (plandan sapma) |
| K-272 | Uç etiketleri TEK `.WithTags("Tracon", "<Alan>")` çağrısıyla verilir |
| K-273 | SSE/ikili yanıtlar `Produces<T>` ile tiple bildirilir; `responseType: null` içerik tipini tamamen düşürür |
| K-274 | Aynı statü koduna birden fazla `.Produces` çağrısı yapılmaz; çoklu içerik tipi TEK çağrıya `additionalContentTypes` ile yazılır |
| K-275 | On bir ham `Task<IResult>` ucunun tamamı yol B (`.Produces`/`.ProducesProblem` üstverisi) ile belgelendi; hiçbiri yol A'ya (`Results<...>` imza değişikliği) taşınmadı |
| K-276 | `docs/openapi/tracon.json` üretim kaynağı `Tracon.AspNetCore.FunctionalTests`'tir, `samples/Tracon.Api` DEĞİL |
| K-277 | Bellek içi depolar `ITenantContext` alır; kiracı süzgeci artık isteğe bağlı değildir |
| K-278 | `sessions` birincil anahtarı `(tenant_id, id)`; oturum kimliği kiracı içinde benzersizdir |
| K-279 | Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu `tenantId` alır 👤 |
| K-280 | Çalıştırmanın alt yazmaları ambient kiracıyla süzülmez; `[TenantAgnostic]` ile gerekçesi yazılır |
| K-281 | `TenantAgnosticAttribute` `Tracon.Sql.Shared` içinde ve `internal`'dir |
| K-282 | Kiracı yalıtımı iki depo örneğiyle değil, değiştirilebilir tek bir kiracı bağlamıyla sınanır |
| K-283 | Görünmeyen bir oturum YOK sayılır; "başkasının oturumu" reddi kaldırıldı |
| K-284 | Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil |
| K-285 | `SingletonGuard` `public`tir; "internal yardımcı" planı uygulanamadı 🔁 |
| K-286 | Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn taban); gerçek devralma ölçüldü |
| K-287 | Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil |
| K-288 | `/api/agents/{name}/run` `Idempotency-Key` varsa akışsız (JSON) çalışır; plandan sapma (kullanıcı kararı) 👤 |
| K-289 | `TraconIdempotencyOptions` `Tracon.Core`'dadır, plandaki gibi `Tracon.AspNetCore`'da değil |
| K-290 | `idempotency_keys` için ayrı bir `Retention: TimeSpan` alanı yerine standart `RetentionTargets`/`TraconRetentionOptions` üçlüsü kullanıldı |
| K-291 | Idempotency desteği varsayılan AÇIKTIR (`Enabled = true`); K1'in "varsayılan kapalı" kuralının bilinçli bir yorumu |
| K-292 | Ham govde, filtreden ÖNCE `MapTracon` içine eklenen koşullu bir ara yazılımla tamponlanır |
| K-293 | `RunError` sınıf/parmak izini doğrudan taşır; ayrı bir arama tablosu açılmadı |
| K-294 | Sınıflandırma TEK bir noktada, `RunRecordingAgent.CompleteAsync` içinde, `error` `null` değilse çalışır |
| K-295 | `ByErrorClass` ayrı bir depo metodu değil, `GetStatisticsAsync`'in genişletilmiş sonucu; `/api/stats/errors` o sonucun dar bir dilimi |
| K-296 | Hata sınıflandırıcının SDK istisna adları örnek uygulamada gerçek bir OpenAI hatasıyla ölçüldü; `ClientResultException` eksikti |
| K-297 | `quota_exceeded` sınıfı otomatik sınıflandırıcı için YAPISAL olarak ulaşılamazdır; taksonomide kalır ama örnek uygulamada uçtan uca gösterilemedi |
| K-298 | Parmak izi normalleştirmesi tırnak içi metni SİLMEZ (Açık Soru 3 → C) |
| K-299 | Sınıf başına en sık üç küme, pencere fonksiyonlarıyla (`ROW_NUMBER()`/`COUNT() OVER`) tek geçişte hesaplanır — bu desenin kod tabanındaki İLK kullanımı |
| K-300 | Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur; plan taslağının "run_events zaten kullanıcı girdisini taşır" iddiası yanlıştı ve düzeltildi (kullanıcı kararı) 👤 |
| K-301 | Çok turluluk, "bu oturumda DAHA ÖNCE başlamış başka bir çalıştırma var mı" sorusuyla belirlenir; tam konuşma geçmişi okunmaz |
| K-302 | `AddCaseAsync`'in `seq`/`source_run_id` eşzamanlılığı `ON CONFLICT`/`MERGE` değil, düz `INSERT` + `SqlDialect.IsUniqueViolation` yakalama + yeniden deneme ile çözülür |
| K-303 | "Olumsuz puan" otomatik terfi tetikleyicisi olarak `Binary` için `Value == 0`, `Stars` için `Value <= 2` (5 üzerinden) tanımlandı |
| K-304 | Kuyruğa alınan (`Prefer: respond-async`) bir çalıştırmanın `runs` satırı, işçinin gerçek yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync` bu yüzden bir UPSERT'tir |
| K-305 | Kuyruğa alınan bir çalıştırmada `JobRecord.Id` ile `RunRecord.Id` bilinçli olarak AYNI değeri taşır |
| K-306 | `TraconAsyncRunOptions.MaxAttempts` varsayılanı `1`'dir (kullanıcı kararı) 👤 |
| K-307 | `TraconAsyncRunOptions.Enabled` varsayılanı `true`'dur (kullanıcı kararı) 👤 |
| K-308 | Çalıştırmanın girdisi AYRI bir `run_inputs` tablosunda ve `json` sütununda saklanır; `runs`'a sütun EKLENMEZ |
| K-309 | Varsayılan tool modu `ReplayTools`; kayıtlı sonucu olmayan bir çağrı yeniden oynatmayı DURDURUR ve `422` döner |
| K-310 | `LiveTools` `Admin` rolü ister ve onay gerektiren bir tool taşıyan agent bu modda çalıştırılamaz (`409`) |
| K-311 | Dallanma öğeleri KOPYALAR; işaretçi zinciri reddedildi |
| K-312 | `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ |
| K-313 | Konusma dallandırma yalnız SQL sağlayıcısı açıkken çalışır; bellek içi kurulumda uç `501` döner (kullanıcı kararı) 👤 |
| K-314 | Kod kaynaklı agent'lar model bindirmesi ve `NoTools`/`ReplayTools` ile oynatılamaz; `400` döner (kullanıcı kararı) 👤 |
| K-315 | Yeniden oynatma OTURUMSUZDUR |
| K-316 | Yeniden oynatma yalnız SENKRON ve akışsızdır; kuyruğa alma bu fazın kapsamı dışındadır |
| K-317 | `azure-sql-edge` artık güvenilir bir yerel doğrulama ikamesidir; hazır-olma denetimi `sqlcmd` yerine ADO.NET ile yazılmalıdır (kullanıcı kararı) 👤 |
| K-318 | SQL Server migration'larında `ALTER TABLE ADD` ile eklenen sütunu AYNI toplu işlemde `CREATE INDEX`'te kullanmak "Invalid column name" verir; dört migration dosyası etkiliydi |
| K-319 | `EvalCaseResult.Scores` ayarlanmamışken (varsayılan `JsonValueKind.Undefined`) SQL Server'a `"[]"` yazılır, `"null"` DEĞİL — `ISJSON` kısıtı bare `null`'ı reddeder |
| K-320 | Model çağrı boru hattının TAMAMINI `ModelProviderRegistry` kurar; `IModelProvider` HAM istemci döndürür (kullanıcı kararı) 👤 |
| K-321 | İçerik guard'ı `IChatClient` katmanındadır ve tool çağrı döngüsünün İÇİNDEDİR |
| K-322 | Devre kesici içerik engellemesini ardışık hata SAYMAZ |
| K-323 | Yeni bir genişleme noktasının "varsayılan kapalı" kapısı KAYITTIR, bir `Enabled` bayrağı değildir (kullanıcı kararı) 👤 |
| K-324 | `422` yalnızca AKIŞSIZ çalıştırma dalında dönebilir (kullanıcı kararı) 👤 |
| K-325 | Engellenen veya maskelenen içerik HİÇBİR yere yazılmaz |
| K-326 | `RunErrorClass.ContentBlocked` `ContentFiltered`'dan AYRIDIR |
| K-327 | `AIJudgeLoopEvaluator` KULLANILMADI; `IRunJudge` sıfırdan yazıldı (Faz 49) |
| K-328 | Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrılır; yeni bir sütun açılmadı (Faz 49) |
| K-329 | İki kapılı varsayılan: `OnlineEvaluationOptions.Enabled = false` VE `SampleRate = 0.0` (Faz 49) |
| K-330 | Yargıcın `IChatClient`'ı guard boru hattından GEÇER; engelleme özel olarak ele alınmadı (Faz 49, D1) |
| K-331 | `RunScore.Author` yargıç puanlarında `judge:{ad}` ile BİLEREK DOLU yazılır (Faz 49) |
| K-332 | Cevrimiçi değerlendirme pencere özeti BELLEK İÇİDİR; yeni bir SQL sorgu yüzeyi açılmadı (Faz 49) |
| K-333 | `OnlineEvalJobHandler` DI'da hem `IJobHandler` hem KENDİ somut tipiyle kayıtlıdır (Faz 49) |
| K-334 | K-057 güncellendi: Tracon artık MCP istemcisi VE sunucusudur (Faz 50) |
| K-335 | A2A sunucu maliyeti yeniden ölçüldü: "+2 paket" değil "+4 paket"; iki paket plan taslağında hiç yoktu (Faz 50) |
| K-336 | A2A her disa acik agent icin AYRI bir alt yol ve AYRI bir agent karti kullanir; tekil kart varsayimi terk edildi (Faz 50) |
| K-337 | MCP/A2A dış çağrısı `ChildAgentInvoker`'ı KULLANMAZ; `ExternalAgentProxy`/`CatalogToolCallHandler` her zaman YENİ bir kök çalıştırma açar (Faz 50) |
| K-338 | MCP/A2A dış yüzeyleri erişim ayarlarını `IApplicationBuilder.Properties` üzerinden `MapTracon`'den DEVRALIR; `MapTracon` önce çağrılmalıdır (Faz 50) |
| K-339 | `ChildRunApproval` public yapıldı: üçüncü tüketici MCP/A2A dış çağrı katmanıdır (Faz 50) |
| K-340 | Dışa açılan bir agent'ın `AgentRunBudget.MaxDepth` değeri, kaç seviye TORUN çağrısına izin verildiğidir; "0" = hiç, "1" = bir seviye (Faz 50) |
| K-341 | Vektör gömüsü metin biçiminde (`::vector` cast) yazılır, hiçbir vektör paketi alınmaz (Faz 51) |
| K-342 | K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş (Faz 51) 👤🔁 |
| K-343 | Anlamsal arama yalnız PostgreSQL'de uygulanır (Faz 51) 👤 |
| K-344 | Sql.Shared'in cross-provider katmanı `IVectorSearchStore` için kullanılmaz (Faz 51) |
| K-345 | `document_embeddings.metadata` sütunu `jsonb`'dir, `json` değil (Faz 51) |
| K-346 | Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues` (Faz 51) |
| K-347 | K-218 kapatıldı: `ToolMethodScanner` örnek metotları TARAMA ANINDA reddeder (Faz 52) |
| K-348 | Kaynak üreteci ayrı bir NuGet paketi değildir; `Tracon.Core` nupkg'sinde `analyzers/dotnet/cs/` altında taşınır (Faz 52) |
| K-349 | Kaynak üretecinin Roslyn sürümü `Microsoft.CodeAnalysis.CSharp` `4.8.0`'dır (Faz 52) |
| K-350 | `AddGeneratedTools()` `ITraconBuilder`'a EKLENMEDİ; derlemeye özel üretilmiş bir uzantı metodudur (Faz 52) |
| K-351 | Parametre tipi beyaz listesi `record`/`class` (composite) tipleri KAPSAMAZ; `TRC0003` ile reddedilir (Faz 52) |
| K-352 | F-76 (OpenAPI 500) YALNIZ `ProjectReference` tüketicisini etkiler; düzeltme kütüphanede değil, örnek uygulamanın derlemesindedir (2026-08-08 denetimi) |
| K-353 | `.UseMcp()` yapılandırmayı AÇIKÇA alır; Tracon kendiliğinden `IConfiguration` okumaz (2026-08-08 denetimi) |
| K-354 | SQL'e dokunan arka plan servisleri `SchemaReadyGate`'i bekler; hosted service kayıt sırası ZORLANMAZ (2026-08-08 denetimi) |
| K-355 | Çalıştırmanın alt yazmaları BEKLENEN kiracıyı taşır; ambient kiracı KULLANILMAZ ve alan HTTP sözleşmesine girmez (2026-08-08 denetimi) |
| K-356 | API anahtarı ozeti SHA-256'dır; Argon2 DEĞİL (Faz 53, Açık Soru 2) |
| K-357 | API anahtarı doğrulaması önbelleklenmez; her istekte `key_hash` ile SQL aranır (Faz 53, Açık Soru 3, seçenek A) |
| K-358 | `last_used_at` her istekte değil, en az bir dakikada bir yazılır (Faz 53, Açık Soru 4, seçenek B) |
| K-359 | `Authorization` başlığı sunulduğunda, statik `AuthToken` tanımsız olsa bile doğrulanmalıdır; eşleşmezse 401 (Faz 53, bilinçli davranış değişikliği) |
| K-360 | API anahtarı kapsam (`scope`) denetimi yalnız agents/runs/external-invoke uçlarına uygulandı; tam taksonomi ERTELENDİ (Faz 53) |
| K-361 | `docs/MIMARI.md` sıcak yol bütçesi 42.000 → 44.000 bayt (Faz 53) |
| K-362 | Heartbeat toplu yazılır: `IRunStore.TouchHeartbeatAsync` tek `Guid` değil `IReadOnlyCollection<Guid>` alır (Faz 54, Açık Soru 1, seçenek B) |
| K-363 | `RunErrorClass.Infrastructure` yeni değer olarak eklendi (Faz 54, Açık Soru 3, seçenek A'nın düzeltilmiş hâli) |
| K-364 | Oksuz hata parmak izi SABİT bir dize (`"orphaned"`); `ErrorFingerprint.Compute` ÇAĞRILMAZ (Faz 54) |
| K-365 | Heartbeat/oksuz-kapama sorguları dizi parametresi (`WHERE id IN (@array)`) KULLANMAZ; tekil `UPDATE` döngüsü ve alt-sorgulu tek `UPDATE` tercih edildi (Faz 54) |
| K-366 | `ClaimOrphanedRunsAsync` kapattığı çalıştırmanın `RunFailed` olayını da KENDİSİ yazar; ayrı bir yazma turu yok (Faz 54) |
| K-367 | `MapTraconMcpServer`/`MapTraconA2A` onay-yüzeyi denetimi, `Map*()` sırasında senkron çalışan bir kontrolden istek-bazlı `IEndpointFilter`e taşındı (kullanıcı kararı) 👤 |
| K-368 | `RunStatus.AwaitingApproval` eklendi; onay kararından sonra AYNI `RunId` devam ETMEZ, `AwaitingInput` emsaliyle birebir aynı şekilde YENİ bir çalıştırma açılır (Faz 55, kullanıcı kararı) 👤 |
| K-369 | `pending_approvals.run_id`, `runs(id)`e `ON DELETE CASCADE` ile bağlı; sözleşme testleri gerçek bir `runs` satırı önceden kuran bir `PrepareRunAsync` kancası kazandı (Faz 55) |
| K-370 | `ApprovalEndpoints.DecideAsync`, `audit` kaydını mutasyondan ÖNCE ve `AuditRecorder.WriteAsync` (hataları yutan sarmalayıcı) DEĞİL doğrudan `IAuditLog.WriteAsync` ile yazar (Faz 55) |
| K-371 | `IPendingApprovalStore.ExpireAsync`, planın taslak imzası `ValueTask<int>` yerine `ValueTask<IReadOnlyList<PendingApproval>>` döner (Faz 55, plandan sapma) |
| K-372 | Senkron/MCP/A2A çalıştırma yolu `pending_approvals`'a HİÇ yazmaz; yalnız kuyruktan koşan (`Prefer: respond-async`) çalıştırmalar yazar (Faz 55, plandan sapma — planın Açık Soru 1 önerisi "B: her ikisi" idi, uygulanan "A: yalnız kuyruk") |
| K-373 | Kanarya kuralı yalnızca İKİ kollu deneylerde tanımlanabilir; planın "kalan kollar kontrol sayılır" (çoğul) ifadesi UYGULANMADI (Faz 56, plandan sapma) |
| K-374 | `ExperimentAssignmentResolver.SelectVariant`, kanarya kuralı tanımlıyken kanarya kolunu HER ZAMAN `[0, ağırlık)` aralığına yerleştirir — bu, `Experiment.Variants`'ın FİZİKSEL sırasından bağımsızdır (Faz 56) |
| K-375 | `CanaryPolicy`'ye ayrı bir `RampRequiresSampleSize` alanı AÇILMADI; kademeli artırma da `MinSampleSize`'ı AYNEN kullanır (Faz 56, plandan sapma) |
| K-376 | `CanaryPolicy`'ye Faz 49'unkine benzer ayrı bir `EvaluationWindow` eklenmedi; kanarya kararı `ExperimentVariantResult`'ın TÜM-ZAMANLI (deney başından beri biriken) sonuçlarına dayanır (Faz 56, plandan sapma, Açık Soru 5) |
| K-377 | `ExperimentVariantResult`e `AverageScore` eklendi; `SelectExperimentResults` sorgusu `run_scores`'a (önce çalıştırma başına ortalama, sonra kol başına o ortalamaların ortalaması) genişletildi (Faz 56) |
| K-378 | Kademeli artırma adımı denetim izine YAZILMAZ; yalnız otomatik GERİ ALMA `IAuditLog.WriteAsync` ile mutasyondan ÖNCE (K-089 emsali) yazılır (Faz 56) |
| K-379 | `IExperimentStore.SetCanaryPolicyAsync`, `SaveAsync`'in Draft-yalnız kısıtından MUAFTIR; kanarya kuralı deney Running iken de tanımlanabilir veya kaldırılabilir (Faz 56) |
| K-380 | `CompiledAgentCache`'in anahtarına `TenantId` eklendi; iki farklı kiracının aynı ad+sürüm+bağımlılık parmak izinde bir tanımı olması artık BİRİNCİ kiracının derlenmiş agent'ını İKİNCİ kiracıya sızdırmaz (manuel kabul testi hazırlığı, kullanıcı talebiyle bulundu) |
| K-381 | Paylaşılan `AgentFileStore` (dosya belleği/metin araması) her kiracı için `TenantPrefixingAgentFileStore` ile sarmalanır; kiracı sınırı MAF'ın kendi deposuna dokunmadan bir vekil (proxy) katmanında uygulanır (manuel kabul testi hazırlığı) |
| K-382 | `TraconEndpointFilter`'a `CheckTenancyWhitelist` eklendi; `AllowedTenants` doluyken listede olmayan bir aday artık istek endpoint'e ulaşmadan 403 ile reddedilir, varsayılan kiracıya SESSİZCE düşmez (manuel kabul testi hazırlığı) |
| K-383 | `Tracon.Core.csproj`'un üreteç-paketleme hedefi `@(Analyzer)` yerine Generators projesinin `GetTargetPath` çıktısını MSBuild görevi ile okur; `dotnet pack --no-build` artık `analyzers/dotnet/cs/Tracon.Generators.dll`'i İÇERİR (manuel kabul testi hazırlığı — daha önce Kritik olarak not düşülmüştü) |
| K-384 | SSE akış uçlarındaki (`AgentEndpoints`, `OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`) dar istisna filtresi kaldırıldı; `OperationCanceledException` dışındaki HER istisna artık bir `error` çerçevesine dönüşür (K-296'nın tamamlanması, manuel kabul testi hazırlığı) |
| K-385 | K-302 güncellendi: `AddCaseAsync`'in yeniden deneme döngüsüne jitter eklendi, üst sınır 5 → 10'a çıkarıldı |
| K-386 | K-317 kapandı: Docker Desktop 4.29.0 → 4.86.0 güncellemesi gerçek `mssql/server`'daki Rosetta hatasını çözdü; SQL Server sözleşme testleri bu makinede artık `azure-sql-edge` ikamesi OLMADAN, gerçek imajla koşuyor |
| K-387 | `SqlServerTestContext.DisposeAsync` artık test şemasını GERÇEKTEN bırakır (dokümantasyon iddia ediyordu, kod yapmıyordu); deadlock'a çarparsa 5 denemeli backoff ile yeniden dener |
| K-388 | `MigrationRunner.ApplyOneAsync` migration SQL'ini ve `INSERT INTO __migrations` kaydını TEK round-trip'te birleştirir; `CreateSchema`+`CreateMigrationsTable` birleşimi ve `SET XACT_ABORT ON` ile tam tek-round-trip BİLEREK yapılmadı |
| K-389 | Migration kilidi VERİTABANI genelinden SEMAYA/ONEĞE kapsandı: `SqlServerDialect`, `PostgresDialect`, `SqliteDialect` artık `sp_getapplock`/`pg_advisory_lock`/kilit dosyası anahtarını sema (veya SQLite'ta tablo öneki) adından türetir |
| K-390 | Sözleşme test izolasyonu "her test kendi şeması" modelinden "her test SINIFI kendi şeması + her test verisini `ResetDataAsync` ile sıfırlar" modeline geçti (K-389 ile birlikte) |
| K-391 | PostgreSQL: `CREATE EXTENSION IF NOT EXISTS vector` içeren `0024_vector.sql`, K-389 sonrası eş zamanlı ilk-kez migrasyonlarda benzersizlik ihlaline (`pg_extension_name_index`, SQLSTATE 23505) düşebilir — `MigrationRunner.ApplyOneAsync` bunu jitter'lı yeniden deneme ile karşılar, test fixture'ı ise uzantıyı bir kez ÖNCEDEN kurarak yarışı tamamen önler |
| K-392 | `InvariantGlobalization` hem örnek uygulamadan (`samples/Tracon.Api`) hem paket şablonundan (`Tracon.Starter`) KALDIRILDI; SQL Server desteğiyle bağdaşmıyor |
| K-393 | `A2AApprovalGuardFilter`/`McpApprovalGuardFilter`: şema hazır değilken (`AutoApplyMigrations=false`) katalog sorgusu HEMEN uygulamayı durdurmak yerine sınırsız sayıda, üstel gecikmeli (5 sn'de tavanlanan) yeniden dener; deneme SAYISI değil yalnız uygulama kapanışı sınırlar (HATA-S1-002, manuel kabul testi S1) |
| K-394 | Workflow çalıştırmaları artık kota muhasebesinden geçiyor: `WorkflowEndpoints.RunAsync` agent'larla AYNI `QuotaGate.CheckAsync` 429 kapısından geçer, `WorkflowRunner.CompleteAsync` workflow'un TAMAMINI (Depth 0, `TreeUsage`/`TreeCost` toplamından) TEK bir "run" olarak `QuotaEnforcer.RecordAsync`'e yazar (HATA-S1-006, Kritik, manuel kabul testi S1) |
| K-395 | `TraconEndpointFilter`: `requireBearerToken:false` gruplarına (eşlenmemiş `api/*` yolları, gerçek zamanlı ses ucu) Tracon'in kendi statik `AuthToken`'ıyla eşleşen bir başlık artık REDDEDİLMİYOR — başlık YOKMUŞ gibi nötr davranılıyor (HATA-S1-014, manuel kabul testi S1) |
| K-396 | `AgentDefinitionCompiler.SearchFileStoreAsync`: `TextSearchProvider`'ın doğal dil sorgusu artık `AgentFileStore.SearchAsync`'e ham regex olarak DEĞİL, boşluğa göre ayrılmış ≥3 karakterlik tokenlerin kaçışlanıp "VEYA" ile birleştirildiği bir desen olarak geçiyor (HATA-S1-009, manuel kabul testi S1) |
| K-397 | `ApiKeyScope`'a `KnowledgeRead`/`KnowledgeAdmin` eklendi; `KnowledgeEndpoints`'in tüm uçları artık `RequireApiKeyScope` çağırıyor (HATA-S1-011, Yüksek, manuel kabul testi S1) |
| K-398 | `RunRecordingAgent.RunCoreStreamingAsync`: terminal durum artık tüketicinin ERKEN `DisposeAsync()`'i (dogal bitiş değil, istisna da değil) durumunda da yazılıyor — `Canceled`, o ana kadar biriken kısmi `usage` ile (HATA-S1-015, Yüksek, manuel kabul testi S1) |
| K-399 | `TraconRetentionOptions`'a `RunInputs`/`VoiceSessions`/`RunScores` (varsayılan sırasıyla 30/30/180 gün) ve `DocumentEmbeddings` (varsayılan KAPALI, `MaxAgeDays=null`) eklendi — dört hedef daha önce `ForTarget`'ta `_ => null` dalına düşüp config varsayılanını SESSİZCE yok sayıyordu (HATA-S1-005, Düşük, manuel kabul testi S1) |
| K-400 | Skill script çalıştırma iki ayrı kök nedenle TAMAMEN çalışmıyordu: (1) resolver'sız `JsonSerializerOptions`, (2) MAF'ın nullable-ama-required arguman semasını `null` ile reddetmesi (HATA-K-002, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-401 | `WorkflowRunner.ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException` sarmalayıcılarını soyar; gerçek neden `RunError.Message`'a yazılır (HATA-K-003, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-402 | `UseWorkflows()` artık `TraconWorkflowOptions`'ı `Tracon:Workflows` bölümünden `IConfiguration`'a BAĞLIYOR (HATA-K-004, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-403 | `WorkflowRunner.RunStreamingAsync` gerçek bir `async IAsyncEnumerable` yineleyicisi yapıldı; `sessionId` doğrulaması artık SSE `event: error` üretiyor, düz `HTTP 500`'e düşmüyor (HATA-K-005, Kritik, manuel kabul testi Ortak Kuyruk) |
| K-404 | `POST/PUT /api/agents` artık `AgentDefinitionValidator.ValidateAsync`'i SAVE ZAMANINDA çağırıyor; bilinmeyen skill/tool/callable-agent adı `400` ile reddediliyor (HATA-K-001, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-405 | `WorkflowEndpoints` artık `ApiKeyScope.WorkflowsRead`/`WorkflowsAdmin`/`RunsRead`/`RunsWrite` uyguluyor (HATA-K-006, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-406 | `BindRunRecording` artık `RecordRunInput`'ı config'ten okuyor (HATA-K-007, Yüksek, manuel kabul testi Ortak Kuyruk — aynı kök neden HATA-S2-002/HATA-S4-015'te de bağımsız bulunmuştu) |
| K-407 | `EvalEndpoints`/`ExperimentEndpoints`/`RunEndpoints` artık Eval/Experiment/`RunsRead`/`RunsWrite` kapsamlarını uyguluyor (HATA-K-008, Yüksek, manuel kabul testi Ortak Kuyruk) |
| K-408 | Kaynak dili sınırı: pakete giren veya çalışma anında çalışan her şey İngilizce'dir; geliştirme aparatı (`docs/`, `.agents/skills/`, `scripts/`) Türkçe kalır 👤 |
| K-409 | Migration `.sql` yorumları İngilizce'ye çevrildi; 61 dosyanın tamamının checksum'ı değişti — var olan bir veritabanına karşı çalıştırmadan önce şema düşürülüp yeniden kurulmalıdır 👤 |
| K-410 | `SourceLanguageTests` eklendi: taban çizgisi güdümlü, kelime sınırlı Türkçe sözlük + aksan taraması; `ProblemDetailsLanguageTests`in genelleştirilmiş kardeşi |
| K-411 | Senkronizasyon kopyası (`<ad> 2.<uzantı>`) taraması `faz-tamamlama` skill'ine KAPI olarak eklendi; kapsam `src tests samples` |
| K-412 | Doküman dizin bütçesi `docs/arsiv/` ve `docs/manuel-test/kosumlar/` HARİÇ ölçülür 👤 |
| K-413 | `docs/YOL-HARITASI.md` ÜRETİLEN dosyadır; kaynak her fazın kendi `> **Durum:**` satırıdır |
| K-414 | Manuel test koşum kaydı ile spesifikasyon AYRI dosyalarda yaşar; ikinci koşum `kosumlar/<tarih>/` kardeşi açar, üzerine yazmaz |
| K-415 | Ürün dokümantasyonu ayrı bir Astro Starlight sitesindedir (`docs-site/`), İngilizce'dir ve `farukatasoy.github.io/Tracon` adresinde yayınlanır 👤 |
| K-416 | API referansı DocFX'in `outputFormat: markdown` çıktısından üretilir ve Starlight içine gömülür; DocFX'in kendi HTML sitesi kullanılmadı |
| K-417 | HTTP API sayfaları OpenAPI belgesinden ÜRETİLİR; Scalar gömülmedi |
| K-418 | 🚨 `AddOpenApi()` ÇIPLAK çağrılmalıdır; yapılandırma `Configure<OpenApiOptions>("v1", ...)` ile AYRI kaydedilir — aksi hâlde ŞEMA XML DOKÜMANI SESSİZCE DÜŞER |
| K-419 | Doküman sitesinin ekran görüntüleri E2E koşumundan üretilir ve commit edilir; elle alınan görsel kabul edilmez |
| K-420 | Yayınlanan `description` metinleri iç doküman referansı taşımaz |
| K-421 | Public API takibi (`EnablePublicApiTracking`) Faz 60'ta, yayın kararından (Faz 7, K-068) bağımsız olarak açıldı 👤 |
| K-422 | RS0026'nın 10 kalemi üç ayrı stratejiyle çözüldü: birleştirme yalnız `ResolveAsync` içindir, geri kalanı ayrıştırma veya statik fabrikadır |
| K-423 | RS0041 (oblivious reference type) `Tracon.Abstractions.csproj`'da tek satırlık `NoWarn` ile bastırıldı; kök neden ölçüldü |
| K-424 | `Tracon.Generators` ve `Tracon.Templates` public API takibinden yeni bir `TraconPublicApiTrackingEnabled` MSBuild özelliğiyle hariç tutuldu |
| K-425 | Aday yeteneği üretimi `aday-kesfi` skill'iyle yazılı bir protokole bağlandı; oturum elemeli diyalog olarak koşar ve her bulgu üç kanala ayrışır 👤 |
| K-426 | Keşif turu kaydı `docs/kesif/<tarih>-<konu>.md` altında yaşar ve doküman dizin bütçesinden HARİÇ tutulur 👤 |
| K-427 | `docs/` kökü YALNIZ canlı dokümanı taşır; kapanmış her kayıt `docs/arsiv/`'e taşınır 👤 |
| K-428 | Aday listesi tur numarası taşımaz: `UCUNCU-FAZ-ADAYLARI.md` → `ADAYLAR.md` 👤 |
| K-429 | Manuel kabul testi PROTOKOLÜ `manuel-test-kosumu` skill'ine taşındı; `docs/manuel-test/` yalnız spec + koşum kaydı taşır 👤 |
| K-430 | `00-INDEKS.md` §7 `Koşum` sütunu koşum kaydından ÖLÇÜLÜR, elle işaretlenmez |
| K-431 | Örnek uygulama rol politikalarını `Tracon:Demo:Roles:Enabled` ile kaydeder ve o anda `RequireRolePolicies`'i AÇAR; şema gösterim amaçlı bir başlık okuyucusudur |
| K-432 | Workflow çalıştırması iptal istendiğinde `Completed` yerine `Canceled` yazılır; zorlama süper-adım sınırında VE pompa çıkışında yapılır |
| K-433 | Pompa, yanıtlanmış bir istekten sonra akışı yeniden açmadan ÖNCE MAF'ın kendi çalıştırma durumunu sorar |
| K-434 | Dosya belleği/metin araması alt ağacı kiracı VE agent adıyla öneklenir; oturumlar arası paylaşım BİLEREK korunur |
| K-435 | `IToolRegistry`/`TraconToolRegistration` `AITool`'a değil `AIFunctionDeclaration`'a genişledi |
| K-436 | İstemci tool'u bildirimi `AIFunctionFactory.Create(...).AsDeclarationOnly()` değil `AIFunctionFactory.CreateDeclaration(...)` ile kurulur |
| K-437 | İstemciden gelen tool sonucu modele `ChatRole.Tool` altında gönderilir, `ChatRole.User` değil |
| K-438 | CORS, `services.AddCors()` OLMADAN `CorsService`/`CorsMiddleware`'in elle kurulmasıyla uygulanır |
| K-439 | `toolResults` eşleştirmesi bilerek İKİ KEZ yapılır: akış başlamadan önce doğrulama, sonra gerçek mesaj kurulumu |
| K-440 | `ClientToolResult.Result`/`ErrorMessage` 65.536 karakterle sınırlanır |
| K-441 | Gömülebilir bileşen yalnız akışsız (`Idempotency-Key`) istek kullanır; SSE ayrıştırıcı taşımaz |
| K-442 | Gömülebilir bileşen `wwwroot/embed/` altına ayrı bir Vite girişiyle derlenir ve VAR OLAN genel varlık sunum mekanizmasıyla, sıfır yeni C# `endpoint` koduyla sunulur |
| K-443 | `FallbackChatClient` devre kesicinin DIŞINDA, `ContentFilterDetectingChatClient`'ın İÇİNDE durur (K-320'nin yerleştirme kuralının Faz 62'ye uygulanışı) |
| K-444 | `ModelFallback` yalnız `Provider`+`Model` taşır; birincil bağlamanın diğer alanları (sıcaklık, `ProviderSettings`, ...) yedeğe DEVRETMEZ |
| K-445 | Sağlayıcı başına giden eşzamanlılık sınırı DOLDUĞUNDA isteği REDDETMEZ, kendi `CancellationToken`'ıyla sınırlı olarak BEKLER |
| K-446 | Ön uçuş reddi `400 Bad Request` döner, `413` DEĞİL |
| K-447 | `ModelFallbackUsed` olayı HEM `run_events`'e HEM kök span'in `tracon.model.id` etiketine yazılır |
| K-448 | Ön uçuş token sayımı için `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.O200kBase` AÇIK `PackageReference` ile eklendi (Faz 62 Açık Soru 1, seçenek A); `Microsoft.Bcl.Memory` CVE zorlamasıyla sabitlendi |
| K-449 | Yedek zincirinde retryable OLMAYAN bir hata (401/403, iptal) HANGİ HALKADA olursa olsun ANINDA ve SARMALANMADAN fırlatılır; zincir yalnız TÜM halkalar retryable hatayla tükendiğinde `TraconProviderUnavailableException` ile "ilk hata" özetine sarılır |
| K-450 | `FallbackRetryClassifier.IsRetryable` istisnanın TAMAMINI (`InnerException` zinciri + her `AggregateException` kolu) gezer; yalnız en dıştaki istisnaya bakmaz |
| K-451 | Argüman-koşulu onay kuralı `POST /api/approvals/rules` YENİ bir uçtur; Faz 63 planı bunu yanlışlıkla "mevcut uç" sanıyordu (Faz 63, plandan sapma) |
| K-452 | `POST /api/approvals/rules` gövdesi `ArgumentsHash` alanı TAŞIMAZ; yalnız `toolName`/`agentName`/`argumentConditions` |
| K-453 | Plandaki `ToolApprovalDecision` adı `ToolApprovalPolicyDecision` olarak gerçekleşti (Faz 63, plandan sapma) |
| K-454 | `POST /api/approvals/rules`'ta "aynı kapsam + aynı koşul" çakışması, depoyu değiştirmeden ÜRETİLEN id ile DÖNEN id'yi karşılaştırarak tespit edilir |
| K-455 | `conditions_hash` kanonikleştirmesi sayısal normalizasyon YAPMAZ (`100` ile `100.0` farklı hash üretebilir); sıralama + `JsonElement.GetRawText()` yeterli sayıldı |
| K-456 | Denetim izi (`audit_log`) veri konusu silmesinin kapsamı dışındadır; silme yalnız İÇERİK verisinde (oturum, çalıştırma, konuşma, ek, puan, ses, çalıştırma girdisi) uygulanır (Faz 64, kullanıcı kararı) 👤 |
| K-457 | `DataSubjectScope`'a plandaki taslağın öngörmediği üçüncü bir alan (`ConversationIds`) eklendi (Faz 64, plandan sapma) |
| K-458 | `DocumentEmbeddings` (bilgi tabanı gömüleri) veri konusu silme/dışa aktarım kapsamının DIŞINDA bırakıldı (Faz 64, plandan sapma) |
| K-459 | Denetim zinciri "tenant'ın son yazılan satırı" sorgusu `ORDER BY created_at, id` YERİNE ayrı bir sıra sütunuyla (`chain_seq`: PostgreSQL `GENERATED ... AS IDENTITY`, SQL Server `SEQUENCE` + `DEFAULT NEXT VALUE FOR`, SQLite yerleşik `rowid`) bulunur |
| K-460 | `audit_log.before`/`after` PostgreSQL'de `jsonb`'den `json`'a değiştirildi (Faz 64, K-027'nin beşinci uygulaması) |
| K-461 | Denetim zinciri yazımında eşzamanlılık, oturum/advisory kilit YERİNE benzersiz dizin + yeniden deneme + rastgele gecikme (jitter) ile çözülür |
| K-462 | Veri konusu önizleme/silme AYNI SQL işlemi (transaction) üzerinden yürür: önizleme her zaman geri alınır (`ROLLBACK`), gerçek silme yalnız çağıranın denetim yazımı (`beforeCommitAsync`) başarılı olursa `COMMIT` edilir (Faz 64, K-370 emsali) |
| K-463 | `SessionConversationResolver` (Core) sıradan bir `AgentSession`'ın dahili sohbet geçmişini veri konusu kapsamına ekler; `DataSubjectScope.SessionIds` tek başına bunun için YETERSİZDİR (Faz 64, bağımsız denetim 🔴 #1) |
| K-464 | `DataSubjectTargetRegistry`'deki her `ArrayContains` çağrısı sütunu TAM NİTELİKLİ (`{table}.column`) verir, bare ad değil (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur) |
| K-465 | `SqliteDialect.AddUuidArray` Guid'leri BÜYÜK harf metin olarak yazar (`System.Text.Json`'ın varsayılan küçük harf biçimi DEĞİL) (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur) |
| K-466 | Kiracı kimlik bilgisi/egress çözümlemesi ASYNC bir ikinci yol olarak eklendi; mevcut SENKRON `AgentDefinitionCompiler.Compile`/`ModelProviderRegistry.CreateChatClient`/`CompiledAgentCache.GetOrAdd` üçlüsü DEĞİŞTİRİLMEDİ (Faz 65) |
| K-467 | Kiracı egress politikası kontrolü, kimlik bilgisi çözümlemesinden ÖNCE ve TEK bir noktada (`ModelProviderRegistry.CreateChatClientAsync`) çalışır — bu nokta hem gerçek `run` derlemesini hem `AgentDefinitionValidator`'ın ön-uçuş kontrolünü kapsar (Faz 65) |
| K-468 | Dört sağlayıcı paketinin (`OpenAI`/`Anthropic`/`Google`/`Azure`) kiracı-kimlik-bilgisi başına istemci önbelleği TEK bir paylaşılan `Tracon.Core.ProviderCredentialClientCache<TFactory>` sınıfıyla yapılır; dört ayrı kopya YAZILMADI (Faz 65) |
| K-469 | Kiracı sağlayıcı bağlama/egress uçları (`/api/tenants/{tenantId}/providers`, `/api/tenants/{tenantId}/egress`) kiracıyı AMBIYANS `ITenantContext`'ten değil, ROTA parametresinden alır — `ApiKeyEndpoints`'in aksine, `GovernanceEndpoints`'in `PUT /api/tenants/{slug}` deseniyle AYNI (Faz 65) |
| K-470 | `FallbackChatClient` bir fallback'i tetiklediğinde kiracı/egress'ten HABERSİZ senkron `CreateChatClient` metot grubunu değil, `ModelProviderRegistry.CreateChatClientAsync`'i çağırır (Faz 65, bağımsız denetim 🔴 #1) |
| K-471 | `CompiledAgentCache`, kiracı credential'ı gömülü bir `AIAgent`'ı credential'dan HABERSİZ bir anahtarla asla saklamaz; bağlama varken üç kaynak (`DefinitionStoreAgentSource`, `CodeAgentSource`) önbelleği TAMAMEN atlar (Faz 65, bağımsız denetim 🔴 #2) |
| K-472 | `InboundTriggerDispatcher`, "tetikleyici yok" ile "imza yanlış"ı TEK bir `InboundTriggerOutcome.Unauthorized`'a (HTTP `401`) birleştirir; ayrı bir `NotFound`/`404` yolu YOKTUR (Faz 66, bağımsız denetim 🔴 #1) |
| K-473 | `InboundTriggerDispatcher.ValidateAsync`, imza doğrulamasını hız sınırından ÖNCE çalıştırır (Faz 66, bağımsız denetim 🔴 #2) |
| K-474 | `InboundTriggerDispatcher.EnqueueAsync`, hedef agent/workflow'un GERÇEKTEN var olup olmadığını kabul anında DOĞRULAMAZ; kontrolü işleyiciye (`AgentRunJobHandler`/`WorkflowJobHandler`) bırakır (Faz 66, bağımsız denetim ile onaylandı) |
| K-475 | Migration ledger'ın `set_name`/`(set_name, id)` şema yükseltmesi numaralı bir migration DOSYASI değil, `SqlDialect.UpgradeMigrationsTableAsync` bootstrap adımıdır (Faz 67, plandan sapma) |
| K-476 | `IVectorSearchStore`, `EnableKnowledge = false` iken KAYITSIZ bırakılmaz; fabrikası `null` döner (Faz 67) |
| K-477 | `SqlPersistenceDiagnosticsSnapshot.PendingMigrations`'a yeni alan eklenmedi; çekirdek-dışı bir setin bekleyen adı `"{set}:{ad}"` önekiyle yazılır (Faz 67, açık soru 2'nin kapanışı) |
| K-478 | Çalıştırma kimliği `IRunAttributionContext`'ten gelir; istek GÖVDESİNDEN asla alınmaz (Faz 68) |
| K-479 | Etiketler ayrı tabloya değil `runs.labels` JSON sütununa yazılır (Faz 68, açık soru 1, kullanıcı kararı) 👤 |
| K-480 | Sınır aşımı KIRPILMAZ, REDDEDİLİR; gürültülü sınır HTTP'de (`400`), sessiz düşürme kayıt yolundadır (Faz 68) |
| K-481 | Kullanıcı kimliği ve etiketler METRİK etiketi OLMAZ; yalnız sorgu boyutudur (Faz 68) |
| K-482 | Token kırılımı toplamların İÇİNDE sayılır; bildirilmeyen sayaç `null` kalır, `0` OLMAZ (Faz 68) |
| K-483 | Cache fiyatı ÇIKARMALI hesaplanır ve HER maliyet toplamının ÜÇÜNCÜ terimidir; tanımsız oran `Unknown`'a DÜŞÜRMEZ (Faz 68, açık soru 2/3, kullanıcı kararı) 👤 |
| K-484 | `UsageDetails`'in ses sayaçları için `MEAI001` bastırması TEK dosyada (`UsageBreakdown`) toplandı (Faz 68) |
| K-485 | `/api/stats`'a `groupBy` EKLENMEDİ; `byUser`/`byLabel` her zaman döner (Faz 68, plandan sapma) |
| K-486 | Attribution UPSERT'te `COALESCE` ile KORUNUR, üzerine yazılmaz (Faz 68) |
| K-487 | Tool sarmalama sırası Authorizing (dış) → Timeout → ApprovalRequired (iç) → gerçek fonksiyon; MCP yolunda AYNI mantık TEKRARLANIR (Faz 69) |
| K-488 | Yetki reddi İSTİSNA fırlatmaz, normal sonuç döner; kanca hata fırlatırsa fail-closed (Faz 69) |
| K-489 | `RunErrorClass.ToolTimeout` yeni değer (13); `TraconToolTimeoutException` stabil kimliği `tool_timeout` (Faz 69) |
| K-490 | MAF `FunctionInvokingChatClient`'ın onay kısa devresi `AITool.GetService<T>()` pipeline'ı üzerinden çalışır; `ApprovalRequiredAIFunction.InvokeCoreAsync`'i DOĞRUDAN çağırmak defer ETMEZ, gerçek gövdeyi çalıştırır (ÖLÇÜLDÜ, Faz 69) |
| K-491 | Tool yürütme varsayılan timeout'u 30 saniye; ÖLÇÜLMEDİ, ilk gerçek koşumdan sonra gözden geçirilecek (Faz 69, F-114, açık soru 5) |
| K-492 | `RunEventType.ReasoningDelta` değeri 23, plandaki 22 DEĞİL (Faz 70, F-115) |
| K-493 | `IRunEventSink` fan-out'u depo başarısından TAM bağımsız; `RunEventWriter.CompleteAsync` artık `IsDisabled` iken erken dönmez (Faz 70, F-115, Açık Sorular 1/2/4) |
| K-494 | Fonksiyon düğümü yalnız `Sequential`'da desteklenir; `WorkflowDefinition.Nodes` `AgentNames` ile karşılıklı dışlanır (Faz 71, F-116) |
| K-495 | Karışık zincirde agent düğümü `AIAgentBinding` değil `WorkflowAgentStepExecutor` (bir `FunctionExecutor` alt sınıfı) olarak bağlanır (Faz 71, F-116) |
| K-496 | Fonksiyon adı kaydetme anında da doğrulanır (agent adının aksine, yalnız derleme anında); kayıt süreç ömrü boyunca sabittir (Faz 71, F-116) |
| K-497 | Kod düğümünün kendi zaman aşımı bu fazda ele alınmadı; Faz 69'un tool timeout sözleşmesi tek aday olarak bırakıldı (Faz 71, F-116, Açık Soru 2, ÇÖZÜLMEDİ) |
| K-498 | Kontrol noktasından devam sözleşmesi ÖLÇÜLDÜ: EN SON kontrol noktasından sürdürme kod düğümünü YENİDEN ÇAĞIRMAZ, DAHA ERKEN bir kontrol noktasından sürdürme ÇAĞIRIR — `AddWorkflowFunction` işleyicisi bu yüzden İDEMPOTENT olmak ZORUNDADIR (Faz 71, F-116, planın "en riskli hata modu") |
| K-499 | Talimat sözlüğü için migration YAZILMADI (Faz 72, F-117); plan yanlıştı |
| K-500 | Kültür sürümün İÇİNDEDİR; dil başına ayrı sürüm hattı açılmadı (Faz 72, F-117, plan Açık Soru 1, Seçenek A, kullanıcı kararı) 👤 |
| K-501 | `SpeechAlignment` KARAKTER bazlıdır, kelime bazlı DEĞİL (Faz 72, F-118) |
| K-502 | Akışlı sentez + `IncludeTimestamps` kombinasyonu istisna ile REDDEDİLİR, sessizce yok sayılmaz (Faz 72, F-118, K1) |
| K-503 | Alt agent çağrıları ebeveynin `culture`'ını MİRAS ALMAZ (Faz 72, F-117, denetimde bulundu) |
| K-504 | `speak` tool şeması `includeTimestamps` ALMAZ; yalnız `POST /api/voice/speak` (operatör HTTP yolu) destekler (Faz 72, F-118, plan sapması) |
| K-505 | Yetenek haritası `capabilities.md`'den ÜRETİLİR, commit edilir ve `dotnet pack` onu okur; Node zinciri `dotnet build`'e bağlanmaz (Faz 73, F-120, plan Açık Soru 4) |
| K-506 | `Tracon.Usage` tanıları `Warning`'dir; `Info` `dotnet build` çıktısına DÜŞMEZ (Faz 73, F-120, kullanıcı kararı, ölçümle) 👤 |
| K-507 | Harita sürüm işareti İÇERİK REVİZYONUDUR, paket sürümü değil (Faz 73, F-120, plan sapması) |
| K-508 | `TRC0302` sarmalayıcıyı değil, DEKORATÖRSÜZ VE FABRİKASIZ sarmalayıcıyı bildirir (Faz 73, F-120, plan Açık Soru 3) |
| K-509 | `CapabilityCoverageTests` kapsamı TÜM kayıt giriş noktalarıdır, yalnız `Use*`/`Map*` değil (Faz 73, F-120, kullanıcı kararı) 👤 |
| K-510 | Yerel referans dosyası PROJE başına yazılır, depo köküne değil (Faz 74, F-121, denetim bulgusu 3) |
| K-511 | `docs/openapi/tracon.json` `Tracon.AspNetCore` paketine KAYNAĞINDAN girer; K-039 yeniden açılmaz (Faz 74, F-121) |
| K-512 | `CapabilityExampleTests` DÖRT iddia taşır; yabancı üye listesi yalnız Microsoft üyelerini içerir (Faz 74, F-121) |
| K-513 | Bir `<example>`'ın doğruluğunu yalnız DERLEME kanıtlar; metin denetimi yapısal olarak yetersizdir (Faz 74, F-121, denetim bulgusu 4) |
| K-514 | Sevk edilen dokümantasyon KENDİ KENDİNE YETER: pakete giren bir metin yalnız tüketicinin elindeki şeylere gönderme yapar (Faz 75, F-126) 👤 |
| K-515 | Sevk edilen dokümanı denetleyen cırcır, satırı değil BLOĞU okur (Faz 75) |
| K-516 | Site üreteçleri artık ONARMAZ, HATA VERİR (Faz 75, F-126) |
| K-517 | `<see cref>` paketlenen OpenAPI belgesinde TAM İMZA olarak render edilir; sözleşme tiplerinde `<c>ÜyeAdı</c>` yazılır (Faz 75) |
| K-518 | Kök `README.md` İngilizce'dir (Faz 75) 👤 |
| K-519 | Doküman sitesinin rengi KAPALI bir token kümesidir ve kontrast derleme anında hesaplanır (Faz 76) |
| K-520 | Figürler (ekran görüntüsü ve diyagram) temadan BAĞIMSIZDIR; iki temada da açık plaka üzerinde durur (Faz 76) |
| K-521 | Kenar çubuğu `src/sidebar.mjs`'te tek kaynaktır; üç tüketici onu okur (Faz 76) |
| K-522 | Tüketici doküman standardı faz dokümanından ayrıştırılıp `tuketici-dokuman-senkronu` skill'ine taşındı (kullanıcı kararı) 👤 |
| K-523 | Kapanmış faz dokümanları `docs/arsiv/fazlar/` altında yaşar; `docs/**.md` bütçesi büyütülmez (kullanıcı kararı) 👤🔁 |
| K-524 | `MIMARI.md` §7 "Güvenlik Modeli" kendi dosyasına ayrıldı |
| K-525 | Kiracı verisi tutan cache anahtarı TİPLİ olur; elle birleştirilen string anahtar yasaktır |
| K-526 | Denetim yükü `AuditPayload` ile kurulur; geri alınamaz eylem `AuditRecorder` kullanamaz |
| K-527 | Yürütülebilir yüzeyi genişleten ayar yapılandırmadan okunmaz |
| K-528 | `external:invoke` kapsamı istek anında zorlanır; anahtarın var olması yetmez |
| K-529 | Giden ağ hedefi TEK bir muhafızdan geçer; üç yüzey de kendi kopyasını taşımaz |
| K-530 | `Tracon:Egress:AllowPrivateNetworkTargets` varsayılanı KAPALI; üç yüzeyde de özel ağ reddedilir (kullanıcı kararı) 👤 |
| K-531 | Sağlayıcı istemcisine muhafız YALNIZ kiracı `Endpoint` override'ı varken takılır (kullanıcı kararı) 👤 |
| K-532 | Sağlayıcı SDK'larının muhafız kancası ÖLÇÜLDÜ; plan tahmini üçte ikisinde yanlıştı |
| K-533 | `secret` çözen HER yapılandırma anahtarı bir önek allow-list'ine bağlıdır |
| K-534 | MCP önek ayarı `Abstractions`'ta yaşar (`TraconMcpSecurityOptions`), `Tracon.Mcp`'de değil |
| K-535 | Webhook ek başlıkları Tracon'in kendi başlık adlarını taşıyamaz |
| K-536 | Giden ağ muhafızı ORTAM PROXY'sini kullanmaz (`UseProxy = false`) |
| K-537 | `llms.txt` yetenek haritasından AYRI bütçelenir: 10 240 B ve 20 480 B (Faz 78, F-135, ölçümle) |
| K-538 | `TRC0402` yalnız yerel referans dosyası GERÇEKTEN yazılırken öter (Faz 78, F-135, denetim 🔴 #1, kullanıcı kararı) 👤 |
| K-539 | Karar defterinin §2 tablosu YAPISAL olarak denetlenir: yinelenen numara · tabloyu kesen boş satır · sıra dışı numara |
| K-540 | Migration çakışmasının İKİ şekli vardır ve ikisi de geçicidir: unique ihlali VE deadlock |
| K-541 | Terminal `AwaitingApproval` durumu, ona bağlı kayıtlar GÖRÜNÜR olduktan sonra yayınlanır; sıra `TraconRunOptions.BeforePendingApprovalIsPublished` kancasıyla zorlanır |
| K-542 | Ürün dokümantasyonu `tracon.dev` alt alan adında kendi sunucumuzda yayınlanır; `base` KALICI olarak `/` oldu ve adres tek dosyada (`docs-site/site.config.mjs`) bildirilir 👤 |
| K-543 | Bağımlılık sürüklenmesini Dependabot fark eder; beş sabit gerekçeli `ignore` listesindedir 👤 |
| K-544 | MEAI 10.9.0, `Microsoft.Extensions.*` tabanını 10.0.11'e ZORLAR; "yalnız MEAI'yi yükselt" diye bir seçenek yoktur |
| K-545 | Geçici çakışma yeniden denemesi migration'ın BOOTSTRAP deyimlerini de kapsar; "bu yalnızca kurulum" muafiyeti yoktur |
| K-546 | `TraconToolAttribute.cs`'in `OrderTools` örneği artık `static` DEĞİL; tool metodu yine `static` |
| K-547 | `Azure.Identity` yalnızca test projesine (`Tracon.Generators.UnitTests`) `PackageReference` olarak eklendi; `Tracon.Azure`'un bağımlılık grafiği DEĞİŞMEDİ |
| K-548 | `SITE_KURALLARI` kural başına eşleşir; `--site-denetle` `git` hatasında artık çıkış kodu 1 verir (Faz 80, kullanıcı kararı) 👤 |
| K-549 | `kirik_baglantilar()` site-mutlak (`/...`) bağlantıları slug haritasıyla çözer; üretilen `api/`, `http-api/` ve `openapi/` hem kaynak hem hedef olarak hariç (Faz 80) |
| K-550 | Doküman bakım testleri `scripts/dokuman_bakim_test.py`dır (ALT ÇİZGİ); planın önerdiği `dokuman-bakim_test.py` (TİRE) DEĞİL (Faz 80, plandan sapma) |
| K-551 | Yanıt önbelleği anahtarı `record struct CacheKeyScope` (taban anahtar, kiracı, sağlayıcı, sıralanmış tool adları) üzerinden hesaplanır; MEAI'nin varsayılan `GetCacheKey`'ine güvenilmez (Faz 81, F-45, ölçümle) |
| K-552 | Yanıt önbelleği halkası tool çağrı döngüsünün İÇİNDE, telemetrinin VE içerik güvencesinin DIŞINDA durur (Faz 81, F-45, kullanıcı kararı) 👤 |
| K-553 | `AllowConcurrentToolCalls`, `ModelProviderRegistry`'nin kurduğu `FunctionInvokingChatClient` örneğine bağlanır; MEAI 1.18.0'ın getirdiği `ChatClientAgentOptions.AllowConcurrentInvocation`'a DEĞİL (Faz 81, F-134) |
| K-554 | `TraconResponseCachingChatClient` `internal sealed`dır; planın taslağı `public` diyordu (Faz 81, plandan sapma) |
| K-555 | Yanıt önbelleğinin okuma/yazma hatası `run`'ı DURDURMAZ; loglanır ve isabet/ıska sessizce devam eder (Faz 81, plan metninde açık değildi, repo-geneli ilkeden türetildi) |
| K-556 | `POST /api/agents/validate`'de eksik `IDistributedCache` `invalid_setting`/`model.providerSettings` olarak görünür; planın beklediği `compilation_error` DEĞİL (Faz 81, plandan sapma, ölçümle) |
| K-557 | 🚨 Önbellek isabeti hem `ChatResponse.Usage`'ı hem her mesajın `UsageContent`'ini SIYIRIR; aksi hâlde bir `run` kaydı hiç faturalanmamış token'ı ikinci kez sayar (Faz 81, gerçek `samples/Tracon.Api` koşumunda ölçülen kusur) |
| K-558 | `SqlConversationBranchStore` içerik korumasına dokunmaz: dallanma `conversation_items.item`'i BAYT BAYT kopyalar, zarf DOKUNULMADAN kalır (Faz 82, plandan sapma) |
| K-559 | `IContentProtector` varsayılanı `NullContentProtector`, `AddTracon()` içinde `TryAddSingleton` ile kaydedilir; `AddContentProtection(...)` `Replace` ile değiştirir (Faz 82, `IAuditLog`/`InMemoryAuditLog` deseninin aynısı) |
| K-560 | `SqlStoreContext.ContentProtector` `IContentProtector?` (nullable); `NullContentProtector.Instance`'a varsayılan DEĞERİ YOKTUR (Faz 82) |
| K-561 | `TraconContentProtectionOptions.Keys` bir kid'i anahtarın KENDİSİNE değil, yapılandırma anahtarının ADINA eşler (K-059 örüntüsünün tekrarı, Faz 82) |
| K-562 | Zarf biçimi metin sütunları için JSON (`$apEnc`/`kid`/`n`/`c`), ikili sütun için sabit başlıklı biçim (`APEB` + sürüm + kid uzunluğu + kid + nonce + şifreli+etiket) — tek yerde tanımlı (Faz 82) |
| K-563 | `TraconContentProtectionOptionsValidator` yalnız YAPISAL doğrulama yapar (`ActiveKeyId` set ve `Keys`'te var); anahtarın gerçek değerini `IConfiguration` üzerinden ÇÖZMEZ (Faz 82) |
| K-564 | `Tracon.Client` NSwag ile üretilir; üretilen kod repoya commit edilir ve davranışsal bir kapıyla (`ClientCoverageTests`) izlenir, byte-diff kapısı DEĞİL (Faz 83, kullanıcı kararı) 👤 |
| K-565 | `Tracon.Client` `Tracon.Abstractions`'ı REFERANSLAMAZ; DTO'lar OpenAPI belgesinden üretilir, sunucu tipleriyle aynı şekle sahip ama FARKLI CLR tipidir (Faz 83) |
| K-566 | `Tracon.Client` ve `Tracon.Cli` public API takibinin (`PublicApiAnalyzers`) DIŞINDADIR (Faz 83, Açık Soru 2 karar A) |
| K-567 | `Tracon.Client`'ın AOT sözünden VAZGEÇİLDİ: `TraconAotCompatible=false` (Faz 83, ölçülen risk gerçekleşti) |
| K-568 | `IMigrationApplier` arayüzü `Tracon.Abstractions`'a eklendi: linked-source `MigrationRunner` tipinin üç sağlayıcı derlemesinde belirsiz olması sorununu çözer (Faz 83) |
| K-569 | `AddTraconClient` `IServiceCollection` döner, `IHttpClientBuilder` DEĞİL; `IHttpClientFactory` kullanılmaz (Faz 83, plandan sapma) |
| K-570 | Üretim öncesi OpenAPI belgesinin bir kopyasında her şema `additionalProperties: false` ile kapatılır (Faz 83, ölçülen kusur) |
| K-571 | Enum'lar için `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` TİP DÜZEYİNDE her `enum` bildirimine eklenir; `JsonSourceGenerationOptions.Converters` listesi KULLANILMAZ (Faz 83, ölçülen STJ davranışı) |
| K-572 | `tracon health` `/api/models/health`'i okur; `MapTracon` içinde jenerik bir `/health` ucu YOKTUR (Faz 83) |
| K-573 | OpenAPI belgesi VARSAYILAN KAPALI uçları da tarif eder; `GET /api/diagnostics` belgeye girdi ama davranışı değişmedi (Faz 84, §84.3) |
| K-574 | `packages/` kök dizini dil sınırı kapısının (`SourceLanguageTests`) kapsamına girdi; `PackagedReadmePattern` regex'i `src/`'nin yanına `packages/`'ı da aldı (Faz 84, §84.7, denetim 🟡 bulgusu) |
| K-575 | `src/Tracon.UI/frontend/package.json`, `@tracon/client`'a `file:../../../packages/tracon-client` ile bağlanır; npm registry'den kurulmayı BEKLEMEZ (Faz 84, §84.8) |
| K-576 | npm yayın işi (`ci.yml`) NuGet'in `--skip-duplicate`'ine karşılık `npm view` ile elle idempotency kontrolü yapar; ikisi de AYNI `v*` git tag'inden türer, ayrı bir npm sürüm şeması YOKTUR (Faz 84, §84.11) |
| K-577 | Konsol göçü TAM göçtür; üretilen istemcinin üzerine adlandırılmış bir cephe (facade) katmanı EKLENMEDİ (Faz 84, planın kendi kaçış merdiveni kullanılmadı) |
| K-578 | OpenAPI üretecinin iki sistemik kusuru (eksik `required`, `number\|string` karışımı) VE paylaşılan-şema nullable sızıntısı frontend'de `Fix<T,K>` tek yardımcı tipiyle düzeltilir; sunucu şeması DEĞİŞTİRİLMEZ (Faz 84, §84.6, Açık Soru 4 karar A) |
| K-579 | `Microsoft.AspNetCore.Mvc.Testing` yalnız GERÇEK giriş noktalı örnek uygulamaları test eden projelerde kullanılır; kütüphane testleri `TestHost` kalır (Faz 85) |
| K-580 | SQL-tabanlı `jsonb`/`json` yükü taşıyan her ARA tip (`AgentDefinitionPayload` gibi), kaynak tipe (`AgentDefinition`) yeni alan eklendiğinde ELLE senkronize edilmelidir; derleyici bunu zorlamaz (Faz 86, ölçülen kusur) |
| K-581 | `CompiledAgentCache`'i atlayan bir çağıran, `IAgentCatalog.ResolveAsync`'in UYGULADIĞI `IAgentDecorator` zincirini de ELLE uygulamak zorundadır; yeni `AgentDecoratorPipeline.Apply` bunu tek bir yerde toplar (Faz 86, ölçülen kusur) |
| K-582 | `TraconOptions.MaxParameterValueLength` tek, üst-düzey bir sınırdır; parametre başına ayrı bir sınır YOKTUR (Faz 86, Açık Soru 2 kapatıldı, öneri A) |
| K-583 | Kesilen bir işi sürdürmek üçüncü, ayrı bir işlemdir (`JobKind.RunContinuation`, `runs.continued_from_run_id`); `Replay` (K-315) ve `ApprovalResume` (K-368) ile KARIŞTIRILMAZ (Faz 87, kullanıcı kararı) 👤 |
| K-584 | `Destructive`/`External` etkili bir tool taşıyan koşu varsayılan olarak devam ETMEZ; gevşetme bir AYARLA değil, tool'un kendi `SafeToRepeat` bildirimiyle yapılır (Faz 87, kullanıcı kararı) 👤 |
| K-585 | Devam koşusunun eşleşmeme politikası `RecordedToolPlayback`'in (internal) kurucusuna bir `ToolPlaybackMismatchPolicy` parametresi olarak eklendi; `ReplayToolMode`'a dördüncü bir üye veya ikinci bir sarmalayıcı tipi AÇILMADI (Faz 87, Açık Soru 1, öneri A) |
| K-586 | Skill veya çağrılabilir alt-agent kullanan bir agent DEVAM ETTİRİLEMEZ; bu Faz 87'nin planında YOKTU, uygulama sırasında ölçülüp keşfedilen bir sınırdır |
| K-587 | Workflow düğüm retry'ı yalnız `AddWorkflowFunction` ile kaydedilen fonksiyon düğümlerini kapsar; retry döngüsü düğümün KENDİ çağrısının içinde kalır ve `MaxSuperSteps` sayacını ETKİLEMEZ — ÖLÇÜLDÜ (Faz 87, Açık Soru 2 kapatıldı, öneri B) |
| K-588 | MEAI görsel üretim yüzeyi deneysel kaldığı sürece `MEAI001` bastırması yalnız görsel kayıt ve çağrı sınırlarında DAR tutulur (Faz 88, plan sapması) |
| K-589 | Birden çok image provider kaydı, `TraconImageOptions.Provider` adına göre KEYED çözülür; isimsiz kayıt yalnız özel consumer fallback'idir (Faz 88, denetim öncesi tasarım bulgusu) |
| K-590 | Görsel ek deposu yalnız doğrulanabilir baytı kalıcılaştırır; `HostedFileContent` fail-closed reddedilir (Faz 88, plan sapması) |
| K-591 | Google image adapter, `WIDTHxHEIGHT` isteklerini tahminden çevirmek yerine REDDEDER (Faz 88, plan sapması) |
| K-592 | Tool çıktısı boyut sınırı UTF-8 bayt biriminde ölçülür; token veya karakter DEĞİL (Faz 89, kullanıcı kararı) 👤 |
| K-593 | Kırpılan tool çıktısı `{"truncated","omittedBytes","content"}` alanlı bir JSON zarfına sarılır; zarf `Utf8JsonWriter` + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` ile üretilir (Faz 89, kullanıcı kararı + plan sapması) 👤 |
| K-594 | `TruncatingAIFunction` yalnız `string` ve `JsonElement` sonuçları ölçer/kırpar; başka bir ham CLR nesnesi HİÇ dokunulmadan geçer (Faz 89, bağımsız denetim bulgusu, plan Açık Soru 1 kapatıldı) 🔁 |
| K-595 | `TruncatingAIFunction`, zarfın asla sınırı AŞMAYACAĞINI kurucuda GARANTİ eder: `maxOutputBytes`, `MinimumEnvelopeBytes`'ın (bugün 57 B, `omittedBytes` için `int.MaxValue` worst-case ile hesaplanır) altındaysa `ArgumentOutOfRangeException` atar (Faz 89, bağımsız denetim bulgusu) |
| K-596 | `McpResourceTrimming` `Tracon.Mcp`'den `Tracon.Core`'a `TextTrimming` adıyla TAŞINDI (kopyalanmadı) ve `public` yapıldı (Faz 89, plan) |
| K-597 | Arşivlemek TAŞIMAK değil DAMITMAKTIR: kapanan fazın dokümanı tam metniyle değil, sabit boyutlu bir kayıtla arşivlenir (Faz 90) (kullanıcı kararı) 👤 |
| K-598 | Tam metin git geçmişinde yaşar ve çözülebilirliği HER denetimde kanıtlanır; `git log --follow` sözleşme DEĞİLDİR (Faz 90) |
| K-599 | `HARIC` muafiyeti KORUNUR; muaf tutulan her ağaç KENDİ bütçesini alır (Faz 90) (kullanıcı kararı) 👤 |
| K-600 | `KARARLAR.md` satırı 450 bayta indirilir; kesilen gerekçe `KARARLAR-GECMISI.md`'ye ÖNCE taşınır, satır SONRA kısaltılır (Faz 90) |
| K-601 | Public yüzey erişilebilirlik ölçütüyle daraltıldı: yaprak olup başka public imzada geçmeyen 96 tip `internal` yapıldı (Faz 96) |
| K-602 | Tek sürüm hattı: paketlenen projelerin hepsi `1.0.0-preview.N` olarak çıkar (Faz 97) (kullanıcı kararı) 👤 |
| K-603 | `PublicAPI.Shipped.txt` preview hattı boyunca boş kalır; 618 tipin `Unshipped` → `Shipped` dolumu `1.0.0` GA'ya ertelendi (Faz 97) (kullanıcı kararı) 👤 |
| K-604 | Yayın işleri (`publish`, `npm-publish`) yayın provası kapısına (`release-dryrun`) bağlandı; kapı her push'ta (PR dahil) koşar (Faz 97) |
| K-605 | Yeni paket `Tracon.Testing.Contracts.Xunit`: `IRunStore` ve 32 diğer store sözleşmesi xunit.v3 test taban sınıfı olarak sevk edilir; ad alanı `Tracon.Testing.Contracts.Storage` (Faz 98) (kullanıcı kararı — paket adı ve ad alanı) 👤 |
| K-606 | `ApiKeyGenerator`/`GeneratedApiKey` `Tracon.Core`'dan `Tracon.Abstractions`'a taşındı (Faz 98) |
| K-607 | `IRunStore.AppendEventAsync` yinelenen `Sequence`'i REDDEDER (dört implementasyonda: bellek içi + üç SQL sağlayıcı) (Faz 98) (kullanıcı kararı) 👤 |
| K-608 | `StartRunAsync`'in dönüş değeri dört sağlayıcıda da COALESCE uygulanmış kaydı taşır (Faz 98) |
| K-609 | Tracon, `IModelProvider.CreateChatClient`'ın döndürdüğü `IChatClient`'ı HİÇBİR ZAMAN dispose etmez; ömür sağlayıcınındır (Faz 99) (kullanıcı kararı) 👤 |
| K-610 | `ContractCoverage`'ın her çağrısı bir sözleşme AİLESİ adı alır; aile adı almayan aşırı yükleme YOKTUR (Faz 99) |
| K-611 | Sözleşme suite'inde isteğe bağlı davranış ATLANAN senaryo değil, AYRI bir opt-in sınıftır (Faz 99) |
| K-612 | Uygulanmış migration baytları değiştirilebilir checksum manifestine değil Git kaynak commit'lerine sabitlenir (Faz 99, F-151) |
| K-613 | Yargıç model istemcisi ayrı `CreateSetupChatClientAsync` üyesiyle kurulur (Faz 100) |
| K-614 | `IToolRegistry` public kalır; tanınmayan implementation `IVerifiedToolRegistry` marker'ı taşımadığı için startup'ta reddedilir; `AllowUnverifiedToolRegistry` bilerek yapan için opt-out'tur (Faz 102) |
| K-615 | Generator kendi ürettiği `JsonSerializerContext`'i kullanmaz; complex tool sonucu için tool sahibi kendi `[JsonSerializable]` context'ini `TraconToolAttribute.JsonSerializerContext`'e verir, aksi hâlde derleme `TRC0008` ile durur (Faz 102) |
| K-616 | `CustomToolContract` yalnız implementer'ın TEK BAŞINA sağlayabileceği isim/metadata/eşzamanlı çağrı sözleşmesini taşır; tenant/timeout/envelope/guard davranışı registry-DI sınırını geçtiği için pakete Core bağımlılığı EKLENMEDEN Core fonksiyonel testlerinde ve sample run'ında kanıtlanır (Faz 102) |
| K-617 | Content guard'ın normalize edemediği tool sonucu için fail-closed yolu, guard PATTERN eşleşmesinden TAMAMEN bağımsız, koşulsuz bir değiştirme olarak yeniden yazıldı (Faz 102, bağımsız denetim bulgusu — kapanmadan kapatılan 🔴) |
| K-618 | BYOK iki ayrı public interface'e bölündü: `IModelProvider.CreateChatClient(binding)` yalnız setup credential, yeni `ITenantCredentialModelProvider.CreateChatClient(binding, credential)` yalnız tenant credential alır; capability yok ise registry provider'ı hiç çağırmadan `provider_credential_unsupported` ile fail-closed olur (Faz 103) (kullanıcı kararı) 👤 |
| K-619 | Provider construction hatası normalizasyonunda Tracon'in KENDİ validation hatası, internal ve tek başına inşa edilemez (`InternalsVisibleTo` ile korunan) bir işaretçi tipiyle "foreign" sayılmaktan çıkarıldı (Faz 103) |
| K-620 | `TraconJudgeException` kaldırıldı; stable judge error code'ları (`judge_failed`/`judge_timeout`/`judge_contract`) internal sabitlere taşındı; `ITraconBuilder.AddRunJudge` `AddAgentSource` deseniyle üç overload (generic/instance/factory) olarak eklendi (Faz 103) (kullanıcı kararı) 👤 |
| K-621 | `JudgeTimeout` cooperative cancellation değil GERÇEK wait cutoff'tur; token'ı yok sayan judge gövdesi timeout'ta öldürülmez, arkada tamamlanır ve geç sonuç sessizce atılır (skor/summary/metric yazmaz, unobserved exception üretmez) (Faz 103) (kullanıcı kararı) 👤 |
| K-622 | `kapi.py yayin` extension sample'larını (+ Native AOT smoke) izole `NUGET_PACKAGES` cache'i ve tek exact packed version ile doğrulayan `release_extension_samples.py`'a bağlandı; AOT publish restore+publish TEK komutta birleştirildi (Faz 103) |
| K-623 | Kiracı yalıtımı UYGULAMA KATMANINDA tek hat kalır; veritabanı RLS'i (Row Level Security) eklenmez (Faz 104) 👤 |
| K-624 | `Production` ortamında kalıcı olmayan store BAŞLANGIÇTA UYARIR; hata fırlatılmaz ve susturma seçeneği eklenmez (Faz 104) 👤 |
| K-625 | Üç SQL sağlayıcısına `Options.DataSource` alanı eklendi; Tracon kendi kurduğu `NpgsqlDataSource`/`SqlServerDataSource`/`SqliteDataSource`'u artık PUBLIC bir DI servisi olarak kaydetmez; `DataSource` ve `ConnectionString` birlikte verilirse başlangıç hatası (sessiz öncelik yok) (Faz 110) |
| K-626 | `runs_v1` okuma sözleşmesi görünümü üç sağlayıcıda `EnableReadViews` ile isteğe bağlı yayımlandı; yayımlanan sürüm sütun kaybetmez/yeniden adlandırmaz/daraltmaz, kırıcı değişiklik `runs_v2` olarak açılır; görünüm bir kiracı sınırı DEĞİLDİR (Faz 111) |
| K-627 | `RunErrorClass.BudgetExceeded` kaldırıldı; sayısal değer `9` KALICI OLARAK EMEKLİ, asla yeniden kullanılmaz 👤 |
| K-628 | İstemci taraflı tool (`AddClientTool`) taşıyan bir agent, `POST /replay` ile HİÇBİR `toolMode`'da (`ReplayTools`/`LiveTools`/`NoTools` üçü de) yeniden oynatılamaz; ret `RunReplayService.PrepareAsync` içinde, run hiç başlamadan, agent TANIMININ tool listesi taranarak verilir (Faz 112) 👤 |
| K-629 | Sağlayıcı retry kararı için `IProviderRetryClassifier` (üç durumlu: `Unknown`/`Retry`/`DoNotRetry`) yeni public seam olarak `Tracon.Abstractions/Providers/` altında açıldı; `DefaultRunErrorClassifier` `internal sealed partial` → `public sealed partial`; `ErrorFingerprint`'in iç hesabı internal kalıp yeni public `RunErrorFingerprint.Compute(string?)` facade'ı eklendi (Faz 113, F-149) |
| K-630 | Çalıştırma-içi bütçe kesmesi (`TraconRunBudgetExceededException`, `run_budget_exceeded`) yeni bir `RunErrorClass` üyesi AÇMADAN mevcut `QuotaExceeded` (`4`)'e eşlenir; `9` K-627'nin kararıyla kalıcı emekli kalır (Faz 114, F-166) 👤 |
| K-631 | `ModelProviderRegistry`'nin kurucusu `IRunPricingResolver?` yerine `IServiceProvider?` alır; `RunBudgetChatClient` fiyat çözümleyiciyi boru hattı KURULUM anında (`BuildPipeline` içinde) GEÇ (lazy) çözer (Faz 114, F-166) |
| K-632 | `AgentRunBudget`'ın maliyet sayacı `decimal` değil, `long` sabit noktalı nano-birim (`1/1_000_000_000`) + `Interlocked.Add`'tir; `RunRecordingAgent.CompleteAsync`'in run sonu `scope.Budget?.RecordUsage(...)` satırı KALDIRILDI — bütçe muhasebesinin %100'ünü artık `RunBudgetChatClient` taşır (Faz 114, F-166) |
| K-633 | `Tracon.Client`'ın üretilmiş `System.Text.Json.JsonElement`/`ChatRole`-tipli alanları geriye dönük UYUMSUZ biçimde düzeltildi (`scripts/nswag-postprocess-client.py`); `TraconPublicApiTrackingEnabled=false` olduğu için derleyici bunu işaretlemedi (Faz 115, eval CLI komutu sırasında bulunan önceden var olan kusur) |
| K-634 | Performans tahsis kapısı yalnız TAHSİS EDİLEN BAYTI karşılaştırır (sıfır tolerans), süreyi bilgi olarak kaydeder ama hiçbir şeyi kırmaz; beşinci bağımsız bir kapı DEĞİL, `kapi.py kapanis`'in üç sıcak yol dosyasından biri değiştiğinde koşturduğu koşullu bir adımdır (Faz 116, F-67) 👤 |
| K-635 | BenchmarkDotNet 0.15.8 yalnız `bench/Tracon.Benchmarks` (`IsPackable=false`) için eklendi; K-007'nin "yeni paket gerekçe ister" barı gerçek restore ile ölçülerek karşılandı (Faz 116, F-67) |
| K-636 | `ModelContextProtocol.Extensions.Tasks` 2.2.0 yalnız `Tracon.AspNetCore`'a eklendi; net yeni geçişli paket sıfırdır, `Tracon.Mcp` (istemci) bu paketi görmez (Faz 117, F-167) |
| K-637 | MCP task kimliği Tracon'in run kimliğidir; SDK'nın `IMcpTaskStore.CreateTaskAsync()`'i çağrının hangi agent/kiracı olduğunu bilmediği için bu eşleşme kayıt-öncesi bir `AsyncLocal` sağlayıcı filtresiyle (`McpTaskRunProvisioningFilter`) kurulur; arka plan yürütmesi kendi `AmbientTenantScope` sarmalını taşır; kiracı-oblivious SDK-içi `tasks/cancel` müdahalesi kabul edilen, kapatılamayan bir sınır olarak belgelenir (Faz 117, F-167) (kullanıcı kararı) 👤 |
| K-638 | Yargıç başına retry checkpoint'i yeni bir tablo/migration AÇMADAN mevcut `run_scores` satırlarından okur; bu, K-621'in "sonraki adım" sütununun sorduğu soruya (F-152 timeout modeline yeni bir katman ekler mi) HAYIR cevabıdır (Faz 118, F-152) (kullanıcı kararı) 👤 |
| K-639 | Kiracı sağlayıcı bağlantısında (`BYOK`) `provider` adı, karşılaştırıcı değiştirilerek değil DEĞER NORMALLEŞTİRİLEREK case duyarsız yapılır; kural `TenantProviderBinding.NormalizeProviderName` olarak public'tir ve store hem yazarken hem sorgularken uygular (Yayın denetimi, BL-006) |
| K-640 | Yabancı (Tracon dışı) bir exception'ın mesajı hiçbir zaman kalıcı alana veya dışa açık yanıta yazılmaz; kural `Tracon.SafeErrorText` olarak `Tracon.Abstractions`'ta public'tir (Faz 119, BL-027/BL-037) |
| K-641 | `IJobHandler`'ın at-least-once yürütme sözleşmesi (aynı job'ın süzülmemiş item listesiyle yeniden çağrılabileceği) `Tracon.Abstractions`'ın XML dokümanına yazılır ve bir contract testiyle (`JobHandlerContract`, `Tracon.Testing.Contracts.Xunit`) kilitlenir; `IIdempotencyStore`'u job dispatch loop'una bağlamak gereksiz ikinci bir mekanizma olacağı için REDDEDİLİR (Faz 120, BL-041) |
| K-642 | `IAgentDecorator.Order`'ın sayısal yönü DEĞİŞMEZ (düşük değer dışta sarar); yanlış olan XML dokümanıydı ve düzeltildi. Sıralama sözleşmesi taşıyan her public üyenin yönünü SÖZCÜKLE belirtmesi `OrderingContractDocumentationTests` ile kalıcı kapıya bağlandı (BL-034) |
| K-643 | Seam sözleşme standardının dört boyutu (DI lifetime, tenant mode, delivery guarantee, guarantee limit) iki ayrı mekanizmayla kilitlenir: ilk üçü `SeamContractDocumentationTests`'in küçülen taban çizgisiyle, dördüncüsü `OrderingContractDocumentationTests` deseniyle ayrı TheoryData satırlarıyla (Faz 121, BL-003 kulvar 3) |
| K-644 | `IAuditLog`'un null-tenant sözleşmesi (boşsa çağıranın AMBIENT tenant'ına düşer, "her kiracı" değil) `ITenantContext` enjeksiyonuyla GERÇEK davranışa dönüştürüldü — `InMemoryAuditLog` öncesinde tüm kiracıları tarıyordu, `SqlAuditLog` sessizce boş dönüyordu; ikisi de `SqlRunStore`'un deseniyle hizalandı (Faz 121, BL-046) |
| K-645 | 60 tekil-registrasyon seam'ine dedicated `Add*()`/`Use*()` metodu EKLENMEZ; `ITraconBuilder.Services`'in XML dokümanına bağlı bir metin kapısı sözleşmesi yeterli sayıldı (Faz 122, BL-008/BL-019 kulvar 2) |
| K-646 | Tenant credential'la üretilen `IChatClient`, dört sevk edilen adaptörün (Anthropic, Azure, Google, OpenAI) hepsinde (credential, model, `ProviderSettings`) başına önbelleğe alınır; paylaşılan anahtar `Tracon.Core.TenantChatClientCacheKey` olarak eklendi 👤 |
| K-647 | `RunEventType` üyesinin payload hakkındaki her iddiası, o payload'ı OKUYAN bir testle eşleşmek zorundadır (`RunEventPayloadContractTests`, yalnız küçülen `uncovered` taban çizgisi); `ModelFallbackUsed` payload'ına eklenen `reason` KAPALI bir küme taşır ve sağlayıcının hata metnini asla taşımaz (tüketici raporu doğrulaması) |
| K-648 | Oturumun İLK yazımı gibi SONRAKİ her yazımı da eşzamanlılık denetiminden geçer: `ISessionStore.TryUpdateAsync` + `sessions.version` (üç SQL sağlayıcıda migration); çakışma `TraconSessionConflictException` fırlatır ve retry ÇAĞIRANA düşer, Tracon içeride sessizce denemez 👤 |
| K-649 | Kalıcı payload sürüm sözleşmesi: `sessions.schema_version` yeniden adlandırılarak `state_schema_version` oldu (veri korunarak), yeni `state_maf_version` sütunu `sessions` ve `workflow_checkpoints`'e eklendi; damgalama/doğrulama sorumluluğu SQL store'dan `AgentSessionManager`'a taşındı 👤 |
| K-650 | `POST /api/stats/recalculate-costs` DARALDI: artık yalnız `PricingSource.Unknown` (veya hiç fiyatlanmamış) satırları fiyatlar; bilinen fiyatlı bir satırı bir daha asla yeniden yazmaz (Faz 132, F-175) |
| K-651 | Manuel test bütçesi ölçüme yeniden bağlanabilir; formül `ölçülen / (1 − BOSLUK_ORANI)`, karar kullanıcınındır 👤 |
| K-652 | Paylaşılan bir SQL sorgusunda toplama fonksiyonunun dönüş tipi `CAST` ile sabitlenir |
| K-653 | Kuyruk derinliği kiracıdan bağımsızdır: `IJobStore.GetQueueDepthAsync` kiracı parametresi almaz ve `tracon.job.queue.depth` kiracı etiketi taşımaz |
| K-654 | Sınırlı yapısal yanıt onarımı (bounded repair) aynı `run` içinde kalır; yeni bir `RunKind`/child run açılmaz 👤 |
| K-655 | K-615 aynen genişler: bir tool'un OBJECT parametre grafındaki her tip de `JsonSerializerContext`'e `[JsonSerializable]` ile eklenmek zorundadır, yalnız complex SONUÇ tipi değil 👤 |
| K-656 | Bir testin `MeterListener`'ı `Meter` INSTANCE'ına bağlanır, meter ADINA değil |
| K-657 | Sevk edilen bir contract'a (`Tracon.Testing.Contracts.Xunit`) dokunan faz, kapanışta `kapi.py yayin --kuru`'yu da koşar 👤 |
| K-658 | Kalıcı (Tracon damgalı) oturum taşıyan bir `run`'da bounded repair AÇILMAZ; reddetme, onarım kapalıymış gibi `run`'ı düşürür 👤 |
| K-659 | Repo private kaldığı sürece pakete giren tüketiciye dönük URL'ler doküman sitesine bakar; `RepositoryUrl` gerçek repo'da kalır 👤 |
| K-660 | Duplex bir protokolde istemciyi bekleme durumuna sokan her istek bir çıkış çerçevesi hak eder; boş commit `idle` ile kapanır 👤 |
| K-661 | Bir NuGet `<id, version>` çifti tekil bir artifact'i adlandırır: `dotnet pack` kirli (commit'siz veya untracked) bir çalışma ağacında REDDEDER, ve `kapi.py yayin` aynı kimlikte farklı SHA-256'lı bir artifact'i asla sessizce ezmez 👤 |
| K-662 | `JobKind` KALDIRILDI; işin kimliği tek bir dizge alandır (`JobRecord.HandlerKey` / `JobSchedule.HandlerKey`), ikinci bir alan tutulmaz 👤 |
| K-663 | Handler anahtarı KAYITTA verilir (`AddJobHandler<T>(key)`), handler SCOPED kaydedilir ve execution başına yeni bir DI scope'undan çözülür; dispatch tam anahtar eşleşmesidir, kayıt sırası sonucu DEĞİŞTİRMEZ 👤 |
| K-664 | Kayıtsız handler anahtarı FAIL-CLOSED'dır: iş `Failed` kapanır, `ErrorMessage` kararlı `JobErrorCodes.UnknownHandlerKey` kodunu taşır ve HAM ANAHTARI TAŞIMAZ |
| K-665 | `PUT /api/schedules/{name}` yalnız `TraconSchedulingOptions.HttpSchedulableHandlerKeys` içindeki anahtarı kabul eder (boş liste = yalnız yerleşik dokuz anahtar); izinli anahtar listesi `GET /api/schedules/handler-keys` ile ADMIN ardında yayınlanır, kimlik doğrulamasız `/api/meta` ile DEĞİL 👤 |
| K-666 | SQLite'ta `kind` sütunu YERİNDE düşürülür (`ALTER TABLE ... DROP COLUMN`), repo'nun tablo-yeniden-kurma emsali (`0006_sessions_tenant_key.sql`) İZLENMEZ |
| K-667 | Aynı tipin aynı handler anahtarıyla ikinci kaydı NO-OP'tur; çakışma yalnız İKİ FARKLI tip aynı anahtarı paylaştığında vardır |
| K-668 | Handler'ın kurucusu çözülemezse iş `JobErrorCodes.HandlerActivationFailed` ile `Failed` kapanır; YENİDEN DENENMEZ |
| K-669 | `VoiceDescriptor.Attributes` sağlayıcı üstverisini typed alanlar değil, sınırlı bir `Dictionary<string,string>` olarak taşır |
| K-670 | `IRunAuthorizationHandler` dört run başlatan yüzeyin (agent run, workflow run, inbound trigger, OpenAI uyumlu `/v1/responses`) DÖRDÜNÜ de kapsar; kapı `QuotaGate` ile AYNI çağrı şeklini taşır (elle çağrı, ortak `IEndpointFilter` DEĞİL) 👤 |
| K-671 | Reddedilen session `List` erişimi `403` döner (asla filtrelenmez); reddedilen `Read`/`Delete`/`Branch` `404` döner ve gövdesi gerçekten var olmayan bir session'la BİREBİR AYNIDIR 👤 |
| K-672 | `ContentGuardContext.Source` içerik TİPİNE göre sınıflanır (`FunctionResultContent` → `ToolResult`, rolden BAĞIMSIZ), sonra mesajın ROLÜNE göre; `Direction`'a hiç bakılmaz — geçmiş bir turun yeniden gönderilen model metni `Input` yönünde de `ModelOutput` kalır. `PatternContentGuard` `Source`'u KASITLI okumaz |
| K-673 | `run_events.custom_type` AYRI bir nullable sütundur (üç dialect'e birer migration), `payload` metni içine gömülmez 👤 |
| K-674 | Geçersiz `CustomType` (`Custom` iken boş/yanlış biçim, ya da `Custom` DIŞINDA doluyken) `RunEventWriter.AppendAsync`'i `ArgumentException` ile REDDEDER, sessizce atlamaz veya loglamaz 👤 |
| K-675 | `IToolApprovalPresenter`'ın çözdüğü sunum `pending_approvals`'a KALICI bir sütun olarak yazılır, karar anında yeniden çözülmez 👤 |
| K-676 | `IToolApprovalPresenter` fail-OPEN'dır: kayıtlı değil, `null` döner, `throw` eder veya zaman aşımına uğrar — dördü de onay isteğinin yayımını ENGELLEMEZ |
| K-677 | Alt-agent çağrısının iki katmanlı bekleme sınırında katman 2'yi (sert kesme) `ChildAgentInvoker`'ın KENDİSİ uygular; MAF'ın `BackgroundAgentsProviderOptions.WaitTimeout`'una GÜVENİLMEZ |
| K-678 | Kayıtlı olay akışının (`GET /api/runs/{id}/events`) SSE çerçeve adları AÇIK bir eşleme tablosuyla verilir; `RunEventType` üyesinin adından MEKANİK türetilmez, ve tamlık bir kapıya (`RunEventFrameNameContractTests`) bağlanır 👤 |
| K-679 | `RunEventType.Custom`'ın kayıtlı olay akışındaki SSE çerçeve adı HER ZAMAN sabit `"custom"`dır; tüketicinin kendi `CustomType` dizgesi asla çerçeve adı OLMAZ 👤 |
| K-680 | Kota muhasebesi (`RecordQuotaAsync`) artık `run`'ın terminal olay yazımından ÖNCE çalışır; kota bir hata sonrası GERİ ALINMAZ 👤 |
| K-681 | `QuotaEnforcer.RecordAsync` geçilen eşikleri döner (`ValueTask` → `ValueTask<IReadOnlyList<QuotaThresholdCrossing>>`); `IQuotaStore`'a `TryClaimThresholdNotificationAsync` eklenir — ikisi de kırıcı |
| K-682 | Kota eşiği tekilliği `quota_usage.notified_thresholds` (`text`, `,metrik:yüzde,` sınırlayıcılı CSV) sütununda kalıcı hâle gelir; atomiklik koşullu `UPDATE`'in etkilenen satır sayısıyla sağlanır |
| K-683 | Kaynak yetkilendirmesi AYRI bir sözleşme açmaz: var olan `RunAccess`/`SessionAccess` enum'ları büyür, `RunAuthorizationRequest` `RunId` kazanır ve `AgentName` `required` olmaktan çıkar; `IRunAuthorizationHandler`'ın metot sayısı DEĞİŞMEZ. Enum'ların sayısal değeri bir persistence sözleşmesi DEĞİLDİR ve bu XML'e açıkça yazılır 👤 |
| K-684 | Reddedilen TEKİL kaynak `404` döner ve gövdesi gerçekten var olmayan kaynakla BİREBİR aynıdır; reddedilen LİSTE `403` döner. Kapı, kaynak bulunduktan ve kiracısı doğrulandıktan SONRA ve durum okumasından ÖNCE sorulur; ret yanıtını çağıran verir, kapı üretmez |
| K-685 | `GET /api/runs/{id}/tools` var olmayan bir `run` için artık `200 []` değil `404` döner; bu, handler kayıtlı olmasa bile geçerli bilinçli bir davranış değişikliğidir 👤 |
| K-686 | `POST /api/attachments` reddi `403` döner, `404` değil 👤 |
| K-687 | Ses WebSocket'inin yetkilendirme reddi `404` döner ve gövdesi erişilemeyen oturumunkiyle BİREBİR aynıdır (`401` veya `403` DEĞİL); var olmayan oturum reddedilmez, handler sorulur ve varsayılan cevap soketi açar (K-283 korunur) 👤 |
| K-688 | Oturum sahipliği KALICI bir sütundur (`sessions.owner_id`, üç migration), ayrı bir `session_owners` tablosu değil; `SessionQuery.OwnerId` süzgeci SQL `WHERE` yan tümcesinde, `Skip`/`Take`'ten ÖNCE yaşar 👤 |
| K-689 | Sahiplik BİR KEZ atanır: ilk yazımda çözülür, sonraki her yazımda KAYNAKTAN taşınır ve üç SQL `store` ile bellek içi `store` sütunu `COALESCE` eder — "set → unset" meşru bir geçiş DEĞİLDİR |
| K-690 | Mod açıkken `IRunAttributionContext` bir MUHASEBE değil bir YETKİLENDİRME girdisidir: çözülemeyen kimlik oturumu açtırmaz (`403`, `errorType` `session_owner_required`), `NULL` sütun bırakmaz 👤 |
| K-691 | Sahiplik sınırı `run` BAŞLATAN yüzeylerde de zorlanır (`403`), yalnız oturum uçlarında değil; ama sahipli LİSTE muafiyeti (`ManagementPolicy`) yalnız listeye uygulanır — tekil oturuk okumasında yönetim muafiyeti YOKTUR |
| K-692 | Sahip çözümünde AÇIK bir `AmbientRunAttributionScope` kayıtlı `IRunAttributionContext`'i EZER; bu öncelik yalnız SAHİPLİK içindir, attribution'ın kendi okuyucusu değişmez |
| K-693 | Sahipsiz eski satır tekil erişimde REDDEDİLMEZ (sahipli listede ise HİÇ görünmez); sahiplik geriye dönük DEĞİLDİR |
| K-694 | Katı modun yönetim muafiyeti yalnız OKUMA kapısındadır (`SessionOwnershipGate.DeniesAsync`), `run` BAŞLATMADA yoktur 👤 |
| K-695 | Sahipsiz satır reddi ile BAŞKASININ oturumu reddi aynı metni taşır; ayrı bir `errorType` icat edilmez 👤 |
| K-696 | `/v1/conversations`'ın üç OKUMA/SİLME ucu `IRunAuthorizationHandler`'a bağlandı; `POST` bağlanmadı |
| K-697 | `TraconEndpointOptions.MapOpenAIConversations` yalnız conversations ailesini yönetir; varsayılan `true` 👤 |
| K-698 | `RequireCustomBinding<T>()` serbest generic'tir; yedi sözleşmenin kapalı kümesi ÇALIŞMA ANINDA zorlanır, derlemede değil 👤 |
| K-699 | Zorunlu binding ihlali `InvalidOperationException` atar; `TraconException` ailesine yeni tip eklenmez 👤 |
| K-700 | Zorunluluk `/api/diagnostics`'te GÖRÜNMEZ; `ExtensionPointDiagnostic` bir `IsRequired` alanı almaz 👤 |
| K-701 | Kapı yalnız "yerleşik varsayılan mı" sorusunu yanıtlar; lifetime iddiası kapsam dışıdır 👤 |
| K-702 | Sözleşmenin non-nullable ilan ettiği bir koleksiyona AÇIK `null` `400`'dür, `500` değil; üretilen istemcinin non-nullable koleksiyonları da boş başlar 👤 |
| K-703 | `RunEventType.LoopIterationCompleted` 31'dir, planın yazdığı 30 DEĞİL; 30 zaten `ChildRunTimedOut`'tur |
| K-704 | Tracon MAF'a TEK bir composite `LoopEvaluator` verir, tanımın ölçüt listesini değil |
| K-705 | `LoopSettings.MaxIterations` `null` iken Tracon'in kendi `DefaultMaxIterations = 10` sabiti uygulanır |
| K-706 | `aiJudge` ölçütü `AddModelRunJudge` binding'ini kullanır; yoksa DERLEME HATASI verir, agent'ın modeline düşmez |
| K-707 | Değerlendirilemeyen bir döngü ölçütü döngüyü DURDURUR; `run`'ı düşürmez |
| K-708 | `AddLoopEvaluator` yerleşik bir `kind`'i gölgeleyemez; kayıt anında reddedilir |
| K-709 | Döngü ölçütü kaydı `TraconExtensionPoints` tablosuna GİRMEZ; sekizinci bir genişleme noktası değildir |
| K-710 | `RunScore.Name` tekillik anahtarına girer, `author` `COALESCE` EDİLMEZ 👤 |
| K-711 | `RunScore.Value` `required int` → `double?` (`null` = ölçüm yok); `TextValue` ve `Categorical` eklendi 👤 |
| K-712 | `RunScoreRules` PUBLIC'tir; invariant tek kaynaktan zorlanır |
| K-713 | `EvalCaseResult.Scores` değer/derece/tanı yazar; `Metadata` ve `Context` YAZILMAZ 👤 |
| K-714 | `IEvalStore` `DiffRunsAsync` üyesini kazanır; hizalama politikası `Tracon.Abstractions` içindeki PUBLIC `EvalRunDiffBuilder`'dadır, `Core`'da değil |
| K-715 | Karşılaştırılamayan iki koşum İSTİSNA atar (`EvalRunDiffUnavailableException`); uç `409`/`400` döner, BOŞ FARK asla dönmez |
| K-716 | `ContentChanged` bayrağı KAPSAM DIŞI; case içeriği koşum başına saklanmaz ve sınır sözleşmeye yazılır 👤 |
| K-717 | Yalnız `Completed` koşumlar karşılaştırılır; diğer her durum `400` döner 👤 |
| K-718 | `tracon eval` DÖRDÜNCÜ bir çıkış kodu kazanır: `4` = karşılaştırılamadı; `3` (kapı düştü) anlamı değişmez |
| K-719 | Bir koşumda bir case'in BİRDEN ÇOK sonuç satırı varsa case yalnız HEPSİ geçtiyse geçmiş sayılır |
| K-720 | Süreçten ÇIKAN metin sayılarını `CultureInfo.InvariantCulture` ile biçimler; risk yalnız ONDALIK ve YÜZDE belirteçlerindedir, analyzer bu sınıfı GÖRMEZ |
| K-721 | `KARARLAR.md` bütçesi 390.000 → 420.000; önce TAŞIMA denendi ve `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı |
| K-722 | Sevk edilen agent haritasının tavanı 10 KiB → 11 KiB 👤 |
| K-723 | `capabilities.md` tablosunda KAÇIRILMIŞ boru (`\|`) hücreyi bölmez |
| K-724 | SQL Server `run_scores` upsert'i artık ham `message_id`'yi değil, `message_key AS ISNULL(message_id, N'') PERSISTED` computed column'unu indeksler ve eşler; migration mevcut `NULL`/`''` çiftlerini en yeni `created_at` kalacak şekilde dedupe eder |
| K-725 | `AgentDefinitionRequest` tüketiciye ait HER `AgentDefinition` alanını taşımak ZORUNDADIR; `PUT /api/agents/{name}` tam değiştirme (full-replace) semantiğinde KALIR |
| K-726 | Approval kararında AYNI cevapla gelen tekrar `409` DEĞİL, handoff'u tamamlayan `200` döner; yalnız TERS cevap `409`'dur. Resume run kimliği approval'dan TÜRETİLİR (`TraconId.DeriveId`) |
| K-727 | `Microsoft.Extensions.AI.Evaluation.Quality` katalogu sevk edilen HİÇBİR pakete girmez; `Tracon.Core` yalnız `Microsoft.Extensions.AI.Evaluation`'ı AÇIK referanslar 👤 |
| K-728 | `RunJudgment.Score`/`Reason` KALDIRILDI; bir yargıç `IReadOnlyList<JudgeScore> Scores` döndürür 👤 |
| K-729 | Köprü metrik adını `{judge}.{metrik}` olarak önekler; ayırıcı `:` KULLANILAMAZ 👤 |
| K-730 | Yalnız MANŞET skor (adı yargıcın adına EŞİT olan) online değerlendirme penceresine ve `tracon.judge.score` histogramına girer |
| K-731 | `IEvalEvaluatorFactory` public'tir; eval suite seam'i çıplak bir `IAgentEvaluator` DEĞİL bir FABRİKADIR |
| K-732 | Sözleşmeyi ihlal eden bir yargıç FIRLATMAZ; `judge_contract` `JudgeFailure` olarak raporlanır ve doğrulama İLK YAZMADAN ÖNCE toplu yapılır |
| K-733 | Durum ön kontrolü AYRI bir salt okunur SQL yüzeyinden okur (`IStatePreflightReader`); `ISessionStore.QueryAsync` bu iş için YETMEZ 👤 |
| K-734 | Desteklenen upgrade penceresi: aynı ana sürüm içinde HER sürümden HER sürüme; söz yalnız Tracon'in KENDİ envelope'u içindir 👤 |
| K-735 | Ön kontrol ÇÖZEMEDİĞİ şifreli satırı hata SAYMAZ; "yapı kontrolü" olarak raporlar |
| K-736 | Ön kontrol "örneklem temiz" der, "hepsi okunabilir" DEMEZ; ayrım çıktıda kelimeyle kurulur |
| K-737 | Bir `OperationCanceledException` ancak İLGİLİ TOKEN gerçekten iptal edildiyse iptaldir; aksi hâlde ARIZADIR |
| K-738 | Yük ve arıza ölçümleri RAPORDUR, kapı değildir; süre hiçbir eşiğe bağlanmaz |
| K-739 | İki process senaryosu bir ÖLÇÜMDÜR, çok node DESTEK BEYANI değildir |
| K-740 | Tracon MIT DEĞİL, `PolyForm-Small-Business-1.0.0` ile sevk edilir; üç paket MIT kalır 👤 |
| K-741 | Pakette lisans anahtarı, aktivasyon çağrısı veya özellik kapısı YOKTUR; uyum tüketicinin kendi lisans taramasına bırakılır 👤 |
| K-742 | Lisans matrisi İKİ yetkili kaynakta yazılır (`src/Directory.Build.props` + `scripts/kapi.py`) ve bir test onları kilitler; üçüncü kopya türetilir |
| K-743 | `SingletonExecution.LeaseDuration` `Enabled` iken en az 3 sn'dir; bir yenileme aralığı yenilediği pencerenin İÇİNDE kalmalıdır 👤 |
| K-744 | Paylaşılan bir dizine yazan MSBuild adımı, damgayla değil TEK BİR PROJE ÖRNEĞİNE devredilerek teklenir 👤 |
| K-745 | Canlı ses oturumunun faturalanan süresi SAĞLAYICININ bildirdiği sayıdır, duvar saati DEĞİL |
| K-746 | `VoiceSessionCost` iki terimli bir `record`'dur ve toplamı YALNIZ `Total()` yapar |
| K-747 | Canlı ses append'i HER kanalda bir `delegation_id` taşır; oturum geneli append YOKTUR |
| K-748 | Delegation olayı AGENT SEÇEMEZ; agent oturum yaratılırken bir kez çözülür |
| K-749 | Eşzamanlılık limiti SAĞLAYICI ÇAĞRISINDAN ÖNCE uygulanır ve sıra tek yerde durur |
| K-750 | Canlı yolda konuşmanın METNİ varsayılan olarak kalıcıdır; SES hiç saklanmaz 👤 |
| K-751 | Giden WebSocket egress politikasını `ValidateAsync` ile ELDE çağırır; çağrı bir TEST MADDESİDİR |
| K-752 | Ses yüzeyinin 404 gövdesini TEK bir yazar üretir (`VoiceEndpointGates`) |
| K-753 | Bir yeniden adlandırma migration YOLLARINI taşıdığında `applied-migrations.json` YENİDEN TEMELLENDİRİLİR; bu iş İKİ commit'tir |
| K-754 | Analyzer tanı öneki `APG` değil `TRC`'dir; kısaltmalar ad aramasıyla BULUNAMAZ, elle aranır 👤 |
| K-755 | NuGet paketlerinin sahibi `Tracon` ORGANİZASYONUDUR, kişisel hesap değil 👤 |
| K-756 | Doküman sayfasının gzip tavanı 57 000 B → 58 000 B 👤 |
| K-757 | Console'un varsayılan teması SAKLANAN TERCİHTİR (`dark`), medya sorgusu DEĞİL |
| K-758 | Console'un runtime bağımlılık kümesi DÖRT isimle kapıya bağlandı 👤 |
| K-759 | Bir `OperationCanceledException`'ın SEBEBİNİ söyleyen her yol, o sebebin KENDİ kaynağını sınar; çağıranın token'ını dışlamak yetmez |
| K-760 | `TraconClientOptions` bir `Timeout` alanı taşır; kendi bütçesini kuran çağıran onu SONSUZA çeker |
| K-761 | `.claude/settings.json`'daki `deny` bloğu bir KORKULUKTUR, güvenlik sınırı DEĞİLDİR |
| K-762 | Denetçinin Bash ile yazması ENGELLENMEZ; `tools` allowlist'i korkuluktur 👤 |
| K-763 | `permissions.ask` bir KİLİT değildir; oturumun izin moduna tabidir ve auto mode onu SESSİZCE onaylayabilir |
| K-764 | Bir kapı bir girdi BİÇİMİNİ tanımıyorsa "kapsam dışı" demek onu SESSİZ yapar; kapı her biçimi sayar ya da saymadığını BİLDİRİR |
| K-765 | Kurtarma rampalarının öneki `KR-`'dir ve bir rampanın GÖVDESİ TEK YERDE yaşar; katalog on ikiden yedisini yalnız BAĞLAR, kopyalamaz |
| K-766 | Süreç ölçümü kapısı bölümün VARLIĞINI denetler, DOĞRULUĞUNU denetlemez |
| K-767 | Süreç ölçümü eşiği sabit sayı `167`'dir (kullanıcı kararı); geriye dönük 166 faz DOLDURULMAZ 👤 |
| K-768 | 🔴 denetim bulgusunun triyajını KULLANICI yapar; denetçi yalnız ÖNERİR |
| K-769 | Bir üretim kararı İKİ kontrol taşıyabilir ve risk başına EN KATI cevap kazanır 👤 |
| K-770 | `NotApplicable`, bir riski taşıyan HİÇ kontrol kayıtlı olmadığında üretilir; sevk edilen altı kontrolün hiçbiri bunu DÖNMEZ 👤 |
| K-771 | Toplu kabul yolu (`AcceptAll()`) YOKTUR ve eklenmeyecektir |
| K-772 | `ProductionProfileResult` bir `record` DEĞİLDİR ve üç fabrikayla kurulur |
| K-773 | Profil kümesi bir SÜRÜM SÖZLEŞMESİDİR; kümeye anahtar eklemek DAVRANIŞSAL KIRICI değişikliktir 👤 |
| K-774 | Kapasite ölçümü bir KAPI DEĞİLDİR; hiçbir profili standart kapanışa, PR yoluna veya release hattına girmez ve CI'da yalnız `smoke` koşar |
| K-775 | Yayımlanan kapasite sayısı sürüm ve commit taşır; ölçüm yenilenmeden sürüm satırı güncellenmez 👤 |
| K-776 | Denetim izinin garanti AYRIMI yayımlanmış bir sözleşmedir: ALTI işlem fail-closed'dır, kalan her audit yazımı best-effort'tur; kümeye ekleme veya çıkarma yayımlanmış bir güvenlik garantisini değiştirir |
| K-777 | SBOM üretimi ve NuGet paket imzalama preview hattında YAPILMAZ; GA turuna ertelenir 👤 |
| K-778 | İş kuyruğu MAF'ın durability uzantısının YERİNE GEÇMEZ; sevk ve zamanlama Tracon'un, workflow içi dayanıklılık MAF'ındır 👤 |
| K-779 | Denetim izinin `before`/`after` içeriği at-rest content protection kapsamı DIŞINDADIR; bu adlandırılmış bir kabul edilen risktir, sessiz bir kusur değil |
| K-780 | `Tracon.Testing` çalışma paketleriyle AYNI matrisi hedefler (`net8.0;net9.0;net10.0`); K-270'in tek-TFM daralması KALDIRILDI |
| K-781 | `docs/KARARLAR.md` bütçesi 420.000 → 450.000; sınır yine damıtma SONRASI ölçülen değere ~%7 boşluk eklenerek kondu |
| K-782 | Best-effort `run` kaydı YAYIMLANMIŞ bir sözleşmedir (K-776'nın kardeşi) ve ihlali SAYILIR; `tracon.recording.stage` KAPALI bir kümedir |
| K-783 | Bildirimsel `kind` adları HER İKİ defterde de büyük/küçük harf DUYARSIZ çözülür; yerleşik bir `kind`'in harf varyantını kaydetmek başlangıçta atar 👤 |
| K-784 | Bir kapı, iddianın makine okunur bir kaynağı VARSA doğruluğu denetler; yoksa yalnız varlığı (K-766'nın diğer yüzü) |
| K-785 | Konsolda `window.confirm` / `alert` / `prompt` KULLANILMAZ; tek bir modal katmanı vardır (`components/dialog.tsx`) ve `frontend/scripts/check-modal-layer.mjs` bunu zorlar |
| K-786 | Bir aksiyon doğrulama adımı alır ancak ve ancak (a) arayüzden aynı girdilerle geri getirilemeyen bir durumu yok ediyorsa VEYA (b) tekrarlanamayan bir kararı kesinleştiriyorsa; ölçüt şema kanıtıyla ÖLÇÜLÜR |
| K-787 | Doğrulama dialogu aksiyon uçarken AÇIK KALIR ve sunucu reddini kendi içinde gösterir; `consequence` metni katman 1'in tooltip'iyle AYNI anahtardan gelir |
| K-788 | Evaluator sürümü `RunJudgment.EvaluatorVersion` ile taşınır; `IRunJudge` seam'i DEĞİŞMEZ 👤 |
| K-789 | `RunScore.EvaluatorVersion` sınırı `RunScoreRules.MaxEvaluatorVersionLength` (128); sütun `varchar(128)` 👤 |
| K-790 | `EvaluatorVersion` `null` = SÜRÜM ÇÖZÜLMEDİ, "sürüm sıfır" değil; çözülemezse skor YİNE yazılır |
| K-791 | `ModelRunJudge` sürüm damgalamaz |
| K-792 | İptal, sevk edilen store sözleşmesinin bir parçasıdır: zaten iptal edilmiş bir token ile çağrılan store metodu `OperationCanceledException` fırlatır ve HİÇBİR satır yazmaz 👤 |
| K-793 | `StoreCancellationContract<TStore>` her store sözleşmesinin KÖKÜDÜR; `TenantIsolationContract<TStore>` ondan türer 👤 |
| K-794 | Akış dönen bir store okuma metodunun İLK `MoveNextAsync`'i iptal edilmiş token'da fırlatır 👤 |
| K-795 | `tracon` komutu tüketicinin ÇALIŞMA AĞACINA yazar; sözleşme "yalnız yokken yaz"dır ve varsayılan hedef REPO KÖKÜDÜR 👤 |
| K-796 | Bir agent skill formatı ÖLÇÜLMEDEN sevk edilmez; ölçüm ayırt edici olmak zorundadır 👤 |
| K-797 | Kapı skill'inin revizyon damgasını PAKET değil TOOL taşır; bayat skill'in düzeltmesi "önce tool'u güncelle"dir |
| K-798 | Bir tool sonucu, sağlayıcı adaptörünün TELE YAZDIĞI metinle ölçülür; `AIContent` protokol sonucu bu ölçümün dışında değildir |
| K-799 | `ContentGuard`'ın "incelenemeyen sonuç" fail-closed dalı, okunamayan sonuç içindir; `string` OLMAYAN her sonuç için değil |
| K-800 | Uzak bir tool'un davranışını sınayan fake, uzak tool'un DÖNÜŞ ŞEKLİNİ taklit etmelidir |
| K-801 | Bir eval case'inin kimliğini istemci AÇIKÇA taşır; sunucu içerikten tahmin etmez 👤 |
| K-802 | Bir case'in promosyon kaydı SUNUCUNUN kendi verisidir ve KİMLİKLE taşınır |
| K-803 | Bir SSE tüketicisi canlılığı FRAME değil BAYT sayarak ölçer 👤 |
| K-804 | `/api/diagnostics` yürürlükteki `RunRecording` ayarlarını bildirir 👤 |
| K-805 | Tool zaman aşımı artık gövdeyi İPTAL EDER; bugüne kadar hiçbir şeyi iptal etmiyordu 👤 |
| K-806 | Zaman aşımından SONRA başarıyla biten bir tool çağrısının sonucu ve harcaması AYNI `tool_invocations` satırına yazılır; ikinci satır AÇILMAZ 👤 |
| K-807 | Sevk edilen `generate_image` kaydı kendi timeout'unu taşır (varsayılan 2 dk), genel 30 sn'yi miras almaz 👤 |
| K-808 | Fiyatsız katalog modeli AÇILIŞTA adıyla bildirilir ve `/api/diagnostics` aynı listeyi taşır 👤 |
| K-809 | Örnek uygulamanın katalog fiyatları AÇIKÇA örnektir, bakımı yapılan bir fiyat listesi DEĞİLDİR 👤 |
| K-810 | Modele giden zaman aşımı cümlesi saniyenin altını milisaniye olarak söyler |
| K-811 | Derlenmiş agent önbelleğinin anahtarı SÜRÜMÜ değil İÇERİĞİ ölçer; `CompiledAgentCache.Evict` kaldırıldı 👤 |
| K-812 | Bağımlılık parmak izi de sürümü değil, ÇAĞIRANA GÖMÜLEN içeriği ölçer |
| K-813 | Cevap veremeyen bir store `503` döner ve HANGİ tür erişilemezlik olduğunu söyler; şema adı ve SQL metni yine sızmaz 👤 |
| K-814 | Anahtar deposunu okuyamayan açılış kapısı KENDİ sonucuna varır: "anahtar kanıtlanamadı" |
| K-815 | Kriptografik doğrulama hatası da anahtarı ADIYLA söyler; beşinci dal diğer dördüyle aynı hizaya getirildi |
| K-816 | Normalleştirilen sağlayıcı hatası `StableIdentities` ile sınıflandırılır; desen yolu o trafiği hiç görmüyordu |
| K-817 | Maskelenen sağlayıcı hatasının kalıcı mesajı ÜÇ OLGU taşır: sağlayıcı adı, istisnanın tip adı, HTTP durum kodu 👤 |
| K-818 | HTTP durum kodu parmak izi normalleştirmesinde GÜRÜLTÜ DEĞİLDİR ve korunur |
| K-819 | Dil kapısının iki-harfli kelime dışlaması VARSAYIM değil ÖLÇÜMDÜR; yedi bağlaç listeye girdi |
| K-820 | Sürüm geçmişi okuması, bir agent'ın tanımının NEREDE yaşadığını söyler; `404` kalır, gerekçe değişir 👤 |
| K-821 | Reddedilen bir enum değeri KENDİNİ ve alternatiflerini söyler; converter TİPE DEĞİL istek gövdesi options'ına takılır 👤 |
| K-822 | Proplanmamış bir sağlayıcı BOZULMA DEĞİLDİR; `/health` yalnız bilinen arızayı sarıya boyar 👤 |
| K-823 | Cevabın yalnız hata olabileceği anda soru sorulmaz; A2A kurulumu şemayı beklemeden kataloğu LİSTELEMEZ |
| K-824 | Sevk edilen inline script'e CSP izni HASH ile verilir ve hash SEVK EDİLEN METİNDEN HESAPLANIR, yazılmaz |
| K-825 | Yayın provası sürüm bölümünü bulamazsa `## [Unreleased]`'i okur; bölüm ETİKET anında yeniden adlandırmayla doğar 👤 |
| K-826 | Serbest biçimli bir `JsonElement` alanının BOŞ değeri JSON `null` değil BOŞ DİZİDİR |
| K-827 | SQLite sağlayıcısı reddedilen bir yazmayı YENİDEN GÖNDERİR; `busy_timeout` yetmez 👤 |
| K-828 | Şablon yer tutucusu açılışta durdurur, ama YALNIZ bir sağlayıcı yapılandırıldığında 👤 |
| K-829 | Denetim filtresi ADI kimlik bilgisi taşıyan alanı değil, DEĞERİ taşıyanı redakte eder |
| K-830 | Kapanan bir kapı span'in durumunu KENDİ ADIYLA kapatır |
| K-831 | Normalleştirilmiş bir sağlayıcı hatasının `fault` ADI zaman aşımıysa sınıf `ProviderError` değil `Timeout`'tur 👤 |
| K-832 | Sınıf taraması İKİ gölgeleme daha ölçtü; ikisi de bilerek KODLANMADI |
| K-833 | Rolün reddettiği bir panel isteği HİÇ GÖNDERMEZ; 403'ü çizmek "konsol bozuk" diye okunur |
| K-834 | Referans örneğin gösteremediği bir seam için KALICI demo kancası eklenir; "geçici `Program.cs` düzenlemesi" bir mekanizma değildir 👤 |
| K-835 | `Tracon.Google`'ın görsel yolu ARTIK SUNULMAYAN Imagen `:predict` yüzeyini hedefliyor; kusur açık, düzeltme kullanıcı kararına bırakıldı 👤 |
| K-836 | Kiracı kimliği, karşılaştırıcı değiştirilerek değil DEĞER NORMALLEŞTİRİLEREK case-duyarsız yapılır; kural `AmbientTenantScope.Normalize` olarak public'tir ve hem giriş sınırı hem SQL depolama sınırı uygular (Faz 179, A-1) |
| K-837 | Kanonik olmayan `tenant_id` taşıyan veritabanında migration DURUR; satırlar KATLANMAZ (Faz 179) |
| K-838 | `tenant_id` case guard'ı üç `.sql` migration'ı olarak değil, `SqlDialect` üzerinden tek bir C# adımı (`TenantIdCaseGuard`) olarak yazılır ve tablo listesi KATALOGDAN okunur (Faz 179, plandan sapma) |
| K-839 | Bellek içi store'ların yalnız İKİSİ (`InMemoryTenantEgressPolicyStore`, `InMemoryTenantProviderBindingStore`) kiracı kimliğini normalleştirir; kalan 30'u kanonik girdi BEKLER (Faz 179) 👤 |
| K-840 | `exception is not OperationCanceledException` filtresi TEK BAŞINA yanlıştır; kural `OperationCancellation.IsFailure(exception, token)` olarak tek yerde yazılır (A-3 · A-24 sınıf taraması) |
| K-841 | Onay parmak izi UZUNLUK-ÖNEKLİ formata geçer; `U+001F` ayırıcı varsayımı terk edilir (A-5) |
| K-842 | `TenantChatClientCacheKey` `internal` yapılır (A-4) 👤 |
| K-843 | `dotnet new tracon-api`'nin ürettiği projenin paket sürümü, ŞABLON PAKETİNİN KENDİ SÜRÜMÜNE paketleme anında damgalanır (A-9) 👤 |
| K-844 | Deadline'la duran bir workflow `run`'ı `Canceled` KALIR ve sebebi `RunRecord.Error`'da taşır; sebep her kapıda değil, `run`'ın kapandığı TEK yerde eklenir (A-26) 👤 |
| K-845 | Bir workflow `run`'ı çağıran iptal ettiğinde VEYA tüketici akışı okumayı bıraktığında `Canceled` olarak kapanır; `RunGuardedAsync`'in `finally`'si bunu garanti eder (A-32) 👤 |
| K-846 | `DbDataSource` adapter'larının `ConnectionString` özelliği parolayı düşürür |
| K-847 | "Silinmez, taşınır" kuralına tanımlı istisna: yenilenen kanıt eskisinin yerine geçince ve sıfır canlı referans ölçülünce eski kayıt silinebilir 👤 |
| K-848 | İstisna atan bir `IModelProviderHealthCheck` listeyi düşürmez: o sağlayıcı `Unhealthy` raporlanır, `detail` YALNIZ istisna tipini taşır, istisnanın kendisi Warning log'a gider (Faz 181, plan dışı kusur) |
| K-849 | Dört provider adaptörünün ortak gövdesi `src/Tracon.Providers.Shared/` shared-source ağacındadır; paket değildir, her tipi `internal` kalır (Faz 181, F-258) 👤 |
| K-850 | Public yüzey dış kanıt ölçütüyle daraltıldı: 91 tip adı (93 paket×tip — `MigrationRunner` üç pakette) `internal` oldu; kalan her tipin kanıtı `scripts/public-yuzey-envanteri.py` ile ölçülür ve kanıtsız kalan tipin gerekçesi `scripts/public-yuzey-gerekceleri.tsv`'ye yazılır (Faz 182, F-259) |
| K-851 | Onay kuralı koşul kümesinin parmak izi tek iç fonksiyondadır (`ToolArgumentConditionFingerprint`); ayırıcı taşıyan bir yol kümeyi uzunluk önekli biçime geçirir, diğer her küme eski biçimi korur — migration yok (Faz 182, plan dışı kusur) |
| K-852 | Bir kaydın adını taşıdığı `secret` anahtarı KİRACININ anahtar alanında olmalıdır: varsayılan olmayan kiracı `{prefix}{tenant}:...` kullanır, önek altındaki düz ad varsayılan kiracınındır; kural dört yüzeyde (MCP, BYOK, webhook, trigger) hem kaydetmede hem çözmede uygulanır (kusur-giderme, 2026-09-23) 👤 |
| K-853 | Kiracıyı İSTEKTEN alan uçlar (`/api/tenants/{tenantId}/providers`, `/egress`, kiracı kayıtları `GET/PUT/DELETE /api/tenants[/{slug}]`) başka kiracıya yalnız PLATFORM YETKİSİYLE dokunur: API anahtarı `PlatformAdmin` kapsamı · statik `AuthToken` · claim tabanlı çağıran için yeni `TraconPolicies.PlatformAdmin` policy'si (çok kiracılık açıkken; kayıtsızsa REDDEDER) · anonim yerel operatör (K1) (kusur-giderme, 2026-09-23; K-469'u daraltır) 👤 |
| K-854 | Kimlik bilgisi taşımayan istek yalnız GERÇEKTEN yerelse ve başka bir sitenin tarayıcı sayfası değilse kabul edilir: `AllowRemoteAccess` açık + `AuthToken` ve policy yok → uzak anonim `401`; `X-Forwarded-For`/`Forwarded`/`X-Real-IP` taşıyan loopback bağlantısı UZAK sayılır; anonim yerel istekte `Host` loopback adı, varsa `Origin` loopback olmalı (`403`) (kusur-giderme, 2026-09-23) 👤 |
| K-855 | `net8.0` ve `net9.0`, Microsoft desteğinin bittiği 2026-11-10'dan sonraki ilk Tracon sürümünde düşer; acil bir güvenlik sürümü onları hâlâ taşıyabilir (kusur-giderme turu, 2026-09-23) 👤 |
| K-856 | MCP sunucusu ve webhook aboneliğinin ek `headers` DEĞERLERİ hiçbir HTTP yanıtında ve MCP audit'inde yer almaz: her değer `***` olur, başlık adı kalır; `***` değerli kaydetme `400` alır; değer `store`'da düz kalır ve hedefe aynen gider (kusur-giderme, 2026-09-23) 👤 |
| K-857 | `QuotaUsageQuery.PeriodStarts` store'un uyguladığı dönem süzgecidir; verilmezse tüm geçmiş döner; `AsOf` `[Obsolete]` olur (kusur-giderme, F-275, 2026-09-24) 👤 |
| K-858 | Her Tracon→Tracon nuspec bağımlılığı tam aralıktır (`[x]`) ve paketin kendi sürümüne eşittir; yalnız IVT kenarları değil, 22 kardeş kenarının hepsi (Faz 185, F-265) (kullanıcı kararı) 👤 |
| K-859 | Yüklü Tracon aile derlemeleri farklı sürümdeyse host başlamaz; kontrolün opt-out'u yoktur; preview hattında silinen üyeye ikili uyum shim'i yazılmaz (Faz 185, F-265) (kullanıcı kararı) 👤 |
| K-860 | Script grant'ı içeriği pinler: stored ve kodda tanımlı script yalnız içeriğinin hash'ini taşıyan grant'la çalışır; hash'siz eski grant onları yetkilendirmez; diskteki script pinlenmez (Faz 186, F-269) (kullanıcı kararı) 👤 |
| K-861 | Çok kiracılı host'ta stored script grant'ı platform yetkisi ister (`PlatformAdmin` kapsamlı anahtar, statik token veya `Tracon.PlatformAdmin` policy'si); tek kiracılı host'ta ve kodda tanımlı skill'de gerekmez (Faz 186) (kullanıcı kararı) 👤 |
| K-862 | B7'nin adı "Script execution gates"tir ("Script sandboxing" değil); R8 kabul edilen risktir (script sunucunun OS kimliğiyle çalışır); `SECURITY.md` ve site politikasının kapsamı "Script execution gates (grant, content pin, interpreter allowlist, audit)" der (Faz 186) (kullanıcı kararı) 👤 |
| K-863 | Kodda kayıtlı bir skill adı yönetim API'sinden yazılamaz: `PUT /api/skills/{name}` `409`; `DELETE` yalnız gölgelenen kayıtlı kopyayı siler, kopya yoksa `409`; konsol kod skill'ini salt okunur açar (Faz 186, denetim 🔴1) (kullanıcı kararı) 👤 |
| K-864 | Yayın provası son yayınlanmış sürüme karşı kırıcı değişiklik kapısı koşar: `kapi.py yayin` (her push'ta `release-dryrun`) son `v*` etiketinin library paketlerini izole bir cache'e nuget.org'dan restore eder, ApiCompat taban doğrulamasıyla paketler, doğrulamanın bu koşumda koştuğunu semaphore ile kanıtlar; kırılan her public tip ve düşen her TFM sürüm notunda tam adıyla (code span, joker değil) geçmezse prova kırmızıdır ve `publish` koşmaz (Faz 187, F-270) (kullanıcı kararı) 👤 |
| K-865 | Her library paketinin public yüzeyi TFM'ler arasında aynıdır: `EnableStrictModeForCompatibleTfms` ve `EnableStrictModeForCompatibleFrameworksInPackage` `src/Directory.Build.props`'ta koşulsuz açık; yalnız bir TFM'de derlenen public üye pack'i kırar (Faz 187) (kullanıcı kararı) 👤 |
| K-866 | DI'ın veya Tracon boru hattının kurduğu public servis tipinin kurucusu `internal`'dır; tip public kalabilir. Tip tabanlı DI kaydı olan böyle bir tip fabrika kaydı kullanır. Opsiyonel parametreli public kurucu yalnız tüketicinin kurduğu tipte olur ve ratchet tabanına gerekçeyle girer; taban yalnız küçülür (Faz 188, F-271 A) (kullanıcı kararı) 👤 |
| K-867 | `ITraconBuilder` yalnız `Services` taşır; Tracon'un bütün kayıt yetenekleri statik uzantı metodudur (`TraconBuilderExtensions`, Core) ve arayüze üye eklenmez. `TraconToolRegistration` tek zorunlu kurucu parametresi (`function`) + yedi `init` ayarı kullanır; yeni tool ayarı `init` özelliğidir. Kaldırılan imzalar `[Obsolete]` geçişi olmadan kalktı, önceki preview ikilisi yeniden derlenir (Faz 189, F-271 B) (kullanıcı kararı) 👤 |
| K-868 | Kimlik başlığı yalnız anahtar ADIYLA saklanır (`HeaderConfigurationKeys`, MCP + webhook); her ad kaydetmede ve çözmede K-852'den geçer; düz `headers`'ta kimlik benzeri ad `400`; eski satır gönderilir ve uyarı loglanır (Faz 190, K-059'u genişletir) (kullanıcı kararı) 👤 |
| K-869 | `AuthorizationConfigurationKey` `[Obsolete]` (yalnız mesaj; `DiagnosticId` ve `UrlFormat` YOK), `1.0.0`'da kalkar; iki alandan veya OAuth ile `Authorization` `400` (Faz 190) (kullanıcı kararı) 👤 |
| K-870 | MCP/webhook `PUT`: `headers` veya `headerConfigurationKeys` yok ya da `null` → saklı değer; `{}` temizler (Faz 190) (kullanıcı kararı) 👤 |
| K-871 | Bir `v*` etiketinin nuget.org'a ittiği her `.nupkg`/`.snupkg`, aynı workflow koşumunda build işinin ubuntu bacağının derleyip test ettiği derlemeden `--no-build` ile üretilen dosyadır; `release-dryrun` onu paketlemeden doğrular, `publish` SHA-256 manifest'ini yeniden sınayıp yalnız onu iter; CI'da ikinci bir yayın `dotnet pack`'i yoktur (Faz 191, F-273) (kullanıcı kararı) 👤 |
| K-872 | Bir paketin ön sürüm üçüncü taraf bağımlılığı nuspec'te tam aralıktır (`[x]`); kararlı üçüncü taraf bağımlılık NuGet'in alt sınırında kalır (A-59, UR-006 A, UR-008 B) (kullanıcı kararı) 👤 |
| K-873 | Store'un kaydetme yolu taşıdığı değeri yazar, sahibi olmadığı alana dokunmaz: `IExperimentStore.SaveAsync` canary kuralını ve rollback sebebini yazmaz (güncellemede saklı değer kalır, yeni kayıtta boş başlar); `IWebhookStore.SaveSubscriptionAsync` `ConsecutiveFailures`'ı taşındığı gibi yazar; sevk edilen sözleşme bunu üç yeni vakayla zorlar (kusur-giderme, 2026-10-10) (kullanıcı kararı) 👤 |
| K-874 | Agent'ın kendi modeli dışında çağırdığı model istemcileri (sıkıştırma özetleyicisi, `aiJudge` loop yargıcı) agent'tan ÖNCE kurulur; async derleme yolunda özetleyici kiracı egress politikası + kiracı anahtarı (BYOK), yargıç egress politikası + kurulum anahtarı alır; önbellek atlama kararı özetleyicinin bağlamasını da sayar; senkron `Compile` kiracıdan habersiz kalır (kusur-giderme, 2026-10-11) |
| K-875 | Kiracı egress politikasının İÇERİĞİ derlenmiş agent önbelleğinin anahtarına girer; kaydedilen politika bir sonraki çözümde etki eder, sürmekte olan run kesilmez; bedeli çözüm başına bir politika okumasıdır (F-304, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-876 | Kalıcı onay kuralı tool'un KAYNAĞINA bağlanır: `ToolApprovalRule.SourceFingerprint` (MCP endpoint'inin SHA-256'sı, adres değil) kural yazılırken damgalanır (yönetici ucu ve "bir daha sorma" akışı), kimliğin parçasıdır ve eşleşme KESİN eşitliktir; damgasız kural (kod tool'u ya da eski kayıt) uzak kaynaklı tool'u onaylamaz, eski MCP kuralları yükseltmede bir kez yeniden sorar; tool'ları o an keşfedilmemiş sunucunun tool'u `Unresolved` raporlar — hiçbir kural eşleşmez, kural yazımı `409` (F-283, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-877 | `requiresApproval: false` yazımı API key ile çağrıldığında `AgentsAdmin`'e EK olarak `SecurityAdmin` ister: yeni sunucuda, `true`→`false` geçişinde ve feragat sürerken endpoint değiştiğinde; değişmeyen feragatin yeniden kaydı ve onayı yeniden istemek serbesttir; kontrol aynı uçtadır (`CrossTenantAuthority` emsali) (F-284, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-878 | Sağlayıcı-barındırmalı canlı ses kiracının egress politikasına ve kendi anahtarına (BYOK) tabidir: denetim oturum satırından, slot'tan ve sağlayıcı çağrısından ÖNCE launcher'da koşar; anahtar `LiveVoiceCreateRequest.Credential` ve yeni `ILiveVoiceProvider.AttachSessionAsync(LiveVoiceAttachRequest)` ile sağlayıcıya geçer; `SupportsTenantCredentials` (varsayılan `false`) olmayan sağlayıcı bağlamalı kiracıya `403` verir; uç nokta override'ı taşıyan bağlama canlı seste launcher'da `403` ile reddedilir; politika ve kimlik kuralı `TenantProviderAccess`'te TEK yerde yaşar (F-306, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-879 | Bir run'ın olayları TEK TEK yazılır: numara, `store` yazımı ve sink dağıtımı tek adımdır, sonraki olay bekler (`RunEventWriter` başına `SemaphoreSlim`); sink içinden aynı writer'a append beklemek yerine `InvalidOperationException` atar ve sink devre dışı kalır; bekleme çağıranın token'ını dinler; bellek içi `ReadEventsAsync` sıralı okur (F-257, kusur-giderme 2026-10-11) |
| K-880 | Derlenmiş agent önbelleğinin anahtarı, tanımın her MCP tool'unun bağlı olduğu bağlantı ÖRNEĞİNİN kimliğini (`McpConnection.Generation`) taşır; anlık görüntüde olmayan ad `absent` yazar (silme, devre dışı, refresh hatası); aynı adlı kod tool'u bileşen eklemez; uzak tool açıklaması ve şeması anahtara girmez (F-305, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-881 | `[TraconTool]` üretecinin her derlemeye yazdığı `TraconGeneratedToolsBuilderExtensions.AddGeneratedTools()` `internal`'dır: yalnız kendi derlemesinin tool'larını kaydeder; bir kütüphane tool'larını host'a kendi public uzantısıyla verir (o uzantı `AddGeneratedTools()`'u kütüphanenin içinden çağırır); `TRC0005` mesajı bu yolu söyler (F-289, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-882 | `CompiledAgentCache` `(kiracı, ad, tanım parmak izi, kültür)` başına TEK girdi tutar; yeni bağımlılık parmak iziyle derleme o girdiyi değiştirir, tanım parmak izi ayrı yuvadır (A/B sürümleri birbirini atmaz); MCP bağlaması bağlantı kimliğine ek olarak uzak tool'un protokol `Tool` JSON'unun SHA-256'sını taşır — K-880'in "açıklama ve şema anahtara girmez" kısmı kalkar (F-308, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-883 | `tools/list` başarısız olunca MCP bağlantısı, kayıtlı ve tanımı aynı kaldıkça, son iyi tool/kaynak listesini son başarılı listelemeden itibaren `TraconMcpOptions.StaleToolRetention` (varsayılan 15 dk; `Zero` = eski davranış, negatif açılışta reddedilir) boyunca sunar; refresh sonucu yine "ulaşılamaz" sayılır; süre geçince sonraki başarısız refresh listeyi düşürür ve K-880'in `absent` reddi döner (F-309, kusur-giderme 2026-10-11) (kullanıcı kararı) 👤 |
| K-884 | `LoopSettings` iki bağımsız alan taşır (`OnExhausted`, `OnCriterionError`) ve ikisi de tek enum'dur (`LoopOutcome`: `ReturnLastResponse = 0` varsayılan · `FailRun = 1`, JSON'da ad); `OnExhausted = FailRun` iken tavandaki tur DEĞERLENDİRİLİR (MAF'a N+1 verilir, karar N'de); yeni sınıf `RunErrorClass.LoopUnsatisfied = 15` yalnız tükenme içindir, ölçüt hatası orijinal istisnayla ve kendi sınıfıyla düşer (Faz 192, F-293) (kullanıcı kararı) 👤 |
| K-885 | Her SSE ucu `SseWriter` içinde kendi keep-alive'ını yazar; aralık yeni public `TraconEndpointOptions.StreamKeepAliveInterval` (15 sn); workflow slot beklemesi run satırı açıldıktan sonradır (F-325) (kullanıcı kararı) 👤 |
| K-886 | Terminal job değişmez (`IJobStore` sözleşmesi + iki contract case); kira dolumu denemedir, sınırı aşan job `AttemptsExhausted` ile koşmadan düşer; yenileme tick başına, kiranın üçte birinde; owner fencing ertelendi (F-326) (kullanıcı kararı) 👤 |
| K-887 | Workflow katılımcısı cevap veremeyen adımı düşürür (`ChildRefusalMode.Fail`), delegasyon metin döner; süre aşımı yeni public `TraconChildRunTimeoutException`; workflow satırı `ErrorType` + sınıf taşır (F-328 · F-314) (kullanıcı kararı) 👤 |
| K-888 | Retention `AwaitingInput` run'ın istek olayını ve checkpoint'lerini tutar; checkpoint temizliği oturumda bekleyen/koşan run varken atlanır; istek kimliği olay `Text`'inden okunur (F-327) (kullanıcı kararı) 👤 |
| K-889 | Akışsız döngü son turun cevabını, tüm turların usage toplamıyla döner; `AggregatedResponseGuard` MAF OTel'in bayat `RawRepresentation` kurulumunu keser; akışlı usage çağrıların toplamıdır; doğrulama son asistan mesajını okur (F-317 · F-329) (kullanıcı kararı) 👤 |
| K-890 | Parametreli agent her yolda bağlanır ya da düşer: `BuildAgent` varsayılanları bağlar, eksik zorunlu parametrede `UnboundParameterGuard`; giriş uçları erken reddeder; `/run` kapısı ve eval kod tanımını okur; replay ve devam parametreli tanımı reddeder (F-330; bu madde K-898 ile değişti: kayıtlı değer varsa kullanılır) (kullanıcı kararı) 👤 |
| K-891 | Descriptor, derlenen tanım sürümünü tanımlar: iç `IDescribingAgentSource` seam'i, tek `AgentDescriptorProjection`; üçüncü taraf kaynakta yalnız `Version` değişir (F-331) (kullanıcı kararı) 👤 |
| K-892 | Harness skill, dosya belleği ve web aramasını yalnız tanım isteyince açar (`SkillNames`, `Memory.EnableFileMemory`, yeni public `HarnessSettings.EnableWebSearch`); tool döngüsü Tracon'undur: MAF'a giden akış güncellemesi kopyadır, `MaximumIterationsPerRequest` iç döngüye ve fallback halkasına taşınır (F-339 · F-315) (kullanıcı kararı) 👤 |
| K-893 | Birden çok modelin harcadığı run her payı kendi modelinin fiyatıyla fiyatlar ve toplar; o run'da `RunCost` birim fiyat alanları `null`; `aiJudge` yargıcının token'ı run `usage`'ına, maliyetine ve kotasına girer; karma satır yeniden fiyatlanmaz (F-349) (kullanıcı kararı) 👤 |
| K-894 | Kuyruklu run'ın deney kolu ve kültürü enqueue anında payload'a yazılır; worker kolu `IExperimentStore` ile doğrular (kiracı, agent, başlamış deney, varyant↔sürüm; Draft ve yarım kol reddedilir), uymazsa iş model çağrısı olmadan KAPALI düşer; kültür run kaydına yazılmaz (F-351) (kullanıcı kararı) 👤 |
| K-895 | Onay devamı onayı isteyen run'ın agent sürümünde ve deney kolunda koşar (sürüm geçmişi olan kaynakta; kod agent'ı güncel tanımla); payload'daki agent adı kaynak run'ınkiyle aynı değilse ya da kaynak run yoksa iş KAPALI düşer; sürümsüz replay'in güncel sürümle ve alt agent'ın güncel sürümle koşması belgelenmiş tasarımdır (F-352) (kullanıcı kararı) 👤 |
| K-896 | Retention, erasure ve ek deposu harici baytı satırdan ÖNCE siler; bayt silinemezse retention satırı tutar, erasure geri alınır; `IAttachmentStorage.DeleteAsync` idempotenttir; workflow run'ı oturumunun eklerine sahiptir (F-332) (kullanıcı kararı) 👤 |
| K-897 | Workflow parametre şeması `WorkflowDefinition.Parameters` (aynı `AgentParameter`); değer `WorkflowRunRequest.Parameters` ile gelir, runner doğrular (HTTP yalnız erken 400); agent adımı YALNIZ kendi şemasındaki adları alır, derleme katalog dışı ve her çağrıda yapılır; katılımcı kapsaması run BAŞINDA denetlenir, kayıtta değil; fonksiyon düğümü `GetRunParameters()` ile okur, run dışında atar; kod tanımlı workflow şemasızdır; `Kind` doğrulanmaz (F-294) |
| K-898 | `run_inputs.parameters` (üç sağlayıcıda nullable sütun): `NULL` "kayıt yok", `{}` "kayıtlı, değer yok"; kendi `ProtectedColumn.RunInputParameters`'ı var; retention `AwaitingInput` run'ın satırını tutar; run bağlandığı NİHAİ değerleri (varsayılanlar dahil) yazar; replay, çökme sonrası devam, onay-devamı ve workflow resume/respond kaydı kullanır, kaydı olmayan kaynak ve parametreli tanım REDDEDİLİR; replay isteği üzerine yazar; workflow satırı mesaj saklamaz; K-890'ın "replay ve devam parametreli tanımı reddeder" maddesi değişti (F-294) |
| K-899 | Kuyruklu run yükü `parameters` ve `documents` taşır; batch öğesi `message` string üyeli JSON nesnesiyse YAPILIdır (bilinmeyen üst alan ve bozuk üye öğeyi tek başına düşürür, zamanlama kaydı ve tetik biçimi reddeder); `jobs.payload` ve `job_items.input` KORUMALI DEĞİLDİR ve değer ile belge metnini düz tutar; worker şemayı yeniden doğrular; webhook gövdesi gibi dış kaynaklı metin `{"message": …}` ile zarflanır, yapılı öğe olarak okunmaz (F-294) (kullanıcı kararı) 👤 |
| K-900 | Fonksiyon düğümü ayarı tipli ve KATI OKUMAYLA doğrulanır: `WorkflowNodeReference.Settings` (`JsonElement?`, yalnız nesne), `AddWorkflowFunction<TInput, TOutput, TSettings>`; kayıtta ve her derlemede AYNI okuma (fonksiyon doğrulanan nesneyi alır, ikinci şema doğrulayıcısı yok); agent düğümünde, nesne olmayan değerde ve ayarsız fonksiyonda `400`; ayar `secret` DEĞİLDİR, değişiklik sonraki run'da geçerlidir, boyut sınırı yalnız kayıtta (F-295) (kullanıcı kararı) 👤 |
| K-901 | Konsol zinciri TEK kanonik biçimde yazar (fonksiyon VEYA agent sürüm sabiti varsa `nodes`, yoksa `agentNames`); sunucu iki biçimi de kabul eder; yapısal değişiklik, adı konan sürümle resume edilen run'ı resume edilemez kılar (bekleyen run kaydettiği sürümle sürer, K-988) ve editör bunu her zaman söyler; aynı fonksiyon bir zincirde iki kez kullanılamaz (F-292) (kullanıcı kararı) 👤 |
| K-902 | Ayarlı fonksiyon kaydı opsiyonel parametre YERİNE `Action<WorkflowFunctionOptions>` alır; `IWorkflowFunctionCatalog` `ValidateSettings` üyesini kazanır (F-295) |
| K-903 | Kuyruklu turun oturum kaydı kira penceresinde tekrarlanır; pencere biterse iş `tracon.job.session-save-failed` ile `Failed` olur, model tekrar çağrılmaz (F-367) (ana oturum varsayılanı) |
| K-904 | Kuyruktaki run kuyruğa alanın atfını taşır: agent run yükü `userId` + `labels`, workflow ve batch yükü `{items, userId, labels}` zarfı; devam kaynak run'ın atfıyla açılır (F-364, F-369) (ana oturum varsayılanı) |
| K-905 | Eval case `Context`'i belge kanalıyla sorgudan önce gider; terfi önce `run_inputs`'u okur ve taşınamayan girdiyi `422` ile reddeder (F-343) (ana oturum varsayılanı) |
| K-906 | Devre kesici yalnız sağlayıcının attığı ve sağlığını gösteren hatayı sayar; diğer kalıcı `4xx` ve devrenin içindeki Tracon kararları nötrdür (F-354) (ana oturum varsayılanı) |
| K-907 | Fallback atfı (run modeli, `ModelFallbackUsed`) yalnız agent'ın kendi bağlamasında yazılır; yan kanalın fallback'i o çağrınındır (F-360) (ana oturum varsayılanı) |
| K-908 | Workflow yanıtı akıştan önce denetlenir (`IWorkflowRunner.ValidateResponseAsync`); kayıt ve derleyici karışık zincirin tip uyumunu atanabilirlikle denetler (F-341) (ana oturum varsayılanı) |
| K-909 | Otomatik devam yalnız kök agent run'ıdır; workflow run'ı ve alt run `Failed` kalır ve ret workflow için resume'u adlandırır (F-344) (ana oturum varsayılanı) |
| K-910 | Retention arşiv modu yalnız arşive yazdığı satırları anahtarla siler: `IRetentionStore.ArchiveBatchAsync` (varsayılan gövdeli) (F-372) (ana oturum varsayılanı) |
| K-911 | Veri sahibi kapsamı `workflow_checkpoints`'i ve oturumla bulunan run'ların `run_scores`'unu içerir; silme `AwaitingInput` muafiyeti tanımaz (F-371) (ana oturum varsayılanı) |
| K-912 | Görsel üretimi kiracının egress politikasına ve kendi anahtarına tabidir: denetim `ImageGeneratorResolver.ResolveForTenantAsync`'te, sağlayıcı çağrısından ÖNCE ve iki yolun (`generate_image`, `POST /api/images/generate`) ortak noktasında koşar; politika görsel sağlayıcısını (`TraconImageOptions.Provider`) adlandırmıyorsa ya da kiracının o sağlayıcı için bağlaması varsa üretim reddedilir — uç `403`, tool hatası; kurulum anahtarına düşülmez; kural `TenantProviderAccess`'te tek yerde (F-384, kusur-giderme 2026-10-13) (ana oturum varsayılanı, K-878 emsali) |
| K-913 | Kök agent run'ı kotaya ve kendi `run.*` webhook'una çağrı ağacının toplamını yazar: kendi usage ve maliyetine, kök satırı kapanmadan run deposundan okunan alt run toplamı eklenir; okuma yalnız ağaç bir alt run rezerve ettiyse (`AgentRunBudget.StartedRuns > 0`) ve kota ya da webhook kayıtlıysa yapılır; farklı para biriminde toplam yoktur (K-893), depo okunamazsa kökün kendi tüketimi yazılır (F-383, kusur-giderme 2026-10-13) (ana oturum varsayılanı) 🔁 |
| K-914 | Kod Loop ölçütü fabrikadan kurulabilir: `AddLoopEvaluator(kind, Func<IServiceProvider, LoopEvaluator>)` kök sağlayıcıyı alır, türü adlandıran İLK derlemede kilit altında tür başına bir kez başarıyla koşar, atan fabrika önbelleğe girmez; tek örnek bütün kiracılara hizmet eder ve kiracı değerlendirme anında okunur; dört aşırı yükleme, opsiyonel parametre yok (F-301) (kullanıcı kararı) 👤 |
| K-915 | Loop ölçüt kataloğu kök seviyede `GET /api/loop-criteria`'dır: yerleşik türler sabit sırada + kodla kayıtlı türler ada göre, açıklamalarıyla; yargıç modeli yalnız `provider` + `model` ya da `null`; Reader + `AgentsRead`; liste fabrika çağırmaz ve kiracıdan bağımsızdır (F-302) (kullanıcı kararı) 👤 |
| K-916 | Loop doğrulaması tek kural kümesidir (`LoopEvaluatorRegistry.FindProblems`): derleyici aynı listeyle reddeder, doğrulayıcının loop adımı her sorunu `loop_` kodu ve alan yoluyla raporlar (dokuz kod); yargıç istemcisi o adımda kiracı-farkında üyeyle kurulur ve kurulamazsa `loop_judge_unavailable` önceki yolsuz `compilation_error`'ın yerine geçer; `judgeCriteria`'daki her boş girdi kendi yolunda reddedilir (F-302) (kullanıcı kararı) 👤 |
| K-917 | Kod Loop ölçütü fabrikasının attığı istisnanın MESAJI yönetim API'sine gitmez: derleme hatası türü ve istisna tipini adlandırır, istisna `InnerException`'da ve host logunda durur (F-301) |
| K-918 | Konsol Loop editörü yalnız tür DEĞİŞİMİNDE yeni türün okumadığı alanı temizler; yükleme ve kayıt her alanı, listelenmeyen türü ve kanonik olmayan yazımı (`aijudge`) olduğu gibi korur; harness kapalıyken düşen loop kaydetmeden önce söylenir (F-302) (kullanıcı kararı) 👤 |
| K-919 | Çıktı sınırında kesilme ayrı bir sonuçtur: `RunErrorClass.OutputTruncated = 16`, `TraconOutputTruncatedException` (`output_truncated`), `RunEventCustomTypes.OutputTruncated` (`tracon.output.truncated`). Karar son bildirilen bitiş sebebinden verilir; yapılandırılmış (`Json`/`JsonSchema`) yanıt run'ı düşürür ve onarım koşmaz, düz metin `Completed` + olay; reddedilen yanıtın token'ı run'a katlanır (F-320, Faz 204; Açık Soru 1-A, 2-A) |
| K-920 | Anthropic istek ayarları: `anthropic.effort` (`output_config.effort`), `anthropic.thinking.mode` (`enabled`/`adaptive`; tek başına bütçe `enabled`), `anthropic.promptCaching.ttl` (`5m`/`1h`), `anthropic.promptCaching.placement` (`request`/`system`; talimatsız `system` üst düzey kırılma noktasına düşer). Değer listeleri SDK enum'larından okunur; `ttl`/`placement` `promptCaching` olmadan, `adaptive` + bütçe ve bütçesiz `enabled` hatadır (F-346, Faz 204; Açık Soru 3-A) |
| K-921 | `ModelDescriptor.SupportsSampling` (`bool?`, `null` = bilinmiyor): `false` iken `Temperature`/`TopP` derlemeyi durdurur, doğrulama `sampling_unsupported` verir; bağlamanın `Fallbacks` bağlantıları da denetlenir (Faz 204; yedek denetimi ana oturum varsayılanı, bakımcı onayı bekler) |
| K-922 | Sağlayıcının varsayılan çıktı sınırı `ModelOutputLimit` kaydıyla (`DefaultMaxOutputTokens`, `HighReasoningEffort`) bağlamaya göre kurulan ham istemcinin `IChatClient.GetService` yanıtından okunur; doğrulama, `MaxOutputTokens` boşken varsayılan katalog sınırından küçükse ve yanıt yapılandırılmışsa ya da effort yüksekse `output_truncation_risk` (Warning) verir (Faz 204 #1, #2; Açık Soru 5-B; seam biçimi ana oturum varsayılanı, bakımcı onayı bekler) |
| K-923 | Kesilen kuyruklu workflow run'ını yalnız onu başlatan job sürdürür; uzlaştırma workflow kökünü `Failed` kapatır ama sürdürmez; zincir `ContinuedFromRunId` ile ileri yürünür; çökme, deploy ve zarif kapanış ayırt edilmez; varsayılan kapalı (`ResumeInterruptedRuns`) ve checkpoint ister (Faz 197, Açık Soru 1-A) (kullanıcı kararı) 👤 |
| K-924 | Kuyruklu workflow işlemi tek anahtardır: `tracon.workflow-run` + yükte `operation` (`run`/`resume`/`respond`); iş kimliği run kimliğidir; anahtar HTTP ile zamanlanamaz (`HttpSchedulableHandlerKeys` de açamaz); kapılar `202`'den önce, senkron uçla aynı sırada koşar; kuyruklu resume/respond kotaya girmez; respond-async F-319'u beklemez (Faz 197, Açık Soru 2-A, 3-A, 6-A) (kullanıcı kararı) 👤 |
| K-925 | Üç workflow ucu `Idempotency-Key`'i yalnız `Prefer: respond-async` ile kabul eder; `Prefer`'siz anahtar `400` alır (Faz 197, Açık Soru 5-A) (kullanıcı kararı) 👤 |
| K-926 | `RunStartInfo.ContinuedFromRunId` ikinci başlatmada korunur (dört depoda `COALESCE`); `RunQuery.ContinuedFromRunId` ve `GET /api/runs?continuedFromRunId=` devam zincirini ileri yürür; `IJobStore.ReportItemAsync` yalnız bekleyen öğeyi değiştirir ve `Pending` rapor sayaç artırmayan bir işarettir — dördü sevk edilen sözleşmededir (Faz 197) |
| K-927 | Kurtarma açıkken kuyruklu workflow run'ının işi `2 + MaxResumeAttempts` deneme alır; işin son denemesi devam BAŞLATMAZ, zinciri kapatır (`tracon.job.workflow-run-not-continued`); batch işi kendi sınırını korur ve öğe başına en çok `MaxAttempts − 2` devam alır; kapalıyken `AsyncRun.MaxAttempts`; run kendi başına `Failed`/`Canceled` biterse iş tekrar denemeden düşer; kurtarma adımındaki `store` hatası işi değil denemeyi düşürür (Faz 197, Açık Soru 4-A ve denetim 🔴1 düzeltmesi) (kullanıcı kararı) 👤 |
| K-928 | Yapılı `tracon.workflow` batch öğesi `sessionId` taşıyabilir: run'ın yürütme oturumudur ve `run.completed` webhook'u ile devamlar onu taşır; kural `WorkflowSessionId`'dir (en çok 128 karakter; harf, rakam, `-`, `_`), bozuk değer öğeyi tek başına düşürür; ajan batch öğesindeki `sessionId` öğeyi düşürür (Faz 197 §197.3) |
| K-929 | Kuyruklu `respond`'un yanıtı (`approved`, `text`, `json`) `jobs.payload`'da düz durur; K-899'un kuyruklu workflow işlemine genişlemesidir (Faz 197) |
| K-930 | Her konuşma çağrısı (TTS, STT, ses listesi) sağlayıcıya gitmeden önce `ISpeechProviderResolver`'da kiracının egress politikasından ve kendi anahtarından geçer: egress kayıt adına değil örneğin `ProviderName`'ine uygulanır; kiracının bağlaması varsa anahtar yalnız `SupportsTenantCredentials` diyen sağlayıcıya `SpeechRequest.Credential`/`SpeechTranscriptionOptions.Credential` ile gider, desteklemeyen sağlayıcı, uç adresi override'ı ve değersiz anahtar reddedilir (`TenantProviderRefusedException`, uçta `403`), kurulum anahtarına düşülmez; kiracı önce run kapsamından okunur; tool, HTTP ve düğüm her çağrıda, konuşma soketi bağlantı başına bir kez (oturum kapısından sonra, slot'tan önce) denetler; yerel motor muaf değildir ve politikalı kiracı listede olmayan ses satıcısına hemen çıkamaz (F-298, Faz 198; Açık Soru 2-A, 3-A, 4-A) (kullanıcı kararı) 👤 |
| K-931 | Konuşma sağlayıcıları adla kaydolur ve adla seçilir: `AddSpeechSynthesizer(name, factory)` / `AddSpeechTranscriber(name, factory)` (Core, `ITraconBuilder` uzantısı) keyed singleton yazar, anahtar çıplak ad değil iç kayıt nesnesidir; ad `[a-z0-9._-]{1,64}`, küçük harfe katlanır, sentezleyici ve tanıyıcı ayrı ad alanıdır, çift ad kurulumda `InvalidOperationException`; `ISpeechProviderResolver` (Abstractions, `AddTracon()` `TryAdd`) adı örneğe çevirir, `null` adsız varsayılandır; motoru Tracon değil çağıran seçer (düğüm ayarı, run parametresi, `POST /api/voice/speak` `provider`, `GET /api/voice/voices?provider=`), kiracı varsayılan motor satırı ve migration yoktur; tool'lar ve konuşma soketi varsayılanı kullanır; `GET /api/voice/providers` kayıtları listeler (F-298, Faz 198; Açık Soru 1-A, 5-A) (kullanıcı kararı) 👤 |
| K-932 | Konuşma çağrısı onu yapan sağlayıcı ve modelle fiyatlanır: `SpeechAudio.ModelId`/`SpeechTranscript.ModelId` gerçekte kullanılan modeli taşır (yerleşik istemci doldurur), model sırası bildirilen → istekteki → yalnız varsayılan sağlayıcının adı için yapılandırılmış model (yerleşik istemcide kendi varsayılanı); `IVoicePricingReader.ForSynthesis(providerName, modelId, characters)` ve `ForTranscription(…)` varsayılan üyedir (`null`); `/speak` adsız istekte `null` dönen yeni üyeden sonra eski `ForCharacters`'a düşer (F-298, Faz 198) |
| K-933 | Süreç içinde run başlatan her yüzey drain sırasında run'ı reddeder: ajan ve workflow run/resume/respond uçları (kuyruklu dahil) ve replay `503` (ProblemDetails), `/v1/chat/completions` ve `/v1/responses` `503` + OpenAI zarfı `type: service_unavailable`, ses sohbet soketi upgrade öncesi `503`, MCP `tools/call` `isError` sonucu, A2A çağrı hatası; yalnız job yazan yüzey (trigger, eval, schedule, onay) kabul eder, job bekler (F-431) (kullanıcı kararı) 👤 |
| K-934 | Veri sahibi dışa aktarımı korumalı sütunu düz metin okur (`DataSubjectTargetRegistry.ProtectedExportColumns`); `workflow_checkpoints.state` korumalı sütun olur: `ProtectedColumn.WorkflowCheckpointState = 11` (eklemeli public üye, `Columns` varsayılanı bütün üyeleri alır), migration yok, eski satır okunur (F-401) (kullanıcı kararı) 👤 |
| K-935 | Veri sahibi dışa aktarımı ve silmesi dört tabloya daha ulaşır, öznenin run'ıyla (adlı run ya da adlı oturumun run'ı) eşlenir: terfi edilmiş `eval_cases` (`source_run_id`, kiracı suite'ten), `idempotency_keys` (`run_id`), `job_items` (`run_id`, kiracı job'dan; batch işinin öteki öğeleri kalır), `jobs` (kimliği run kimliği olan kuyruk işi; öğeleri basamaklı gider); dördü `Runs`'tan önce silinir (F-400) (kullanıcı kararı) 👤 |
| K-936 | Bilgi tabanının her embedding çağrısı (yükleme, arama ucu, `search_knowledge`) metin süreçten çıkmadan kiracının egress politikasından ve kendi anahtarından geçer (`EmbeddingProviderAccess`): sağlayıcı adı `TraconKnowledgeOptions.EmbeddingProvider` (`Tracon:Knowledge:EmbeddingProvider`, eklemeli public üye), yoksa üretecin bildirdiği ad; ad yoksa politikalı kiracı reddedilir (fail-closed), politikasız kiracı geçer; kiracının o sağlayıcı için bağlaması varsa ret (üretici kurulum anahtarıyla kurulu, K-912 emsali); uçta `403`, tool çağrısı düşer; politika her çağrıda okunur (F-416) (kullanıcı kararı) 👤 |
| K-937 | `functions` (workflow) ve `handler-keys` (schedule) ayrılmış addır: kayıt `400` döner, büyük/küçük harf ayrımsız (ASP.NET Core rota literali de ayrımsızdır); `AddWorkflow` aynı adda `ArgumentException` atar; derleyici adı denetlemez, kural öncesi saklanan workflow adla koşmaya devam eder, yalnız geri okunamaz; sabit uçlar yerinde kalır (F-382) (kullanıcı kararı) 👤 |
| K-938 | Akışlı workflow uçları (`run`, `resume`, `respond`) girdiyi ve kaynak run'ı akış açılmadan denetler; kodlar kuyruklu yolun (F-318) kodlarıdır: olmayan ya da başka kiracının run'ı `404` (aynı başlık, varlık sızmaz), geçersiz `sessionId`, run'da olmayan checkpoint ve girdi beklemeyen run'a yanıt `400`; hiçbiri run satırı açmaz (F-396) |
| K-939 | Gerçek veri sahibi silmesi öznenin `Running` ya da `Queued` run'ı varken `409` döner; problem `activeRunIds` uzantısını taşır, ön izleme aynı listeyi `DataSubjectErasureResult.ActiveRunIds`'de verir (eklemeli public üye, `init`); `?force=true` yine siler (çökmüş sürecin bıraktığı satır için); run'lar adlı run'lar ve adlı oturumların run'larıdır, kiracıyla süzülür (F-400) (kullanıcı kararı) 👤 |
| K-940 | `AddEvaluatorJudge` ile köprülenen MEAI evaluator'ının model çağrıları yerleşik yargıç gibi (K-328) kaydedilir: her yargı bir `judge:{name}` run'ı (`RunKind.Eval`, model kimliği ve sağlayıcısıyla) açar, çağrıların kullanımını sayan sarmalayıcı istemciden toplam kullanım, maliyet ve yargıç maliyeti metriği yazılır; atan evaluator run'ı `Failed` kapatır ve yine atar; mağaza `null` ise run yazılmaz (F-361) (kullanıcı kararı) 👤 |
| K-941 | Retention arşivi korumalı sütunu `IArchiveSink`'e düz metin verir (`RetentionTargetRegistry.ProtectedArchiveColumns`, dışa aktarımın K-934 deseni); arşivin at-rest şifrelenmesi sink'in işidir; silinen satır ile arşivlenen satırın eşleşme kuralı değişmez (F-447) (kullanıcı kararı) 👤 |
| K-952 | Çok konuşmacılı diyalog ayrı bir yetenek arayüzüdür (`ISpeechDialogueSynthesizer`) ve yalnız `ISpeechProviderResolver.GetDialogueSynthesizerAsync(name, ct)` ile alınır: çözümleyici yeteneği kayıtlı sentezleyici örneğinde okur (sarmalayıcıda değil); yeteneği taşımayan motoru kayıt adı ve `ProviderName` ile reddeder ve yerleşik istemciye düşmez; K-930 kapısını diyaloğun `ProviderName`'ine uygular; kiracı anahtarını yalnız `SupportsTenantCredentials` (varsayılan `false`) diyen motora `SpeechDialogueRequest.Credential` ile iletir. Yetenek DI'a kaydedilmez. Çözümleyici üyesi soyuttur. Sınırı aşan istek ağdan önce reddedilir; Tracon bölmez ve birleştirmez. Diyalog modeli `VoiceOptions.DialogueModelId`'dir, `SynthesisModelId` kullanılmaz. HTTP ucu ve tool yoktur (F-296, Faz 203; Açık Soru 1-A, 3-A, 4-B, 5-B, 7-B) (kullanıcı kararı) 👤 |
| K-953 | Konuşma isteği sağlayıcıya özgü ayarları önekli sözlükle taşır (`SpeechRequest.ProviderSettings`, `SpeechDialogueRequest.ProviderSettings`, boş varsayılan). Kural `ModelProviderSettings.Validate`'in yeni sözlük aşırı yüklemesidir; `ModelBinding` sürümü aynı kodu koşar. Tip okuyucuları `internal` kalır ve `Tracon.Voice` `InternalsVisibleTo` alır. Bilinmeyen, başka önekli, yanlış tipli, aralık dışı ve diyalogda etkisiz anahtar ağdan önce reddedilir. `speak` tool'u ve `POST /api/voice/speak` ayar taşımaz (F-296, Faz 203; Açık Soru 2-A, 6-B) (kullanıcı kararı) 👤 |
| K-954 | Sağlayıcı ayarının reddi anahtarı ve kabul edilen değerleri adlandırır, reddedilen değeri asla yazmaz: konuşma ayarları, Anthropic seçim ayarları ve düşünme bütçesi, Google güvenlik eşiği ve düşünme bütçesi. `ModelProviderSettingsContract` metin `InvalidSettingValue` için bunu doğrular; değeri yankılayan üçüncü taraf sağlayıcı bu senaryoda düşer (F-296, Faz 203; Faz 204 devri) (ana oturum varsayılanı, bakımcıya bildirildi) |
| K-955 | Konuşma ve diyalog isteğinin ağdan önceki reddi (sınır, boş tur, sessiz tur kimliği, ayar ihlali) `TraconException`'dır, `ArgumentException` değildir; `null` istek `ArgumentNullException` kalır (F-296, Faz 203 planından sapma) (ana oturum varsayılanı, bakımcıya bildirildi) |
| K-956 | `SpeechAudio.ProviderRequestId` sağlayıcının üretim kimliğini taşır (yerleşik istemcide `request-id` başlığı, tek ses ve diyalog yolu); `elevenlabs.previousRequestIds` (≤ 3) bu kimliklerle parçalar arası süreklilik kurar (F-296, Faz 203 planından sapma) (ana oturum varsayılanı, bakımcıya bildirildi) |
| K-957 | Yerleşik ElevenLabs istemcisi kendisine doğrudan verilen kiracı kimliğini sentez, akışlı sentez, diyalog ve tanımada `NotSupportedException` ile reddeder; kurulum anahtarıyla çağırmaz (F-296, Faz 203, kusur-giderme; K-930 Açık Soru 4-A'nın uygulama düzeyi) |
| K-958 | Fonksiyon düğümü süresi: kayıtta `WorkflowFunctionOptions.Timeout` (yeni `Action<>` aşırı yüklemesi), düğümde `WorkflowNodeReference.Timeout`; aşım run'ı `Failed` + `workflow_function_timeout` bitirir; süre deneme başınadır (F-303) (kullanıcı kararı) 👤 |
| K-959 | Agent düğümü `timeout`'u yalnız `nodes` zincirinde o adımın `ChildDeadline`'ını ezer; konsol süre için biçimi `nodes`'a çevirmez, K-901 değişmez (F-405) (kullanıcı kararı) 👤 |
| K-960 | Tanım `RunTimeout`'u kurulum değerini kısaltır, `MaxRunTimeout` tavanına kadar uzatır; tavan aşımı kayıtta `400`; süre slot beklemesini sayar (F-405) (kullanıcı kararı) 👤 |
| K-961 | Job süresi: `MaxDuration` alanı + `jobs.max_duration_ms` sütunu + `MaxDurationByHandlerKey` haritası; aşan deneme `tracon.job.max-duration-exceeded` ile başarısızdır ve yeniden kiralanır (F-323) (kullanıcı kararı) 👤 |
| K-962 | Job iptali handler token'ına bir `PollInterval` içinde ulaşır: üç kaynaklı token, sonuç kaynaktan okunur, işçi çalışan job'u yoklar (F-323) (kullanıcı kararı) 👤 |
| K-963 | Workflow işinde süre aşımı işin sonudur: run `job_max_duration_exceeded` ile kapanır ve kurtarma onu sürdürmez; 13-A, 7-A'dan önce gelir (F-323) (kullanıcı kararı) 👤 |
| K-964 | Onaylanan skill script'i public `ISkillScriptExecutor` seam'ine gider (singleton · beklenen kiracı · teslim garantisi yok); kapılar, içerik pini, `script.run` denetimi, kota, `Timeout + 5 sn` bekleme sınırı ve çıktı sınırı runner'da kalır, yürütücü yalnız yeri seçer; yerel süreç `UseSkillScripts`'in işaretli varsayılanıdır (`TryAddTraconDefault`, `RequireCustomBinding` reddeder); `SkillScriptExecutionResult` `Duration`'sız `Abstractions`'a taşındı, süreyi runner ölçer, tutarsız/`null` sonuç çağrıyı düşürür (Faz 202, F-345) |
| K-965 | Harici yürütücüde de `PlatformIsolationAcknowledged` zorunludur ve K-861 (çok kiracılı host'ta stored grant için platform yetkisi) değişmez; yürütücü yalıtım beyan etse de gevşemez (Faz 202, Açık Soru 1-A ve 2-A) (kullanıcı kararı) 👤 |
| K-966 | Disk script'i de yürütücüye gider: içeriği runner kotadan sonra okur ve istekte taşır, kardeş dosyalar taşınmaz (`SourcePath` yalnız yerel yürütücü içindir); kota harici yürütücüde de uygulanır; bekleme toleransı seçenek değil sabit 5 sn'dir (Faz 202, Açık Soru 3-A, 4-A, 5-A) (kullanıcı kararı) 👤 |
| K-967 | R8 "varsayılan yürütücüyle" diye daralır: script yalnız yerel yürütücüde Tracon'un işletim sistemi kimliğiyle çalışır; B7'nin adı ("Script execution gates") ve `SECURITY.md`/politika kapsam satırı değişmez; site ve XML "sandbox" vaadi vermez, harici yürütücünün yalıtımı host'undur (Faz 202) |
| K-968 | `MaxScriptContentLength` disk script'ine de uygulanır; sınırı aşan, boş, yok ya da düzenli dosya olmayan (FIFO, aygıta bağlantı) disk script'i dosya AÇILMADAN meta veriden reddedilir; okuma havuz iş parçacığında, yürütücüyle aynı bekleme sınırı altında ve sınır+1 baytta durur (Faz 202 denetimi) (kullanıcı kararı) 👤 |
| K-969 | Stored script adı ve disk script'i boyut/tür kapısı denetimden ÖNCE kapıdır (`script.denied`, `script.run` yok, kota slotu yok); kapıdan sonraki okuma hatası (`could not be read`, `in time`, sınır aşımı) `script.run`'dan sonra tool hatası olarak kaydedilir (Faz 202) |
| K-970 | Modele ve `tool_invocations`'a dönen script metinleri: zaman aşımı `The script timed out and was stopped.`, bekleme sınırı ayrı metin ve `Timed out; the script executor did not return in time.`; kesme işaretini yalnız runner sonuna ekler (`[Tracon: output truncated at the N-byte limit per stream.]`) ve her akışı yeniden keser; yürütücü istisnasında yalnız tip yazılır, mesaj sunucu log'una gider (Faz 202) (kullanıcı kararı) 👤 |
| K-971 | `SkillScriptExecutionRequest` ortam değeri taşımaz; script'e `TRACON_SKILL_NAME` ve çağrıya özel `TRACON_SKILL_TEMP` verilir; `ContentSha256` grant pini değil içeriğin UTF-8 SHA-256'sıdır; `ToString` `Content` ve `ArgumentsJson`'ı basmaz (Faz 202) |
| K-972 | Token dışı tutar (düğüm kalemi, tool ölçümü) `RunNonTokenCost` adlı AYRI toplamdır ve `RunCost.Total()`'a girmez; token tutarıyla toplayan TEK yer `RunSpend.Total`'dır, para kararı veren beş yer yalnız onu kullanır; yalnız `Tracon:Pricing:Currency` cinsinden tutar toplanır, kalan kalem sayılır; K-219'u daraltır (Faz 199) (kullanıcı kararı) 👤 |
| K-973 | `WebhookRunSummary.Cost` token ve bildirilen token dışı tutarı birlikte taşır, `NonTokenCost` yalnız bildirilen payı verir; `IRunStore` iki üye alır (GA öncesi kırılma); `GET /api/runs/{runId}/usage` run'ın kendi kalemlerini döner (Faz 199) (kullanıcı kararı) 👤 |
| K-974 | Token dışı harcamanın kalıcı biçimi: `run_non_token_usage` tablosu (kalem anında yazılır), `runs` üzerinde dört snapshot sütunu (run kapanırken), `tool_invocations`'ta sağlayıcı/model; kapanıştan sonra gelen kalem yalnız detaya; kalem sayısı ve değer sınırları kabulde denetlenir (Faz 199) (kullanıcı kararı) 👤🔁 |
| K-975 | Düğüm listesinin ilk düğümü `object` alamaz: kayıt `400`, saklı tanım düğümden önce `Failed`; `TurnToken` her başlangıç ve cevapsız resume'da gider (F-398, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-976 | Para birimi karışık toplam tutar taşımaz: ağaç, istatistik, zaman serisi, deney kolu ve yargıç maliyetinde tutar `null`, public `CurrencyCount` sayar; kural `CurrencyTotals`'ta (F-419, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-977 | Cevaplanamayan onayda run beklemez: Chat Completions ve oturumsuz yerel run `409` (akışta error frame), MCP/A2A dahil run `Failed` (`approval_unanswerable`) (F-448, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-978 | Kurtarma açıkken işçi durunca `Canceled` kapanan workflow run'ı checkpoint'ini `KeepCheckpointsAfterCompletion=false` olsa da tutar; devam bitince temizler (F-433, kusur-giderme 2026-10-14) (ana oturum varsayılanı) |
| K-979 | Kuyruklu turun onay yazımı kira penceresinde tekrarlanır, kimlikle önce okur; pencere biterse `tracon.job.approval-save-failed`, model tekrar çağrılmaz (F-391, kusur-giderme 2026-10-14) (ana oturum varsayılanı, K-903 emsali) |
| K-980 | Çağıranı bekleyen yanıt (onay isteği, sonuçsuz istemci tool çağrısı) yapılandırılmış doğrulamadan geçmez; doğrulayıcı çağrılmaz (F-350, kusur-giderme 2026-10-14) (ana oturum varsayılanı) |
| K-981 | Katalog ve doğrulayıcı skill'i kod ve `store`'dan sonra `SkillRoots`'ta da arar; disk skill'i kurulum genelidir, içeriği önbellek parmak izine girer; grant pini diske bakmaz (F-481, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-982 | Tekrar aynı cevabı alacak hata işi o denemede düşürür: karar hatanın kararlı kimliğinden verilir (`output_truncated`, `tenant_provider_refused`, kalıcı 4xx `upstream_error`), metninden değil; işçi geneli, tüketicinin kendi ham hatası tekrar dener (F-429, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-983 | Kiracının sağlayıcı reddi tek public tiptir ve kendi sınıfını alır: `TraconTenantProviderRefusedException`, `tenant_provider_refused`, `RunErrorClass.TenantProviderRefused` (17); model, yedek, özetleyici, görsel, ses, embedding ve canlı ses aynı tipi atar, derleme tipi ajan adıyla korur (F-442, F-444, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-984 | Normalleştirilmiş sağlayıcı hatasının ikinci daraltması: `(HTTP 429)` ile biten `upstream_error` `RateLimited`'dır; durum `HttpRequestException.StatusCode`'dan da okunur (F-353, kusur-giderme 2026-10-14) (ana oturum varsayılanı) |
| K-985 | Anthropic'in iki bitişi: bağlam penceresi kesilmesi public `TraconFinishReasons.ContextWindowExceeded` ile kesilmedir ve nedenini adlandırır; `pause_turn` blokları API'nin JSON'uyla geri gönderilerek en çok 5 kez sürdürülür, sonra çağrı `AnthropicTurnPausedException` ile düşer (F-426, kusur-giderme 2026-10-14) (kullanıcı kararı) 👤 |
| K-986 | SQL deposunun işlemi tek sınırdan açılır (`SqlStoreContext.ExecuteInTransactionAsync`); deadlock kurbanı işlemin tamamıyla, yeni bağlantı ve işlemle, en çok 5 kez rastgele beklemeyle yeniden koşar (F-430, kusur-giderme 2026-10-14) (ana oturum varsayılanı, K-540 emsali) |
| K-987 | Workflow tanımı sürüm geçmişi tutar (K-126 açıldı): `workflow_definition_versions`, yabancı anahtarsız, numara kimliktir ve yeniden verilmez; kayıt iki satırı tek işlemde yazar; `IWorkflowDefinitionStore` üç soyut üye alır; geri alma yeni sürümdür (F-300) |
| K-988 | Workflow kök satırı derlediği sürümü `runs.agent_version`'a yazar; `resume`/`respond` kaynağın damgasını derler, `definitionVersion` başka sürüm seçer, olmayan sürüm kapalı düşer (F-300) |
| K-989 | `WorkflowNodeReference.AgentVersion`: yalnız `nodes` biçiminde veritabanı agent'ı sabitlenir; kayıtta ve derlemede doğrulanır; sabit önbellek anahtarındadır, executor kimliğinde değil (F-300) |
| K-990 | Kota girdisi ağaç başına değil harcayan run başına yazılır: kök (`Runs = 1`), alt agent run'ı (`Runs = 0`), workflow kökü ve kapanıştan sonra dönen tool kendi token ve tutarını, kapanırken, kökün kiracısı ve adı altında yazar; kimlik ortamdan değil ağacın bütçesindeki `TreeAccount`'tan okunur ve tek yazım yolu `TreeQuota.RecordAsync`'tir; `run.*` webhook'u kök kapanış anının görüntüsü olarak kalır; eşik bildirimi eşiği geçiren run'ın akışına yazılır; K-913'ün kota yarısını ve K-974'ün "yalnız detaya" kuralının kota yarısını değiştirir (F-418, F-482, Faz 205) (kullanıcı kararı) 👤 |
| K-991 | Ajan run akışı run'ın kayıtlı ayrılmış bildirimlerini `done`'dan hemen önce `custom` çerçevesi olarak verir: kapalı liste (`tracon.output.truncated` her zaman, `tracon.quota.threshold` yalnız `PublishThresholdToRunStream` açıkken), veri `/events` ile bayt bayt aynı kayıttır (yeniden okunur, ikinci kopya kurulmaz); depo yalnız kota seçeneği açıksa ya da bir `update` kesilme sebebi taşıdıysa okunur; okuma hatası uyarı + `done`'dur; listeye tip eklemek sözleşme kararıdır (F-427, Faz 207) (kullanıcı kararı) 👤 |
| K-992 | `/v1/chat/completions` `finish_reason` yanıtın son model çağrısının sebebinden gelir: `Length` ve `TraconFinishReasons.ContextWindowExceeded` → `"length"`, içerikli `ContentFilter` → `"content_filter"`, geri kalanı (`ToolCalls` dahil) → `"stop"`; iki kolda, run kaydından bağımsız (F-427, Faz 207) (kullanıcı kararı) 👤 |
| K-993 | Okunamayan yapılandırma girdisi atlanmaz, açılışta hata verir: `Quotas:ThresholdPercents` ve `Webhooks:RetryDelays` girdisi sıfır, `ContentProtection:Columns` girdisi tanımsız değer, `MaskedPii` tanımsız bit olarak bağlanır, `ContentProtection:Keys` boş adı yazıldığı gibi kalır; ham metin `internal` taşıyıcıda durur ve mesaj onu alıntılar; hata bağlanmış değerden çıkar (bağlamadan sonra yazan kod kazanır); `Keys`/`Columns` denetimi `Enabled`'dan bağımsızdır; tanımsız `ProtectedColumn`/`PiiPatterns` kod yolundan da reddedilir; boş `MaskedPii` "yok"tur (F-247, Faz 206) (kullanıcı kararı) 👤 |
| K-994 | Workflow başına cevap modu: `WorkflowResponseMode.Branch` (varsayılan, K-130 korunur) ve `SingleAnswer`; tek cevapta iddia `(tenant, session, requestId)` kapsamındadır, iki cevap `approved`/`text`/`data` SHA-256'sı eşitse aynıdır (checkpoint ve tanım sürümü sayılmaz), aynı cevap ilkinin başlattığı işi izler (akışlı SSE depodan, kuyruklu aynı `202`), farklı cevap `409` alır ve ne run'ı ne öbür cevabı adlandırır; iş cevabı almadan durduysa (zincir `Failed`/`Canceled`, kuyruk işi bitmiş, kayıt `Accepted` ve tek run'lık zincir ya da zincirde checkpoint yok) aynı cevap işi yeniden başlatır; bekleyen run kaydettiği tanım sürümünün moduyla cevaplanır (F-319, Faz 208) (kullanıcı kararı) 👤 |
| K-995 | Cevap kaydı ve kaynak bağı: `workflow_responses` tablosu (üç sağlayıcı; kısmi benzersiz indeks `(tenant_id, claim_scope, request_id) WHERE claim_scope IS NOT NULL AND status <> 2`; `source_run_id` → `runs` `ON DELETE CASCADE`), `IWorkflowResponseStore` + `WorkflowResponseStoreContract`, `GET /api/workflows/runs/{runId}/responses` (`Reader` + `RunAccess.Read`); devam run'ı `RunEventType.WorkflowResponseApplied` (33, `workflow.response-applied`) olayını `RecordToolPayloads` kapalıyken de yazar; kayıt yalnız veri sahibi silmesiyle kaynak run'la gider, hiçbir saklama hedefi silmez (F-319, Faz 208) (kullanıcı kararı) 👤 |
| K-996 | Fail-closed denetim kümesi 6 → 7: cevap modu ne olursa olsun run başlatan her `POST /api/workflows/runs/{runId}/respond` `workflow.respond` olarak run satırından ve kayıttan ÖNCE `AuditRecorder.WriteOrThrowAsync` ile yazılır, yazılamazsa cevap `500` ile reddedilir; kayıt cevabın hash'ini taşır, cevabı değil; run başlatmayan cevap (izleme, `409`) yazılmaz; K-776'nın yayımlanmış kümesini değiştirir (F-319, Faz 208) (kullanıcı kararı) 👤 |
| K-997 | Önce satır, sonra kayıt: cevabın run satırı (akışlıda `Running`, kuyrukluda `Queued`) her modda ve her yolda kayıttan/iddiadan önce açılır, bir kayıt hiçbir zaman var olmayan bir run'ı adlandırmaz; iddiayı kaybeden kendi run'ını olaysız `Canceled` + `request_already_answered` kapatır (run listesinde ve istatistikte görünür), kayıt yazılamazsa run `Failed` + `answer_not_recorded` kapanır ve cevap `500` alır; atıf karardan önce bir kez okunur, satırdan sonraki her yazım `CancellationToken.None` kullanır; kuyruk işi bitmemişse kapı izler; işi hiç yazılmamış kuyruklu run'ın işini aynı cevabın tekrarı yazar (F-319, Faz 208) (kullanıcı kararı, 2026-10-14) 👤 |
| K-998 | `IJobStore.EnqueueAsync` kayıtlı bir iş kimliğini reddeder: çağrı hata verir, kayıtlı iş ve öğeleri değişmez; bellek içi depo işi değiştiriyordu, artık reddeder; `QueuedRunPlaceholder` reddedilen yazımdan sonra işi geri okur ve oradaysa onu döndürür, satırı kapatmaz; `JobStoreContract` yeni case'le dört depoda zorlar (Faz 208) |
| K-999 | Fonksiyon düğümü, ağaç bütçesinin MALİYET veya SÜRE boyutu geçilmişse başlamaz; token ve run sayısı durdurmaz; ret, reddedilen agent adımıyla aynı kimliği taşır (`Failed`, `run_budget_exceeded`, `QuotaExceeded`); kapı her denemeden önce koşar ve reddi tekrar denenmez; muafiyet yalnız kodda: `WorkflowFunctionOptions.StartsWhenBudgetIsSpent` (varsayılan `false`) (F-407, Faz 210) (kullanıcı kararı) 👤 |
| K-1000 | Bekleyen insan istekleri için `workflow_requests` projeksiyonu açıldı; istek yükü (istem, form) olayda KALIR, satır olayı sırasıyla (`event_seq`) adlandırır ve istemi kopyalamaz; run `AwaitingInput` kapanırken `WorkflowRunner.CompleteAsync`'te yazılır, yazılamazsa run etkilenmez (uyarı) ve istek run'ın kendi listesinden hâlâ cevaplanır; üç sağlayıcıda tablo (`(run_id, request_id)` birincil anahtar, `run_id` → `runs` `ON DELETE CASCADE`), `IWorkflowRequestStore` + `WorkflowRequestStoreContract`, bellek içi varsayılan; Faz 209 öncesi bekleyen run'lar için doldurma yoktur (F-319, Faz 209) (kullanıcı kararı) 👤 |
| K-1001 | Kiracı listesi `(oturum, requestId)` başına BİR mantıksal istektir: o isteği tutan en yeni AÇIK satır listelenir (yeniden yayınlayan devam run'ı), süzgeç (`state=pending|answered|all`, `workflowName`) sayfadan ÖNCE uygulanır, sıra `RequestedAt` eskiden yeniye; "yanıtlandı" işareti `IWorkflowRequestStore.MarkAnsweredAsync` ile `WorkflowResponseGate` kayıttan sonra (üç yolda) yazılır ve aynı oturum+istek kimliğindeki bütün açık satırları işaretler, yazılamazsa cevap etkilenmez (uyarı); `GET /api/workflows/runs/{runId}/requests` aynı beş alanı taşır ama okuma bu bağımlılıkta düşerse alansız döner (F-319, Faz 209) (kullanıcı kararı) 👤 |
| K-1002 | Workflow başına istek süresi: `WorkflowDefinition.RequestTimeout` (`TimeSpan?`, `null` = süresiz; `PUT /api/workflows/{name}` `requestTimeout`; kod workflow'unda `WorkflowRegistrationOptions.RequestTimeout`), 1 dakika – 365 gün, `RequestedAt`'ten sayılır ve YAZIM anında satırın `expires_at`'ine sabitlenir: bekleyen run'ın tanımı sonradan değişirse süresi değişmez; devam run'ı isteği yeniden yayınlarsa o run kendi sayacını açar; konsolda tam-değiştirme editörü alanı her kayıtta geri gönderir (F-319, Faz 209) (kullanıcı kararı) 👤 |
| K-1003 | Süre dolunca kapanış: `WorkflowRequestExpirationService` (`SingletonGuard`, `RequestExpirationScanInterval` 1 dk, `MaxRequestExpirationsPerScan` 100) önce `ExpireAsync` ile satırı kapatır, sonra run'ı son açık satırı kapanınca kapatır: cevapsız bir istek varsa `Failed` + `WorkflowRequestExpired` (sınıf `Timeout`), hepsi cevaplıysa hata taşımayan `Completed`; run satırını OKUYUP olay sayısını, kullanımı, maliyeti geri yazar (`CompleteRunAsync` bunları değiştirir) ve yalnız hâlâ `AwaitingInput` olan run'ı kapatır; kapatılamayan run aynı süreçte her turda 10 kez denenir ve süreç satırı kapatıp run'ı kapatmadan dururse lease'i alan sonraki örnek `IWorkflowRequestStore.ListStrandedAsync` ile (kapalı satırı olan, açık satırı kalmamış, hâlâ `AwaitingInput` run) onu bulup kapatır; cevaplanıp bekleyen run K-130'un "durum geriye değişmez" cümlesine istisnadır (onay süresi aynısını `AwaitingApproval` için yapar) (F-319, Faz 209) (kullanıcı kararı) 👤 |
| K-1004 | `RetentionTargets.WorkflowRequests` (`workflow_requests`): yalnız KAPALI satır yaşına (`closed_at`) göre silinir, açık satır silinmez ve satır sınırına sayılmaz; varsayılan 30 gün (`Tracon:Retention:WorkflowRequests`), kullanıcı verisi değildir; süre dolup run `AwaitingInput` olmaktan çıkınca K-888'in üç muafiyeti (istek olayı, checkpoint, girdi satırı) bırakır ve bir sonraki turda silinir; `RetentionTargetRegistry` yorumu "an expiry for them is F-319's scope" kalktı (F-319, Faz 209) |
| K-1005 | Kiracı listesi `GET /api/workflows/requests` `Reader` + `RunAccess.Read` ister; run okumayı reddeden `IRunAuthorizationHandler` listeyi `403` ile REDDEDER, süzmez (K-671); `requests` adı `functions` gibi ayrılmış workflow adıdır (rota `/api/workflows/{name}`'den önce eşleşir) (F-319, Faz 209) (kullanıcı kararı) 👤 |
| K-1006 | Cevabı okuyan akışlı yüzey Loop'lu agent'tan yalnız son iterasyonu alır (`AgentAnswerStream`); native agent SSE'si her iterasyonu akıtır (F-536, K-889 uzantısı) 🔁 |
| K-1007 | Kod grafında düğüm yetenekleri iki public yardımcıyla verilir: `GetWorkflowFunction(ad[, Action<WorkflowFunctionStepOptions>])` kayıtlı fonksiyonun SAKLANAN ZİNCİRLE AYNI sarmalanmış executor'ını, `GetWorkflowAgentStep(workflow, agent[, Action<WorkflowAgentStepOptions>])` agent'ı düz bir `FunctionExecutor` adımı olarak döner; executor kimliği fonksiyon veya agent adıdır (bir grafta bir kez), `ExecutorBinding` sarmalanmaz; bilinmeyen fonksiyon ve okunamayan ayar run'ı ilk düğümden ÖNCE `Failed` bitirir; `AgentVersion` sabiti graf kurulurken değil adım koşarken, model çağrısından önce denetlenir; `GetWorkflowAgent` değişmez (F-537, Faz 211) (kullanıcı kararı) 👤 |
| K-1008 | Workflow agent adımı (saklanan karma zincir ve `GetWorkflowAgentStep`) agent'tan akışsız cevap ister (`agent.RunAsync`): yapılandırılmış yanıt onarımı adımda çalışır, adımın alt run satırı `IsStreaming = false` yazar ve `message.completed` olayı fazladır (F-537, Faz 211) (kullanıcı kararı) 👤 |
| K-1009 | `WorkflowFunctionOptions.MaxConcurrency` (`int?`, en az 1) fonksiyon başına, SÜREÇ başına ve kiracılara ortak slot sayısıdır; sarmalayıcı sırası `scope(retry(limit(gate(timeout(handler)))))`: her deneme slotu yeniden alır, retry gecikmesinde slot bırakılır, bütçe kapısı slot alındıktan sonra okunur, bekleme run token'ına bağlıdır (iptal ve `RunTimeout` bekleyeni bırakır) ve düğüm `Timeout`'una sayılmaz; bekleme run olayı yazmaz, `Debug` loglanır (F-538, Faz 211) |
| K-1010 | Kod kaydı `WorkflowRegistrationOptions.RunTimeout` ve `Parameters` taşır: `RunTimeout` K-960'ın kuralını izler, ama tavanı aşan değer HOST AÇILIŞINDA (`TraconWorkflowOptionsValidator`, `Enabled` iken) workflow adıyla düşer, tavan sonradan düşerse run tavana kesilir ve loglanır; `Parameters` K-897'nin kuralını izler (HTTP erken `400`); katılımcı kapsaması kod grafında YOKTUR, eksik zorunlu değeri adım koşarken düşürür (F-537, Faz 211) |
| K-1011 | Süre dolumu servisi (`WorkflowRequestExpirationService`) kapattığı run `Failed` ise `run.failed` yayınlar; `Completed` kapanışta HİÇBİR olay yayınlamaz; yayın `CancellationToken.None` ile yapılır, hata loglanır ve kapanışı bozmaz (F-539, Faz 211) |
| K-1012 | HTTP istek/yanıt DTO'su `internal` doğar ve açıklamasını `[Description]` taşır: 81 tip `internal` oldu; OpenAPI şeması artık public kalma kanıtı değildir, K-850'nin ikinci kuralı geçersiz (F-554, Faz 212) (kullanıcı kararı) 👤 |
| K-1013 | Seam, bir rehberin, sample'ın veya contract suite'in tüketiciye uygulatmayı anlattığı public arayüzdür; 12 tesisat arayüzü ve yalnız onların imzasında yaşayan 23 tip `internal` oldu, MIMARI K4 "belgelenmiş genişleme noktası" olarak daraldı (F-555, Faz 213) (kullanıcı kararı) 👤 |
| K-1014 | DI'ın ve araç boru hattının kurduğu 5 tipin kurucusu `internal`, kaydı fabrika; somut sınıfın her public kurucusu `public-constructor-baseline.txt`'e gerekçeyle bağlı ve taban yalnız küçülür; `UpdateRunCostAsync` `tenantId` varsayılanı yok; `BackendModel` ×2 ve `QuotaUsageQuery.AsOf` silindi (F-556, Faz 213) (kullanıcı kararı) 👤 |
| K-1015 | `LiveVoiceDelegationMode.Responses` silindi, enum yalnız `Client`; `Tracon:Voice:Live:DelegationMode` bilinmeyen adı başlangıçta reddeder (F-556, Faz 213) |
| K-1016 | `TraconAgentSessionStore` `internal`; köprünün `GetSessionAsync`'i oturum AÇMAZ; anahtarın `isolation` bölümü eski önek biçimine (`kaçışlı-anahtar::kimlik`) katlanır, başka her bölüm `NotSupportedException` ile reddedilir; `MAAI001` bastırması yalnız köprü dosyasında ve onu kullanan iki yerde (`OpenAIResponsesEndpoints`, `ResolveSessionStore`) (F-557, Faz 214) |
| K-1017 | Bağımlılık yükseltmesi istek davranışını değiştirmez: `Tracon.Anthropic` SDK adaptörünü `AnthropicThinkingMode.Extended` ile kurar (SDK varsayılanı `Adaptive`), önbellek işaretli talimat bloğu `system` dizisinde ilk system-olmayan mesajdan ÖNCE yerleşir (F-557, Faz 214) |
| K-1018 | Sevk edilen `Tracon.Testing.Contracts.Xunit` paketi `xunit.v3.extensibility.core` ve `xunit.v3.assert` için `>= 3.2.2` alt sınırını korur; reponun kendi testleri xunit.v3 `4.0.2` + `Microsoft.Testing.Extensions.TrxReport` `2.5.1` üzerinde koşar (F-557, Faz 215, A-70) |
| K-1019 | Bir sağlayıcı veya ses paketi "Native AOT: Yes" vaadini ancak bir tüketici yayınında kanıtlarsa verir: gerçek istek/yanıt yolu sahte sunucuya karşı AOT'ta koşar ve yayın sıfır trim/AOT uyarısı verir; `Anthropic` ve `Google` vaadi geri çekildi (F-558, Faz 217, A-83) |
| K-1020 | `Tracon.Azure` Azure OpenAI'nın v1 ucuna (`{endpoint}/openai/v1/`) düz `OpenAI` istemcisiyle gider; `Azure.AI.OpenAI` bağımlılıktan çıktı; anahtar `Authorization: Bearer`, Entra kimliği `BearerTokenPolicy` ile yalnız HTTPS'te (F-558, Faz 217, A-83) |
| K-1021 | `ModelBinding.ReasoningEffort` Anthropic'te uyarlanabilir düşünmeye (`thinking.type=adaptive` + `output_config.effort`) gider; K-1017'nin `Extended` sabiti kaldırıldı (F-558, Faz 217) |
| K-1022 | `AuthorizationConfigurationKey` kalktı; `PUT /api/mcp-servers/{name}` eski alanı taşıyan gövdeyi `400` (`Removed field`) ile reddeder ve yerine geçeni adıyla söyler; kural 1.x boyunca kalır (F-290, Faz 218) (kullanıcı kararı) 👤 |
| K-1023 | Migration `authorization_configuration_key` değerini `header_configuration_keys["Authorization"]`'a taşır ve sütunu AYNI migration'da düşürür; geri dönüş yoktur (PostgreSQL `0062`, SQL Server `0050`, SQLite `0049`) (F-290, Faz 218) (kullanıcı kararı) 👤 |
| K-1024 | `WorkflowRunRequest.DefinitionVersion` public; checkpoint'siz kurtarma yeniden başlatması zincirin ilk run'ının sürümünü sabitler (F-492) |
| K-1025 | `/v1/chat/completions` görsel, ses ve dosyayı modele verir; tool geçmişi ve bilinmeyen rol `400` (F-580) (kullanıcı kararı) 👤 |
| K-1026 | OpenAI ses istemcisi `OpenAIClient.Pipeline` üstündedir, `AudioClient`/MEAI adaptörü üstünde değil (F-579) |
| K-1027 | `/v1/chat/completions` agent'ın istek ayarını `400` ile reddeder; `/v1/conversations` ilk `items`'ı reddeder (F-581) |
| K-1028 | `RunRecordingAgent` iç agent'a düz `AgentRunOptions` verir, `TraconRunOptions` değil (G-07) |
| K-1029 | Agent uzantısı MAF seçenek nesnesinin kendisini alır; korunan üye tablosu ve ratchet testi sınırı zorlar (F-588) |
| K-1030 | Uzantıyı kod kaydeder, tanım yalnız adla seçer: `AddAgentExtension`, `ExtensionNames` (F-588) |
| K-1031 | SQL payload'ında sonradan gelen liste alanı null okunabilir; boş liste yazılmaz (F-588) |
| K-1032 | Sohbet agent'ında `RequirePerServiceCallChatHistoryPersistence` serbesttir; açıksa model istemcisi sınır seam'inden geçer (F-588) 🔁 |
| K-1033 | Tool oynatan yollar uzantıyı çalıştırmaz: replay `ExtensionNames`'i boşaltır, continuation reddeder (F-588) |
| K-1034 | Tanınmayan içerik `tracon.content`, workflow olayı `tracon.workflow.event` olarak kaydedilir; yük gövdesizdir (F-589) |
| K-1035 | `UseProvidedChatClientAsIs` ve `AllowConcurrentInvocation` korunur: ikisi korunan bir ayarı dolaylı değiştirir |
| K-1036 | Deneysel upstream tip yalnız ön sürüm hattındaki uydu paketin public imzasına girer; çekirdek aile kararlı kalır (D-01) |
| K-1037 | Ön sürüm upstream'e bağlı yeni uydu paket ayrı ön sürüm hattında çıkar; mevcut aile tek hatta kalır (D-19) |
| K-1038 | `EnableMessageInjection`, `RequirePerServiceCallChatHistoryPersistence`, `RawRepresentationFactory` korunur: Tracon'da işlemez ya da tanımı ezer 👤 |
| K-1039 | Run başına `ChatOptions` HTTP'de yoktur; kod ve MAF yolu tek kuraldan geçer: tanımın üyesi reddedilir, ek talimat ve tool beyanı geçer 👤🔁 |
| K-1040 | Model istek alanları tanımdadır; sağlayıcı yüzeyi gönderdiğini `ISamplingSupport` ile bildirir, bildirmeyen alan derleme hatasıdır (F-598) |
| K-1041 | `AllowConcurrentToolCalls` `ModelBinding.ToolLoop` altına taşındı; eski yük okumada çevrilir, eski HTTP anahtarı `400` alır (F-598) 👤 |
| K-1042 | Run seçeneği `AllowMultipleToolCalls = false` geçer; `true` ve öteki korunan üyeler reddedilir (K-1039 daraltması) |
| K-1043 | Alt agent çağrı modu tanımdadır: `SubAgentSettings.CallMode` (`Background`, `Tool`); `Tool`'da ad çakışması reddedilir (F-599) |
| K-1044 | Hosted tool kodda kaydedilir, tanım adla seçer: `AddHostedTool`; tool zinciri yoktur, tür başına en çok bir tane (F-603) |
| K-1045 | Hosted tool desteğini sağlayıcı `IHostedToolSupport` ile bildirir; bildirmeyen tür derleme hatasıdır (F-603) |
| K-1046 | Kayıtlı hosted çağrı `tracon.content` değil `ToolInvoking`/`ToolInvoked` olur; kayıtsız olan K-1034'te kalır (F-603) |
| K-1047 | Hosted çağrı ücreti iç döngü halkasında, yanıt önbelleğinin içinde raporlanır; fiyatsız tür `null` kalır, `0` olmaz (F-603) |
| K-1048 | `EvalCase.ExpectedTools` yerine `ExpectedToolCalls` (ad + isteğe bağlı argüman); eski HTTP anahtarı `400` alır (F-605) 👤 |
| K-1049 | `toolCallArgsMatch` Tracon'un kendi kontrolüdür: değeri JSON anlamıyla karşılaştırır; beklenti `EvalItem`'a iki biçimde verilir (F-605) |
| K-1050 | `eval_cases.expected_tool_calls` yeni sütun; eski `expected_tools` yazılmaz, okunur, düşürülmez; okunamayan metin vakayı düşürür (F-605) |
