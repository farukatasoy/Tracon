# 18 — MCP İstemcisi/Sunucusu ve A2A Dış Yüzeyi (`MCP`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../18-MCP-VE-A2A.md`](../../18-MCP-VE-A2A.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/18-MCP-VE-A2A.md
> ```

---

## Temiz geçen case'ler (35)

| Case | Durum | Başlık |
|---|---|---|
| MT-MCP-001 | ☑ | `PUT {prefix}/api/mcp-servers/{name}` yeni bir sunucu kaydı oluşturur |
| MT-MCP-002 | ☑ | `requiresApproval` alanı GÖNDERİLMEZSE varsayılan `true` |
| MT-MCP-004 | ☑ | `http`/`https` DIŞI bir uç adresi → `400` |
| MT-MCP-006 | ☑ | JSON yanıtında OAuth alanları `oauthEnabled`/`oauthClientId` biçiminde (camelCase, çift büyük harf DEĞİL) |
| MT-MCP-007 | ☑ | `GET {prefix}/api/mcp-servers` listede secret DEĞERİ hiç GÖRÜNMEZ |
| MT-MCP-008 | ☑ | `DELETE` sunucu kaydını siler |
| MT-MCP-010 | ☑ | Örnek uygulama config-bağlı `UseMcp` overload'ını kullanır — `AgentPrism:Mcp:RefreshInterval` GERÇEKTEN etkilidir |
| MT-MCP-011 | ☑ | Per-server ayarlar (`Endpoint`, `Transport`, `Headers`...) HİÇBİR ZAMAN `IConfiguration`'dan okunmaz |
| MT-MCP-012 | ☑ | `authorizationConfigurationKey` config'te TANIMSIZ/BOŞSA → istisna YOK, başlık atlanır |
| MT-MCP-015 | ☑ | Var olmayan bir MCP sunucusu kaydetmek AGENT KAYDINI ETKİLEMEZ |
| MT-MCP-016 | ☑ | Sunucu keşif turu ortasında OFFLINE olursa, ESKİ (bayat) tool listesi KORUNMAZ — boşaltılır |
| MT-MCP-017 | ☑ | Bir sunucunun zaman aşımına uğraması DİĞER sunucuları ETKİLEMEZ |
| MT-MCP-018 | ☑ | Arka plan keşif döngüsü İSTİSNA sonrası ASLA çökmez |
| MT-MCP-020 | ☑ | Keşfedilen tool adı `{sunucu}_{tool}` biçiminde niteleniyor — NOKTA AYRACI YOK |
| MT-MCP-021 | ☑ | Kod-tanımlı bir tool ile AYNI ADA sahip MCP tool'u ÇAKIŞIRSA kod tool KAZANIR |
| MT-MCP-024 | ☑ | `resources` yeteneği bildiren sunucuda sentetik `{sunucu}_read_resource` tool'u OTOMATİK belirir |
| MT-MCP-026 | ☑ | Yerel bir MCP sunucusu kur (tester-tedarikli altyapı) |
| MT-MCP-027 | ☑ | Yerel sunucuyu AgentPrism'e kaydet, tool keşfi gerçekleşir |
| MT-MCP-028 | ☑ | Keşfedilen tool'u GERÇEK bir agent çalıştırmasında kullan (uçtan uca) |
| MT-MCP-030 | ☑ | Varsayılan KAPALI: boş beyaz liste + `ExposeAllAgents=false` → `tools/list` BOŞ döner |
| MT-MCP-031 | ☑ | `ozetleyici` fixture: `tools/list` gerçek çıktısı |
| MT-MCP-032 | ☑ | `tools/call` gerçek çıktı üretir |
| MT-MCP-033 | ☑ | `message` alanı BOŞ/EKSİKSE hata döner |
| MT-MCP-034 | ☑ | Onay gerektiren tool taşıyan bir agent'ı dışa açmaya çalışmak → UYGULAMA BAŞLAMAZ |
| MT-MCP-035 | ☑ | Canlı katalog: yeni bir agent DB'ye eklenince MCP sunucusu YENİDEN BAŞLATILMADAN görünür |
| MT-MCP-036 | ☑ | Gerçek bir MCP istemcisiyle (Claude Code CLI) uçtan uca el sıkışma |
| MT-MCP-040 | ☑ | Agent kartı `GET .well-known/agent-card.json` gerçek çıktısı |
| MT-MCP-042 | ☑ | Agent kartındaki `url` alanı GÖRECELİDİR, mutlak DEĞİL |
| MT-MCP-043 | ☑ | A2A'da `ExposeAllAgents` seçeneği HİÇ YOKTUR — yalnız kayıt-zamanı sabit liste |
| MT-MCP-044 | ☑ | Çalışma anında eklenen agent A2A'da GÖRÜNMEZ — MCP'nin TAM TERSİ davranış |
| MT-MCP-045 | ☑ | Onay gerektiren tool taşıyan bir agent'ı A2A'ya açmaya çalışmak → AYNI GUARD, UYGULAMA BAŞLAMAZ |
| MT-MCP-048 | ☑ | `mcp.tsx` formu: OAuth açılınca `authorizationConfigurationKey` alanı OTOMATİK TEMİZLENİR |
| MT-MCP-049 | ☑ | `mcp.tsx` sunucu listesi tablosunda secret DEĞERİ hiç GÖRÜNMEZ |
| MT-MCP-050 | ☑ | `/agentprism/mcp` ve `/agentprism/a2a` GRUP SEVİYESİNDE `ExternalInvoke` kapsamını doğru uygular (pozitif kontrol) |
| MT-MCP-053 | ☑ | `AllowRemoteAccess=true` + `ExternalInvoke` kapsamlı anahtar YOKKEN → UYGULAMA BAŞLAMAZ (bağımsız koruma) |

## Ayrıntı taşıyan case'ler (8)

## MT-MCP-003 — `stdio` transport denemesi → `400`

**Gerçek sonuç**
**KALDI - HATA-S2-007 (Orta, HATA-S2-006 ile ayni kok neden).** Beklenen HTTP 400 yerine HTTP 500 (genel ProblemDetails) dondu. Kok neden: PUT /api/mcp-servers/{name} handler'i (GovernanceEndpoints.cs:190-195) McpServerRequest request parametresini ACIK [FromBody] ozniteligi OLMADAN (ORTUK govde baglama) aliyor; McpTransportMode enum'u (McpServerDefinition.cs:33-40) yalniz StreamableHttp ve Sse tasiyor, 'Stdio' hic bir enum uyesi degil - JsonStringEnumConverter bunu JsonException ile reddediyor, bu istisna Validate(name, request) (satir 197, 527) hic cagirilmadan, govde-baglama asamasinda olusuyor ve app.UseExceptionHandler() genel 500'e ceviriyor. Onemli ek bulgu: bu ORTUK (oznitelik olmadan) govde baglama ornegi, HATA-S2-006'nin 'ac [FromBody] kullanan 10 dosya' kapsam tahminini ASIYOR - grep '[FromBody]' bu deseni YAKALAMAZ, gercek etkilenen yuzey daha genis olabilir.

---

**Yeniden koşum (Aile G, 2026-08-14).** DÜZELTİLDİ — **HTTP 400**:
`{"title":"Gecersiz istek govdesi","detail":"The JSON value could not be converted to AgentPrism.McpTransportMode. Path: $.transport..."}`.
`GovernanceEndpoints`'in `/api/mcp-servers/{name}` PUT handler'i artık
`RequestBodyBinding.ReadAsync<McpServerRequest>` ile govdeyi elle okuyor —
implicit binding tamamen kaldırıldı. Bu case'in kendi bulgusu ("grep tabanlı
tahmin ORTUK baglamayı kaçırır") doğrulandı ve düzeltmenin kapsamını
genişletti: OpenAPI belgesindeki (`docs/openapi/agentprism.json`)
`requestBody` taşıyan TÜM rotalar tek tek çapraz kontrol edildi (yalnız
grep'e güvenilmedi) — implicit binding kullanan 9 EK uç bulundu
(`AgentEndpoints.RollbackAsync`, `.../run`, `SkillEndpoints.SaveAsync`,
`SessionEndpoints.BranchSessionAsync`, `KnowledgeEndpoints.UploadAsync`/`SearchAsync`,
`VoiceEndpoints.SpeakAsync`, `GovernanceEndpoints` tenants PUT + mcp-prompts
POST) ve hepsi aynı desene taşındı. Ayrıntı `KAPANIS-PLANI.md` §6 Aile G.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-005 — `oauthEnabled: true` VE `authorizationConfigurationKey` BİRLİKTE → `400`

**Gerçek sonuç**
Ilk denemede oauthClientId eksikti, farkli (ama gecerli) bir 400 (OAuth istemci kimligi eksik) tetiklendi - test verisi eksikti, urun kusuru degil. oauthClientId eklenerek tekrarlandiginda: HTTP 400, title: Cakisan kimlik dogrulama, detail: OAuth acikken authorizationConfigurationKey bos olmalidir... - beklenen karsilikli dislama kurali dogru calisiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-022 — `MaxToolsPerServer` aşımı → fazla tool'lar UYARIYLA düşürülür, HATA değil

**Gerçek sonuç**
MaxToolsPerServer=1 ile yeniden baslatildi. /api/tools listesinde test-sunucu kaynakli TAM 2 tool goruldu: test-sunucu_echo (gercek, alfabetik ilk) VE test-sunucu_read_resource (sentetik kaynak-okuma tool'u, MT-MCP-024). Log: "MCP sunucusu 'test-sunucu' 1 tool sinirini asti; fazlasi atiliyor." Sinirlama GERCEK tool'lara dogru uygulaniyor (14 -> 1); sentetik read_resource tool'u bu sinirin DISINDA ayrica ekleniyor (ayri kod yolu) - dokuman bunu hesaba katmamis ama davranis mantikli ve kusur degil, kesif BASARISIZ OLMADI, uyari logu ile duzgun dusuruldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-023 — `requiresApproval=true` bir MCP tool'u agent tarafından çağrılınca ONAY KARTI üretir

**Gerçek sonuç**
**KALDI - HATA-S2-004 kapsam genislemesi (onemli).** test-sunucu requiresApproval:true yapildi, test-sunucu_echo tool'unu tasiyan bir agent olusturuldu ve tetiklendi. Oturum gecmisinde bir toolApprovalRequest kaydi olustu (tool GERCEKTEN onay bekliyor, dogrulandi) - ama /api/runs?agentName=...&take=1 (YONETIM API'si, compat DEGIL) run kaydi status: Completed, completedAt dolu, error: null gosterdi - AwaitingInput/AwaitingApproval DEGIL. Kok neden bulundu: RunStatus.AwaitingInput (RunStatus.cs:29-48) kendi XML belgesinde ACIKCA 'Yalnizca RunKind.Workflow satirlarinda gorulur' diyor - yani bu durum kod-tanimli (Workflow olmayan, Kind:'Agent') calistirmalar icin YAPISAL OLARAK HIC KULLANILMIYOR. Bu, HATA-S2-004'un (dosya 08, MT-COMPAT-029) 'yalniz compat uclarini etkiliyor' seklindeki onceki cerceevelemesini YANLISLIYOR: sorun compat'a ozgu degil, TUM Agent-turu calistirmalarin genel bir mimari sinirlamasidir - onay bekleyen bir kod VEYA MCP tool'u calistiran herhangi bir Agent-turu run, HANGI ucten (yonetim API'si veya compat) tetiklenirse tetiklensin, status alaninda bunu hic yansitamiyor.

---

**🔧 Kapanış güncellemesi (2026-08-15, Aile V — HATA-S2-004 düzeltildi, bkz.
dosya 08 `MT-COMPAT-029`).** `RunRecordingAgent`'ta kök (`Depth == 0`) bir
çalıştırmanın `AwaitingApproval`'a kapanması artık yola (yönetim API'si,
compat, MCP, A2A, kuyruk) bakmaksızın uygulanıyor —
`AgentPrismRunOptions.SuspendOnApproval` (yalnız kuyruk yolunun ayarladığı,
tek başına anlamı kalmayan bayrak) kaldırıldı. Ampirik doğrulama (bu case'in
KENDİ senaryosu, canlı sunucuya karşı — `test-sunucu`'nun `requiresApproval`
tool'unu taşıyan agent, YÖNETİM API'si üzerinden tetiklendi): `GET
/api/agents/{name}/run` (Idempotency-Key ile akışsız) artık `status:
"AwaitingApproval"` döndürüyor — `Completed` DEĞİL. Case'in kendi tespiti
("sorun compat'a özgü değil, TÜM Agent-türü çalıştırmaların mimari
sınırlaması") doğrulanmış oldu; düzeltme de aynı genel kapsamda yapıldı
(tek bir kod yolu, `RunRecordingAgent`, hem yönetim API'si hem compat hem
MCP/A2A tarafından paylaşılıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-041 — `SendMessage` JSON-RPC çağrısı — PascalCase metot adı, `ROLE_AGENT`/`ROLE_USER` (spec DIŞI biçim)

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi govdesi 'messageId' alanini eksik birakmisti - A2A SDK'si bunu zorunlu kildigi icin ilk deneme -32602 'Invalid parameters: request body could not be deserialized as SendMessageRequest.' hatasi verdi (urun kusuru degil, eksik test verisi). messageId eklenerek duzeltildi: HTTP 200, metot gercekten SendMessage (PascalCase, A2A spec'inin message/send'i DEGIL - SDK davranisi). Yanittaki role alani ROLE_AGENT (protobuf-tarzi, 'agent' DEGIL). Beklenen uyumsuzluk dogrulandi - AgentPrism kusuru degil, bagimli SDK'nin (A2A.AspNetCore) davranisi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-047 — Tools ekranında MCP kökenli tool `mcp: {sunucu}` rozetiyle ayrışır

**Gerçek sonuç**
**KALDI - HATA-S2-008 (Yuksek).** /agentprism/tools ekraninda test-sunucu_* tool'lari dogru sekilde sari 'mcp: test-sunucu' rozeti gosterdi. AMA ayni ekranda get_order_status, cancel_order, list_recent_orders (UCU DE KOD-TANIMLI, [AgentPrismTool] ozniteligiyle isaretli, MCP ile hicbir ilgisi yok) da YANLIS bir sekilde 'mcp: generated' rozeti gosteriyor - case'in kendi beklentisi ('Kod-tanimli tool'larda bu rozet HIC YOKTUR') ihlal edildi. Kok neden bulundu: src/AgentPrism.Generators/SourceWriter.cs:105-107, [AgentPrismTool] kaynak ureteci HER kod-tanimli tool kaydi icin KOSULSUZ `source: "generated"` literal string'i geciriyor (AgentPrismToolRegistration constructor'inin varsayilani null'dir, XML belgesi de 'Kodda tanimli tool'larda null' diyor - SourceWriter bu sozlesmeyi ihlal ediyor). ToolDescriptor.Source (ToolDescriptor.cs:29-37) kendi belgesinde bu alanin yalniz 'uzak MCP sunucusundan gelen tool'larda' dolu olmasi gerektigini soyluyor. tools.tsx:71 (`{tool.source != null && <Badge>mcp: {tool.source}</Badge>}`) bu degeri kosulsuz MCP rozeti olarak yorumluyor. **Kapsam genis**: [AgentPrismTool] ozniteligi AgentPrism'in ONERILEN, kaynak-ureteci-tabanli (AOT uyumlu) tool tanimlama yontemidir - resmi `dotnet new` sablonu da (AgentPrism.Templates/content/AgentPrism.Starter/Tools/OrderTools.cs) ayni ozniteligi kullaniyor. Bu, [AgentPrismTool] kullanan HER projede, HER kod-tanimli tool'un arayuzde yaniltici bir 'mcp: generated' rozetiyle gosterilecegi anlamina geliyor - yalniz bu ornek uygulamaya ozgu degil, framework genelinde.

---

**2026-08-14 yeniden koşum (KAPANIS-PLANI Aile I).** Kök neden doğrulandığı
gibi `src/AgentPrism.Generators/SourceWriter.cs`'nin `WriteAggregator`
metodunda: her kod-tanımlı tool kaydı için koşulsuz `source: "generated"`
argümanı üretiliyordu. Düzeltme: bu argüman tamamen kaldırıldı — kayıt artık
`AgentPrismToolRegistration` constructor'ının `source: null` varsayılanını
kullanıyor, tıpkı XML belgesinin ("Kodda tanimli tool'larda null") ve
`ToolDescriptor.Source`'un kendi belgesinin ("yalniz uzak MCP sunucusundan
gelen tool'larda") tarif ettiği sözleşme gibi. Canlı doğrulama (`samples/AgentPrism.Api`,
gerçek Postgres şeması, `ProjectReference` ile tazelenmiş üreteç):
`GET /agentprism/api/tools` artık `get_order_status`, `cancel_order`,
`list_recent_orders` için `"source": null` döndürüyor — eskiden `"generated"`.
Regresyon testi: `tests/AgentPrism.Generators.UnitTests/GeneratedOutputTests.cs`
`Isaretli_statik_metot_icin_kayit_uretilir` artık üretilen toplayıcı dosyasının
`source:` literalini HİÇ TAŞIMADIĞINI doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-051 — `GovernanceEndpoints` (`/api/mcp-servers/*`) API ANAHTARI KAPSAMI HİÇ ÇAĞIRMAZ

**Gerçek sonuç**
**KALDI - HATA-S2-009 (Yuksek, dogrulanmis supheydi).** Yalniz RunsRead kapsamli (ExternalInvoke'suz) bir anahtarla PUT /api/mcp-servers/kapsam-testi -> HTTP 200, sunucu basariyla kaydedildi. GovernanceEndpoints.cs'in MCP sunucusu KAYIT API'si (PUT/DELETE/refresh/prompts/resources/oauth - toplam 15 uc eslemesi) hicbir RequireApiKeyScope cagrisi TASIMIYOR - kodun kendi yorumunda (GovernanceEndpoints.cs:230) 'GUVENLIK SINIRI' diye adlandirilan bir islem, kapsam sisteminden TAMAMEN bagimsiz calisiyor. 00-INDEKS.md'nin izledigi kalibin (WorkflowEndpoints/SchedulingEndpoints ile) BESINCI bagimsiz tekraridir.

---

**GECTI (Aile F, docs/manuel-test/KAPANIS-PLANI.md §6).** GovernanceEndpoints.cs'in MCP sunucusu KAYIT API'sinin tum 15 uc eslemesine RequireApiKeyScope eklendi (PUT/DELETE/refresh -> AgentsAdmin; GET/prompts/resources -> AgentsRead; oauth/start -> SecurityAdmin). Canli PostgreSQL'e karsi yeniden uretildi: ayni RunsRead-kapsamli (ExternalInvoke'suz) anahtarla PUT /api/mcp-servers/kapsam-testi -> HTTP 403, title: "Kapsam yetersiz", detail: "Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor." Sunucu KAYDEDILMEDI (istek handler'a hic ulasmadan filtrede reddedildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-052 — Varsayılan örnek uygulamada: statik bearer token sahibi HERKES dış MCP sunucusu kaydedebilir

**Gerçek sonuç**
**KALDI - HATA-S2-009 ile ayni kok neden, ikinci kanit.** Duz statik bearer token (FIX-TOKEN-01, salt-okunur run inceleme icin verilen ayni token) ile PUT /api/mcp-servers/token-kaniti -> HTTP 200. Ne RequireRole (no-op, rol politikalari kayitli degil) ne RequireApiKeyScope (hic cagrilmiyor) bu sinirlamayi koruyor - bu ortamda run'lari okumak icin verilen SIRADAN bir bearer token, agent'larin erisebilecegi KEYFI bir dis sunucuyu (potansiyel olarak kotu niyetli tool'lar sunan) sisteme ekleyebiliyor. Temizlik: her iki test sunucusu (token-kaniti, kapsam-testi) DELETE ile kaldirildi.

---

**KALDI KALIR - Aile F bu case'i KAPATMADI (docs/manuel-test/KAPANIS-PLANI.md §6/§11).** GovernanceEndpoints.cs'e RequireApiKeyScope eklendi (bkz. MT-MCP-051, artik Gecti) ama bu case'in kok nedeni FARKLIDIR: istek bir API anahtariyla degil DUZ statik AuthToken ile geliyor. AgentPrismEndpointFilter.InvokeAsync'te statik token '_authToken is { Length: > 0 } expected && BearerTokenValidator.IsValid(...)' dalinda eslesir ve dogrudan Proceed()'e gider - ApiKeyRequestContext hic kurulmaz, dolayisiyla CheckScope (ve ondaki ApiKeyScopeRequirement metadata'si) hic calismaz; bu TASARIM GEREGI boyle (bolum 53: kapsam denetimi yalniz ApiKeyRequestContext.Get() bos degilse uygulanir). Canli PostgreSQL'e karsi yeniden uretildi: ayni curl (FIX-TOKEN-01 ile PUT /api/mcp-servers/token-kaniti) Aile F SONRASI da HTTP 200 donuyor, sunucu yine kaydediliyor. Kok neden HATA-S2-009'un IKI ayri yarisidir: (1) API-anahtari kapsam boslugu - Aile F ile kapandi; (2) statik token'in rol politikasi kayitli olmayan bir ornekte fiilen tam-yetkili (root) davranmasi - bu Aile F'nin kapsami DISINDA, ayri bir bulgu olarak izlenir (yeni HATA numarasi kapanis sirasinda docs/KARARLAR.md'ye yazilacak). Temizlik: test-kaniti sunucusu DELETE ile kaldirildi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---
