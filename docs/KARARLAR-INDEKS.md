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

---

## 1. Reddedilen İşler (24 kalem) — bunları yeniden önerme

| KARARLAR.md satırı | Karar | Tarih |
|---|---|---|
| L15 | Arayüz Blazor ile yazılmadı 👤 | 2026-08-01 |
| L16 | EF Core kullanılmadı 👤 | 2026-08-01 |
| L17 | `netstandard2.0` ve `net472` hedeflenmedi  | 2026-08-01 |
| L18 | Tek paket (monolitik) paketleme yapılmadı 👤 | 2026-08-01 |
| L19 | Geçişli sabitleme (`CentralPackageTransitivePinningEnabled`) açılmadı  | 2026-08-01 |
| L20 | Arayüzden tool kodu yazma özelliği eklenmedi  | 2026-08-01 |
| L21 | MAF tipleri sarmalanmadı  | 2026-08-01 |
| L22 | Trim/AOT analyzer'ları kök seviyede açılmadı  | 2026-08-01 |
| L23 | Katalog MAF'ın `AddAIAgent` kayıtlarını doğrudan okumadı  | 2026-08-02 |
| L24 | `AddToolsFrom<T>()` (attribute taramalı tool kaydı) Faz 1'de yapılmadı 🔁 | 2026-08-02 |
| L25 | Yerleşik OpenAI model listesi kodda tutulmadı  | 2026-08-02 |
| L26 | Responses API'de sunucu tarafı konuşma durumu kullanılmadı  | 2026-08-02 |
| L27 | `ValidateDataAnnotations()` kullanılmadı  | 2026-08-02 |
| L28 | Çalıştırma kaydı MAF middleware'i olarak yazılmadı  | 2026-08-01 |
| L29 | EF Core migration'ları yerine gömülü SQL — uygulandı ve doğrulandı  | 2026-08-02 |
| L30 | `tenant_id` sütunlarına yabancı anahtar konmadı  | 2026-08-02 |
| L31 | `tool_invocations` tablosu Faz 2'de doldurulmadı  | 2026-08-02 |
| L32 | MAF'ın `MapOpenAIResponses()` / `MapOpenAIConversations()` uçları kullanılmadı 👤 | 2026-08-02 |
| L33 | `/v1/conversations` ucu Faz 4'te yazılmadı 👤🔁 | 2026-08-02 |
| L34 | `/api/stats` maliyet döndürmüyor  | 2026-08-02 |
| L35 | OpenAPI paketi `AgentPrism.AspNetCore` bağımlılığı yapılmadı  | 2026-08-02 |
| L36 | `run_events` gerçekten partition'lanmadı  | 2026-08-02 |
| L37 | Arayüzden tool istatistiği ve model sağlık kontrolü Faz 5'te gösterilmedi 👤 | 2026-08-02 |
| L38 | Arayüz i18n altyapısı kurulmadı; dil İngilizce 👤🔁 | 2026-08-02 |

## 2. Kalıcı Kararlar (203 kalem)

