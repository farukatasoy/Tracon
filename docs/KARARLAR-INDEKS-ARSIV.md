# KARARLAR — İndeks Arşivi

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme). Yeni kararlar için: [`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md).

## Arşivlenen Kararlar (133 kalem)

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