| K | Satır | Karar | Tarih |
|---|---|---|---|
| K-001 | L46 | Modüler paket ailesi + meta paket 👤 | 2026-08-01 |
| K-002 | L47 | Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü 👤 | 2026-08-01 |
| K-003 | L48 | Hibrit agent tanımı: kod + çalışma anı veritabanı 👤 | 2026-08-01 |
| K-004 | L49 | Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner 👤 | 2026-08-01 |
| K-005 | L50 | Hedef framework `net8.0;net9.0;net10.0` 👤 | 2026-08-01 |
| K-006 | L51 | Trim/AOT uyumu katman bazlı  | 2026-08-01 |
| K-007 | L52 | Geçişli sabitleme kapalı  | 2026-08-01 |
| K-008 | L53 | Ön sürüm MAF bağımlılığı yalnız `AgentPrism.AspNetCore` içinde  | 2026-08-01 |
| K-009 | L54 | Sırlar `dotnet user-secrets` ile 👤 | 2026-08-01 |
| K-010 | L55 | Arayüz erişimi üç katmanlı 👤 | 2026-08-01 |
| K-011 | L56 | Klasör düzeni `src/` + `samples/` + `tests/` 👤 | 2026-08-01 |
| K-012 | L57 | Tool'lar yalnız kodda tanımlanır  | 2026-08-01 |
| K-013 | L58 | Ayrı `agentprism` PostgreSQL şeması  | 2026-08-01 |
| K-014 | L59 | `run_events` append-only  | 2026-08-01 |
| K-015 | L60 | Birincil anahtarlar `uuid` v7  | 2026-08-01 |
| K-016 | L61 | Public API takibi Faz 7'ye ertelendi  | 2026-08-01 |
| K-017 | L62 | Sürümleme MinVer ile git etiketinden  | 2026-08-01 |
| K-019 | L63 | Agent kaynakları `IAgentSource` ile soyutlandı  | 2026-08-02 |
| K-020 | L64 | `MAAI001` bastırması tek dosyada toplandı  | 2026-08-02 |
| K-021 | L65 | Yapılandırma elle bağlanır, `Bind()` kullanılmaz  | 2026-08-02 |
| K-022 | L66 | Çalıştırma olayı sıra numarasını yazıcı üretir, depo değil  | 2026-08-02 |
| K-023 | L67 | Kendi UUIDv7 üretecimiz (`AgentPrismId`)  | 2026-08-02 |
| K-024 | L68 | `IAgentDecorator` genişleme noktası  | 2026-08-02 |
| K-018 | L69 | Bellek içi store'lar birinci sınıf implementasyon  | 2026-08-01 |
| K-025 | L70 | `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil  | 2026-08-02 |
| K-026 | L71 | Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil 👤 | 2026-08-02 |
| K-027 | L72 | Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil  | 2026-08-02 |
| K-028 | L73 | Sağlayıcı ve depolama ayarları kendi alt bölümlerinde 👤 | 2026-08-02 |
| K-029 | L74 | Şema adı SQL metnine gömülür, katı doğrulamadan sonra  | 2026-08-02 |
| K-030 | L75 | Responses API her zaman sunucu tarafı depolama kapalı çalışır  | 2026-08-02 |
| K-031 | L76 | `OPENAI001` bastırması tek dosyada toplandı  | 2026-08-02 |
| K-032 | L77 | Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur 👤 | 2026-08-02 |
| K-033 | L78 | Tool taraması açık işaretleme ister (`[AgentPrismTool]`) 👤 | 2026-08-02 |
| K-034 | L79 | `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir 👤 | 2026-08-02 |
| K-035 | L80 | Ayar sınıfları `record` olamaz  | 2026-08-02 |
| K-036 | L81 | OpenAI uyumlu uçlar AgentPrism tarafından yazılır, MAF'ın `Map*` uçlarıyla değil 👤 | 2026-08-02 |
| K-037 | L82 | `ChatHistoryProvider` `AddAgentPrism()` içinde açıkça kaydedilir  | 2026-08-02 |
| K-038 | L83 | `/v1/*` uçları OpenAI hata biçimini kullanır, `ProblemDetails` değil  | 2026-08-02 |
| K-039 | L84 | Kütüphane OpenAPI üretimini dayatmaz, yalnız üstveri taşır  | 2026-08-02 |
| K-040 | L85 | Enum'lar JSON'da ad olarak yazılır  | 2026-08-02 |
| K-041 | L86 | Çalıştırma özeti depoda hesaplanır  | 2026-08-02 |
| K-042 | L87 | `MapAgentPrism()` yalnız korumalı grubun convention builder'ını döndürür  | 2026-08-02 |
| K-043 | L88 | Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur  | 2026-08-02 |
| K-044 | L89 | Çalıştırma kimliğini çağıran üretir (`AgentPrismRunOptions`) 👤 | 2026-08-02 |
| K-045 | L90 | Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı 👤 | 2026-08-02 |
| K-046 | L91 | Arayüz kabuğu bearer token katmanından muaftır 👤 | 2026-08-02 |
| K-047 | L92 | Arayüz token'ı `sessionStorage`'da tutulur 👤 | 2026-08-02 |
| K-048 | L93 | Arayüz varlıkları Brotli sıkıştırılmış gömülür  | 2026-08-02 |
| K-049 | L94 | `IAgentPrismUiProvider` tek metotlu tutuldu  | 2026-08-02 |
| K-050 | L95 | Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır  | 2026-08-02 |
| K-051 | L96 | `AgentPrism.UI.csproj` SDK'yı açık `Import` ile yükler  | 2026-08-02 |
| K-052 | L97 | Frontend birim testleri `npm run build` içinde koşar  | 2026-08-02 |
| K-053 | L98 | `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı  | 2026-08-02 |
| K-054 | L99 | Faz 6 kapsamı daraltıldı: Workflows ertelendi 👤 | 2026-08-02 |
| K-055 | L100 | Span'ler `ActivityListener` ile toplanır, exporter ile değil  | 2026-08-02 |
| K-056 | L101 | Örnekleme kararı çalıştırma bittiğinde verilir  | 2026-08-02 |
| K-057 | L102 | `ModelContextProtocol.Core`, tam `ModelContextProtocol` paketi değil  | 2026-08-02 |
| K-058 | L103 | MCP'de yalnızca uzak HTTP aktarımı; stdio yok  | 2026-08-02 |
| K-059 | L104 | MCP kimlik doğrulama değeri veritabanında saklanmaz  | 2026-08-02 |
| K-060 | L105 | MCP tool adları `{sunucu}_{tool}`; nokta kullanılmaz  | 2026-08-02 |
| K-061 | L106 | "Bir daha sorma" MAF'ın biçimiyle değil AgentPrism deposunda tutulur  | 2026-08-02 |
| K-062 | L107 | Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı  | 2026-08-02 |
| K-063 | L108 | `run_events` partition'ı açılmadı (kapandı → K-199)  | 2026-08-02 |
| K-064 | L109 | İkinci faz önceliği: yetenek derinliği 👤 | 2026-08-02 |
| K-065 | L110 | Ses: hedef gerçek zamanlı konuşma katmanı 👤 | 2026-08-02 |
| K-066 | L111 | Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası 👤 | 2026-08-02 |
| K-067 | L112 | Alt agent çalıştırması ayrı bir `runs` satırıdır 👤 | 2026-08-02 |
| K-068 | L113 | Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz 👤 | 2026-08-02 |
| K-069 | L114 | `UseOpenAICompatible(ad, ...)` ayrı ad, `UseOpenAI` aşırı yüklemesi değil 👤 | 2026-08-02 |
| K-070 | L115 | Model sağlayıcı devre kesici varsayılan olarak açık 👤 | 2026-08-02 |
| K-071 | L116 | Sağlayıcı sağlık denetimi arka planda varsayılan olarak koşmaz 👤 | 2026-08-02 |
| K-072 | L117 | Sağlayıcı adı deseni regex değil elle karakter denetimiyle doğrulanır  | 2026-08-02 |
| K-073 | L118 | Sağlık denetimi hata detayında `HttpRequestException.Message` değil `HttpRequestError` kategorisi kullanılır  | 2026-08-02 |
| K-074 | L119 | Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi  | 2026-08-02 |
| K-075 | L120 | Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapAgentPrism()` çağrısında bir kez yapılır  | 2026-08-02 |
| K-076 | L121 | Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil  | 2026-08-02 |
| K-077 | L122 | Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır  | 2026-08-02 |
| K-078 | L123 | `/api/meta` yanıtına rol bilgisi (`roles`) eklendi  | 2026-08-02 |
| K-079 | L124 | `mcp.refresh` denetim izine uç katmanında yazılır, depo dekoratöründe değil  | 2026-08-02 |
| K-080 | L125 | Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil  | 2026-08-02 |
| K-081 | L126 | Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler  | 2026-08-02 |
| K-082 | L127 | Skill sürüm geçmişi tutulmaz 👤 | 2026-08-02 |
| K-083 | L128 | `AllowedTools` yalnız saklanır, zorlanmaz 👤 | 2026-08-02 |
| K-084 | L129 | Bir agent için varsayılan skill sınırı 10'dur 👤 | 2026-08-02 |
| K-085 | L130 | Derlenmiş agent cache anahtarında skill parmak izi vardır  | 2026-08-02 |
| K-086 | L131 | Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz  | 2026-08-02 |
| K-087 | L132 | Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir 👤 | 2026-08-02 |
| K-088 | L133 | Yorumlayıcı beyaz listesi varsayılan olarak boştur  | 2026-08-02 |
| K-089 | L134 | Denetim izine yazılamayan bir script çalıştırılmaz  | 2026-08-02 |
| K-090 | L135 | Argüman doğrulaması sığdır; tam JSON Schema doğrulayıcı eklenmedi  | 2026-08-02 |
| K-091 | L136 | Argümanlar script sürecine stdin ile geçirilir  | 2026-08-02 |
| K-092 | L137 | İzin kaydı silinmez, `revoked_at` ile iptal edilir  | 2026-08-02 |
| K-093 | L138 | Alt çalıştırma ayrı bir `runs` satırıdır  | 2026-08-02 |
| K-094 | L139 | `root_run_id` denormalize edilir  | 2026-08-02 |
| K-095 | L140 | `runs.parent_run_id` için yabancı anahtar konmaz  | 2026-08-02 |
| K-096 | L141 | Bütçe ağaç boyunca tek nesnedir; `AgentRunBudget` bilerek `class`'tır  | 2026-08-02 |
| K-097 | L142 | Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır  | 2026-08-02 |
| K-098 | L143 | Alt agent gec (lazy) çözülür; derleme anında çözülmez  | 2026-08-02 |
| K-099 | L144 | Trace tamponunun sahibi yalnız kök çalıştırmadır  | 2026-08-02 |
| K-100 | L145 | `RunQuery.OnlyRootRuns` varsayılanı `true` 👤 | 2026-08-02 |
| K-101 | L146 | Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma 👤 | 2026-08-02 |
| K-102 | L147 | Alt çalıştırmalar kök akışa yalnız özet olay yazar 👤 | 2026-08-02 |
| K-103 | L148 | Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur  | 2026-08-02 |
| K-104 | L149 | Sıkıştırma bağımlılığı için yeni paket alınmadı  | 2026-08-02 |
| K-105 | L150 | `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında 👤 | 2026-08-02 |
| K-106 | L151 | Sıkıştırma varsayılan kapalıdır 👤 | 2026-08-02 |
| K-107 | L152 | Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez 👤 | 2026-08-02 |
| K-108 | L153 | Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir  | 2026-08-02 |
| K-109 | L154 | `Pipeline` sırası sabittir: ToolResult → SlidingWindow → Summarization  | 2026-08-02 |
| K-110 | L155 | `TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır; bu faz `InMemoryAgentFileStore`  | 2026-08-02 |
| K-111 | L156 | İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans  | 2026-08-02 |
| K-112 | L157 | `attachments.session_id` yabancı anahtar DEĞİLDİR  | 2026-08-02 |
| K-113 | L158 | Ek türü sihirli bayta göre doğrulanır; istemcinin bildirdiği `Content-Type` yok sayılır  | 2026-08-02 |
| K-114 | L159 | Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur  | 2026-08-02 |
| K-115 | L160 | `IFormFile` alan minimal API uçları `.DisableAntiforgery()` gerektirir  | 2026-08-02 |
| K-116 | L161 | `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez  | 2026-08-02 |
| K-117 | L162 | Kalıcı agent dosya belleği (`agent_files`) aynı migration'da (0006) eklendi 👤 | 2026-08-02 |
| K-118 | L163 | Workflow yürütmesi ayrı pakette (`AgentPrism.Workflows`), `AspNetCore` bu pakete referans vermez  | 2026-08-03 |
| K-119 | L164 | Beş desenin tamamı Faz 15'te uygulandı 👤 | 2026-08-03 |
| K-120 | L165 | Workflow çalıştırmaları `runs` tablosunda yaşar; `workflow_runs` açılmadı  | 2026-08-03 |
| K-121 | L166 | Kontrol noktası durumu `json` sütununda, `jsonb` DEĞİL  | 2026-08-03 |
| K-122 | L167 | 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir  | 2026-08-03 |
| K-123 | L168 | Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar  | 2026-08-03 |
| K-124 | L169 | `Magentic` plan onayı Faz 15'te kapalı (`RequirePlanSignoff(false)`)  | 2026-08-03 |
| K-125 | L170 | `GroupChat` yönetici agent kabul etmez; `managerAgentName` yalnız `Magentic` içindir  | 2026-08-03 |
| K-126 | L171 | Workflow tanımlarında sürüm GEÇMİŞİ tutulmaz  | 2026-08-03 |
| K-127 | L172 | 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır  | 2026-08-03 |
| K-128 | L173 | Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar  | 2026-08-03 |
| K-129 | L174 | `Microsoft.Agents.AI.Workflows.Declarative` alınmadı 👤 | 2026-08-03 |
| K-130 | L175 | Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar  | 2026-08-03 |
| K-131 | L176 | Graf tanımdan değil, DERLENMİŞ workflow'dan çıkarılır  | 2026-08-03 |
| K-132 | L177 | Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı  | 2026-08-03 |
| K-133 | L178 | Graf hatası çalıştırmayı `Failed` yapar  | 2026-08-03 |
| K-134 | L179 | `WorkflowJobHandler` ayrı bir pakete değil, `AgentPrism.Core`'a konur  | 2026-08-03 |
| K-135 | L180 | Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil  | 2026-08-03 |
| K-136 | L181 | Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır  | 2026-08-03 |
| K-137 | L182 | İş iptali ayrı bir `cancel_requested` sütunu gerektirmez; `jobs.status` tek gerçek kaynaktır  | 2026-08-03 |
| K-138 | L183 | `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti  | 2026-08-03 |
| K-139 | L184 | Eval (Faz 18) için yeni bir NuGet paketi eklenmedi  | 2026-08-03 |
| K-140 | L185 | AI yargıç (`AIJudgeLoopEvaluator`/`LoopAgent`) Faz 18 kapsamı dışında bırakıldı  | 2026-08-03 |
| K-141 | L186 | Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi 👤 | 2026-08-03 |
| K-142 | L187 | `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir  | 2026-08-03 |
| K-143 | L188 | Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context)  | 2026-08-03 |
| K-144 | L189 | Deney atama anahtarı varsayılan olarak oturum kimliğidir; `Experiment.AssignmentKey` bu fazda rezerve 👤 | 2026-08-03 |
| K-145 | L190 | `EvalRunTriggerRequest.AgentVersion` eklendi; eval, A/B deneyinden tamamen bağımsız bir mekanizmadır 👤 | 2026-08-03 |
| K-146 | L191 | `agentprism.agent.version` metrik/span etiketi varsayılan açıktır (`IncludeAgentVersionTag = true`); `experiment_id`/`variant` hiçbir zaman etiket olmaz 👤 | 2026-08-03 |
| K-147 | L192 | A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür  | 2026-08-03 |
| K-148 | L193 | Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi  | 2026-08-03 |
| K-149 | L194 | `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı  | 2026-08-03 |
| K-150 | L195 | Maliyette para birimi dönüşümü yapılmaz 👤 | 2026-08-03 |
| K-151 | L196 | Ağaç maliyeti kendi maliyetiyle toplanmaz; iki ayrı alan 👤 | 2026-08-03 |
| K-152 | L197 | `/api/stats` Eval/Workflow'u hariç tutmaya devam eder (K-141); yeni `/api/stats/timeseries` bilerek hariç TUTMAZ 👤 | 2026-08-03 |
| K-153 | L198 | `POST /api/stats/recalculate-costs` eklendi: Admin + denetim izi 👤 | 2026-08-03 |
| K-154 | L199 | Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır  | 2026-08-03 |
| K-155 | L200 | `AgentPrism:Pricing` `AgentPrismOptions`'ta (Core), bir saglayici paketinde değil  | 2026-08-03 |
| K-156 | L201 | Dashboard'un devre kesici uyarısı mevcut `/api/models/health` `Unhealthy` durumunu kullanır; yeni uç eklenmedi  | 2026-08-03 |
| K-157 | L202 | `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı  | 2026-08-03 |
| K-158 | L203 | Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında  | 2026-08-03 |
| K-159 | L204 | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir  | 2026-08-03 |
| K-160 | L205 | Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi 👤 | 2026-08-03 |
| K-161 | L206 | Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez 👤 | 2026-08-03 |
| K-162 | L207 | Kota aşımında devam eden çalıştırma KESİLMEZ; yalnızca yeni çalıştırma reddedilir 👤 | 2026-08-03 |
| K-163 | L208 | Webhook imzası zaman damgasını İÇERİR  | 2026-08-03 |
| K-164 | L209 | SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz  | 2026-08-03 |
| K-165 | L210 | Hız sınırının varsayılanı KAPALIDIR 👤 | 2026-08-03 |
| K-166 | L211 | `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür  | 2026-08-03 |
| K-167 | L212 | `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil  | 2026-08-03 |
| K-168 | L213 | MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok  | 2026-08-04 |
| K-169 | L214 | MCP OAuth geri dönüş adresi (`OAuthCallbackBaseUri`) sabit bir ayardır, istekten türetilmez  | 2026-08-04 |
| K-170 | L215 | MCP OAuth token'ları `(kiracı, sunucu)` başına tek bellek içi önbellekte, iki tüketici arasında paylaşılır  | 2026-08-04 |
| K-171 | L216 | Arka plandaki (etkileşimsiz) OAuth denemesi hemen başarısız olur, beklemez  | 2026-08-04 |
| K-172 | L217 | `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir  | 2026-08-04 |
| K-173 | L218 | Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi  | 2026-08-04 |
| K-174 | L219 | Mod A kaynak okuması `AgentDefinitionCompiler`'a `IMcpResourceContextProviderFactory` soyutlamasıyla bağlanır  | 2026-08-04 |
| K-175 | L220 | MCP kaynak toplu okuma önbelleği `ConcurrentDictionary`'e geçirildi  | 2026-08-04 |
| K-176 | L221 | SQL kalıcılık mantığı `AgentPrism.Sql.Shared` altında PAYLAŞILAN KAYNAK olarak yaşar 👤 | 2026-08-04 |
| K-177 | L222 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ  | 2026-08-04 |
| K-178 | L223 | Migration numaraları sağlayıcı başına bağımsızdır  | 2026-08-04 |
| K-179 | L224 | Şema adı kuralı iki sağlayıcıda AYNIDIR  | 2026-08-04 |
| K-180 | L225 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda  | 2026-08-04 |
| K-181 | L226 | `AgentPrism.SqlServer` AOT uyumlu olarak İŞARETLENMEZ  | 2026-08-04 |
| K-182 | L227 | Diziler SQL Server'a JSON metni olarak taşınır  | 2026-08-04 |
| K-183 | L228 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır  | 2026-08-04 |
| K-184 | L229 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez  | 2026-08-04 |
| K-185 | L230 | `AgentPrism` meta paketi `AgentPrism.SqlServer`'ı İÇERMEZ  | 2026-08-04 |
| K-186 | L231 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 | 2026-08-05 |
| K-187 | L232 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti  | 2026-08-05 |
| K-188 | L233 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor  | 2026-08-05 |
| K-189 | L234 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu  | 2026-08-05 |
| K-190 | L235 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır  | 2026-08-05 |
| K-191 | L236 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ  | 2026-08-05 |
| K-192 | L237 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ  | 2026-08-05 |
| K-193 | L238 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR  | 2026-08-05 |
| K-194 | L239 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING`  | 2026-08-05 |
| K-195 | L240 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir  | 2026-08-05 |
| K-196 | L241 | `AgentPrism.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi)  | 2026-08-05 |
| K-197 | L242 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni)  | 2026-08-05 |
| K-198 | L243 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz  | 2026-08-05 |
| K-199 | L244 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı)  | 2026-08-05 |
| K-200 | L245 | Parti silme her sağlayıcıda farklı teknik kullanır  | 2026-08-05 |
| K-201 | L246 | `MaxRows` var ama uygulanmıyor (ertelendi)  | 2026-08-05 |
| K-202 | L247 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır  | 2026-08-05 |
| K-203 | L248 | `sessions`/`conversations` ayrı hedeftir  | 2026-08-05 |
