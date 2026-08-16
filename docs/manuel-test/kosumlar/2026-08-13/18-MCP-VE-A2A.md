# 18 — MCP İstemcisi/Sunucusu ve A2A Dış Yüzeyi (`MCP`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../18-MCP-VE-A2A.md`](../../18-MCP-VE-A2A.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-MCP-001 — `PUT {prefix}/api/mcp-servers/{name}` yeni bir sunucu kaydı oluşturur

**Gerçek sonuç**
HTTP: 200, id dolu bir GUID, requiresApproval: false (istekte acikca belirtildigi icin varsayilan gecersiz kilindi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-002 — `requiresApproval` alanı GÖNDERİLMEZSE varsayılan `true`

**Gerçek sonuç**
**Dokuman duzeltmesi.** Script GET /api/mcp-servers/{name} (tekil) cagiriyordu - boyle bir uc YOK (yalniz liste ucu GET /api/mcp-servers var, GovernanceEndpoints.cs sadece MapGet(list)/MapPut/MapDelete/{name}'e ozel MapGet YOK). Script listeden filtrelemeye duzeltildi. Duzeltilmis sorguyla: True yazdirildi - requiresApproval alani gonderilmedigi icin varsayilan true kullanildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

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

## MT-MCP-004 — `http`/`https` DIŞI bir uç adresi → `400`

**Gerçek sonuç**
HTTP: 400, title: Adres semasi desteklenmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-005 — `oauthEnabled: true` VE `authorizationConfigurationKey` BİRLİKTE → `400`

**Gerçek sonuç**
Ilk denemede oauthClientId eksikti, farkli (ama gecerli) bir 400 (OAuth istemci kimligi eksik) tetiklendi - test verisi eksikti, urun kusuru degil. oauthClientId eklenerek tekrarlandiginda: HTTP 400, title: Cakisan kimlik dogrulama, detail: OAuth acikken authorizationConfigurationKey bos olmalidir... - beklenen karsilikli dislama kurali dogru calisiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-006 — JSON yanıtında OAuth alanları `oauthEnabled`/`oauthClientId` biçiminde (camelCase, çift büyük harf DEĞİL)

**Gerçek sonuç**
Ham JSON govdesinde alan adlari oauthEnabled, oauthClientId, oauthClientSecretConfigurationKey, oauthScopes bicimindedir - oAuthEnabled (cift buyuk harf) DEGIL. Regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-007 — `GET {prefix}/api/mcp-servers` listede secret DEĞERİ hiç GÖRÜNMEZ

**Gerçek sonuç**
Her sunucu satirinda authorizationConfigurationKey yalniz bir yapilandirma anahtari adi tasiyor (orn. AgentPrism:Mcp:TestSecret icin oauthClientSecretConfigurationKey alaninda), hicbir gercek secret DEGERI govdede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-008 — `DELETE` sunucu kaydını siler

**Gerçek sonuç**
HTTP: 404 - ftp-sunucu hic basariyla olusturulmamisti (negatif case kalici iz birakmadi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — MCP Keşfi: Yenileme Aralığı ve Yapılandırma Bağlama (Faz 22, K-353)

---

## MT-MCP-010 — Örnek uygulama config-bağlı `UseMcp` overload'ını kullanır — `AgentPrism:Mcp:RefreshInterval` GERÇEKTEN etkilidir

**Gerçek sonuç**
AgentPrism__Mcp__RefreshInterval=00:00:10 ile yeniden baslatildi. test-sunucu kaydedildikten 15 saniye sonra /api/tools listesinde 14 adet test-sunucu_* onekli tool goruldu (test-sunucu_echo, test-sunucu_get-sum, vb.) - varsayilan 5 dakika yerine 10 saniyede bir tarama gerceklesti (K-353 duzeltmesi dogrulandi). Uygulama loglarinda 4 McpDiscoveryService satiri gozlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-011 — Per-server ayarlar (`Endpoint`, `Transport`, `Headers`...) HİÇBİR ZAMAN `IConfiguration`'dan okunmaz

**Gerçek sonuç**
AgentPrism__Mcp__Servers__0__Endpoint=http://olmayan-bir-yer/mcp ayarlandi, uygulama yeniden baslatildi. GET /api/mcp-servers listesinde yalniz elle PUT edilen test-sunucu var - bu config anahtari hicbir etki uretmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-012 — `authorizationConfigurationKey` config'te TANIMSIZ/BOŞSA → istisna YOK, başlık atlanır

**Gerçek sonuç**
test-sunucu authorizationConfigurationKey: AgentPrism:Mcp:HicVarOlmayanAnahtar (hic tanimli olmayan bir user-secrets anahtari) ile guncellendi. POST /api/mcp-servers/refresh HTTP 200 dondu (toolCount:14, uygulama COKMEDI, tarama devam etti). Log satiri: "warn: AgentPrism.McpToolCatalog[0] MCP sunucusu 'test-sunucu' icin 'AgentPrism:Mcp:HicVarOlmayanAnahtar' yapilandirma anahtari bos. Kimlik dogrulama basligi gonderilmeyecek." - baglanti istegi o baslik olmadan gonderildi (tool sayisi degismedi, sunucu yine erisilebilirdi cunku gercek sunucu auth istemiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — MCP Keşfi: Sunucuya Ulaşılamaması — Zarif Bozulma (Faz 22)

---

## MT-MCP-015 — Var olmayan bir MCP sunucusu kaydetmek AGENT KAYDINI ETKİLEMEZ

**Gerçek sonuç**
Sunucu kaydi HTTP 200 ile basariyla olustu (kayit aninda baglanti denenmedi). Ardindan support/run cagrisi HTTP 200 ile normal calisti - ulasilamayan MCP sunucusu agent calistirmasini etkilemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-016 — Sunucu keşif turu ortasında OFFLINE olursa, ESKİ (bayat) tool listesi KORUNMAZ — boşaltılır

**Gerçek sonuç**
Yerel test sunucusu durduruldu, bir sonraki kesif turu (10s araliktan) beklendi. /api/tools sorgusunda test-sunucu_* tool sayisi 14 -> 0'a dustu - onceden kesfedilmis tool'lar LISTEDEN KAYBOLDU, bayat liste korunmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-017 — Bir sunucunun zaman aşımına uğraması DİĞER sunucuları ETKİLEMEZ

**Gerçek sonuç**
ulasilamayan (port 59999) VE test-sunucu (calisir durumda) birlikte kayitliyken /api/mcp-servers/refresh sonrasi test-sunucu 14 tool katti, ulasilamayan 0 katti - bir sunucunun basarisiz olmasi digerini etkilemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-018 — Arka plan keşif döngüsü İSTİSNA sonrası ASLA çökmez

**Gerçek sonuç**
ulasilamayan kayitliyken uygulama 22+ saniye (2+ kesif araligi, 10s ayarlanmis) canli tutuldu. Loglarda tekrarlayan 'warn: AgentPrism.McpToolCatalog[0] Client... client initialization error.' satirlari gorundu ama uygulama COKMEDI - GET /api/agents sonrasinda hala HTTP 200 dondu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — MCP Tool Adlandırma, Onay Sınırı, Kaynak Modları (Faz 22)

---

## MT-MCP-020 — Keşfedilen tool adı `{sunucu}_{tool}` biçiminde niteleniyor — NOKTA AYRACI YOK

**Gerçek sonuç**
/api/tools listesinde test-sunucu_echo, test-sunucu_get-sum, test-sunucu_read_resource gibi tool adlari goruldu - test-sunucu_<orijinal-ad> bicimi, nokta ile DEGIL alt cizgi ile.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-021 — Kod-tanımlı bir tool ile AYNI ADA sahip MCP tool'u ÇAKIŞIRSA kod tool KAZANIR

**Gerçek sonuç**
**Dokuman duzeltmesi (onemli).** Senaryonun kendi onerdigi kurulum yolu (sunucu adinin bos/ayni-onek olacak sekilde ayarlanmasi) kod ile IMKANSIZ: McpToolNaming.TryQualify (McpToolNaming.cs:32-33) HER ZAMAN kosulsuz $"{serverName}_{toolName}" ureterek onek ekliyor; IsValidServerName (satir 25-26) bos/whitespace sunucu adini zaten reddediyor (SafeName regex ^[a-zA-Z0-9_-]+$). Yani bir MCP tool adi hicbir zaman onek TASIMADAN kod-tanimli bir tool ile (orn. get_order_status) TAM ESLESEMEZ - carpisma yapisal olarak olusturulamiyor, sadece 'kod kazanir' varsayimi test EDILEMIYOR degil, senaryo TAMAMEN gereksiz hale geliyor (carpisma zaten imkansiz). Buna ragmen alttaki guvence dogrulandi: McpToolRegistry.TryGet (McpToolRegistry.cs:69-74) `_codeTools.TryGet(name, out tool) || _catalog...TryGet(...)` sirasiyla ONCE kod tool'larina bakiyor - kod okumasiyla dogrulandi, calisir kanit yerine geciyor.

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

## MT-MCP-024 — `resources` yeteneği bildiren sunucuda sentetik `{sunucu}_read_resource` tool'u OTOMATİK belirir

**Gerçek sonuç**
/api/tools listesinde test-sunucu_read_resource adli sentetik bir tool otomatik belirdi - yerel test sunucusu (server-everything) resources yetenegini bildiriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Yerel Test MCP Sunucusu (İzlek B)

> **Önemli not.** `PROMPT.md` §3'ün "Kapsam kararları" tablosu "Yerel test
> MCP sunucusu kurulur — dış bağımlılık yok" der, ama bu repo'da böyle bir
> sunucu **hiç dokümante edilmemiştir**: `docs/`, `samples/`, `scripts/`
> içinde `npx`/`docker` ile başlatılacak bir MCP sunucusuna dair TEK bir
> satır yoktur, `tests/AgentPrism.Mcp.UnitTests/` içinde de gerçek/sahte
> bir üst akış MCP HTTP sunucusu başlatan hiçbir test yoktur. Aşağıdaki
> kurulum bu boşluğu dolduran **tester-tedarikli altyapıdır** — AgentPrism
> deposunun bir parçası veya onaylı bir fixture DEĞİLDİR.

---

## MT-MCP-026 — Yerel bir MCP sunucusu kur (tester-tedarikli altyapı)

**Gerçek sonuç**
Yerel test sunucusu olarak resmi referans sunucusu kullanildi: `npx -y @modelcontextprotocol/server-everything streamableHttp` (CLI --port secenegi desteklemiyor, sabit port 3001 kullaniyor - dokumandaki 6060 ornegi yerine 3001 kullanildi, tum sonraki case'lerde tutarli). Sunucu http://localhost:3001/mcp uzerinde Streamable HTTP ile ayakta, en az 13 tool VE resources yetenegi sunuyor (initialize yanitinda dogrulandi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-027 — Yerel sunucuyu AgentPrism'e kaydet, tool keşfi gerçekleşir

**Gerçek sonuç**
POST /api/mcp-servers/refresh sonrasi GET /api/tools listesinde test-sunucu_echo, test-sunucu_get-sum gibi en az bir test-sunucu_<ad> tool'u goruldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-028 — Keşfedilen tool'u GERÇEK bir agent çalıştırmasında kullan (uçtan uca)

**Gerçek sonuç**
test-sunucu_echo tool'unu tasiyan bir agent olusturuldu, "MERHABA-MCP-TEST" metnini yankilamasi istendi. Yanit gercek tool cagrisi (functionCall test-sunucu_echo) + gercek MCP sunucu sonucu (functionResult: "Echo: MERHABA-MCP-TEST") + son metin ("Echo sonucu: MERHABA-MCP-TEST") icerdi. Olay akisinda ToolInvoking/ToolInvoked cerceveleri test-sunucu_echo adiyla goruldu - gercek MCP sunucusundan donen sonuc.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — MCP Sunucusu: AgentPrism'i Dışa Açma (Faz 50)

---

## MT-MCP-030 — Varsayılan KAPALI: boş beyaz liste + `ExposeAllAgents=false` → `tools/list` BOŞ döner

**Gerçek sonuç**
Yanit tam olarak tek tool icerdi (asagida MT-MCP-031 ile birlikte kanitlandi) - baska hicbir agent listede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-031 — `ozetleyici` fixture: `tools/list` gerçek çıktısı

**Gerçek sonuç**
Yanit TAM OLARAK tek tool icerdi: agentprism_ozetleyici, inputSchema yalniz message (string, required) alani tasiyor. Baska hicbir agent (support dahil) listede YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-032 — `tools/call` gerçek çıktı üretir

**Gerçek sonuç**
HTTP 200 (SSE event: message). result.content[0].text ozetleyici agent'inin urettigi gercek bir uc maddeli ozet metni icerdi. GET /api/runs?agentName=ozetleyici bu cagriya karsilik gelen YENI bir kok run (depth:0, status:Completed) gosterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-033 — `message` alanı BOŞ/EKSİKSE hata döner

**Gerçek sonuç**
Yanit isError:true tasiyor, content[0].text: "'message' argumani bos olamaz." - bos mesajla agent calistirilmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-034 — Onay gerektiren tool taşıyan bir agent'ı dışa açmaya çalışmak → UYGULAMA BAŞLAMAZ

**Gerçek sonuç**
Program.cs'te GEÇICI olarak .UseMcpServer(o => o.ExposedAgents.Add("support")) yapildi (cancel_order tasiyan agent), yeniden derlendi, dotnet run ile baslatildi. Loglarda 'crit: AgentPrism.McpApprovalGuardFilter[0] MCP disa acik yuzey denetimi basarisiz oldu; uygulama durduruluyor.' + InvalidOperationException ("'support' agent'i MCP uzerinden disa acilamaz: 'cancel_order' tool'lari kullanici onayi istiyor...") gorundu, sonra 'Application is shutting down...' - surec kendini kapatti (once dinlemeye basliyor, arka plan denetimi sonra durduruyor - LogCritical + StopApplication paterni, cikri unhandled exception degil). Program.cs degisikligi geri alindi (git diff temiz), yeniden derlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-035 — Canlı katalog: yeni bir agent DB'ye eklenince MCP sunucusu YENİDEN BAŞLATILMADAN görünür

**Gerçek sonuç**
**Dokuman duzeltmesi (onemli, yontem degisti).** Orijinal senaryo ozetleyici'yi PUT ile guncellemeyi oneriyordu - bu ISLEMEZ cunku ozetleyici KODDA TANIMLI bir agent'tir (origin:Code, isEditable:false, MT-API-008'in zaten kanitladigi 409 kurali gecerli); ustelik senaryonun kendi govdesi name/model alanlarini da eksik birakmisti (400 alindi, PUT hicbir zaman basarili olmadi). Duzeltilmis yontem: Program.cs'e GECICI olarak henuz var olmayan bir DB-kokenli agent adi (manuel-canli-katalog) ExposedAgents'e eklendi, TEK bir yeniden baslatma icinde: (1) tools/list -> yalniz ozetleyici, (2) POST /api/agents ile manuel-canli-katalog olusturuldu (ILK aciklama) -> restart OLMADAN tools/list'te agentprism_manuel-canli-katalog (description: ILK aciklama) gorundu, (3) PUT ile description GUNCELLENMIS aciklama - canli katalog testi yapildi -> AYNI restart icinde, tekrar tools/list cagrildiginda YENI aciklama goruldu. CatalogToolListHandler IAgentCatalog'u dogrulanmis sekilde CANLI okuyor, onbelleklenmis kopya donmuyor. Program.cs degisikligi geri alindi (git diff temiz).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-036 — Gerçek bir MCP istemcisiyle (Claude Code CLI) uçtan uca el sıkışma

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi komutu Authorization basligi TASIMIYORDU - ilk deneme HTTP 404 ("Failed to connect", OAuth kesif hatasi olarak yanlis yorumlandi) ile basarisiz oldu. --header "Authorization: Bearer manuel-test-token-2026" eklenerek (claude mcp add --help'te belgelenen secenek) duzeltildi: claude mcp get agentprism-manuel-test -> Status: Connected. Case sonrasi claude mcp remove agentprism-manuel-test -s local ile temizlendi (git status: yalniz .claude.json degisti, repo etkilenmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — A2A: AgentPrism'i Dışa Açma (Faz 50)

---

## MT-MCP-040 — Agent kartı `GET .well-known/agent-card.json` gerçek çıktısı

**Gerçek sonuç**
Govde name: ozetleyici, capabilities: {streaming:false, pushNotifications:false}, defaultInputModes: [text/plain], defaultOutputModes: [text/plain], supportedInterfaces[0].url: /agentprism/a2a/ozetleyici (goreceli), protocolBinding: JSONRPC.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-041 — `SendMessage` JSON-RPC çağrısı — PascalCase metot adı, `ROLE_AGENT`/`ROLE_USER` (spec DIŞI biçim)

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi govdesi 'messageId' alanini eksik birakmisti - A2A SDK'si bunu zorunlu kildigi icin ilk deneme -32602 'Invalid parameters: request body could not be deserialized as SendMessageRequest.' hatasi verdi (urun kusuru degil, eksik test verisi). messageId eklenerek duzeltildi: HTTP 200, metot gercekten SendMessage (PascalCase, A2A spec'inin message/send'i DEGIL - SDK davranisi). Yanittaki role alani ROLE_AGENT (protobuf-tarzi, 'agent' DEGIL). Beklenen uyumsuzluk dogrulandi - AgentPrism kusuru degil, bagimli SDK'nin (A2A.AspNetCore) davranisi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-042 — Agent kartındaki `url` alanı GÖRECELİDİR, mutlak DEĞİL

**Gerçek sonuç**
Cikti /agentprism/a2a/ozetleyici - goreceli bir yol, mutlak URL DEGIL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-043 — A2A'da `ExposeAllAgents` seçeneği HİÇ YOKTUR — yalnız kayıt-zamanı sabit liste

**Gerçek sonuç**
HTTP: 404 - A2A'da support icin agent karti yok, ExposeAllAgents secenegi bu tarafta yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-044 — Çalışma anında eklenen agent A2A'da GÖRÜNMEZ — MCP'nin TAM TERSİ davranış

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi scripti PUT kullaniyordu - PUT bir upsert DEGILDIR (MT-API-009), var olmayan bir agent'i olusturamaz, 404 doner. POST /api/agents ile duzeltildi: HTTP 201, agent basariyla olusturuldu. Ardindan GET /a2a/yeni-a2a-adayi/.well-known/agent-card.json -> HTTP 404 - AddA2AServer kayit-zamanli bir API, calisirken eklenen bir agent'i GOREMEDI (MT-MCP-035'in MCP tarafindaki canli davranisinin TAM TERSI).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-045 — Onay gerektiren tool taşıyan bir agent'ı A2A'ya açmaya çalışmak → AYNI GUARD, UYGULAMA BAŞLAMAZ

**Gerçek sonuç**
Program.cs'te GECICI olarak .UseA2A(o => o.ExposedAgents.Add("support")) yapildi, yeniden derlendi, dotnet run ile baslatildi. MT-MCP-034 ile BIREBIR ayni sonuc: 'crit'/istisna log satiri (A2AApprovalGuardFilter.RunCheckAsync, ExternalSurfaceGuard.EnsureNoApprovalRequiredTools ayni yardimci metot) + 'Application is shutting down...' - ayni desen (arka plan gorev + guard, LogCritical + StopApplication). Program.cs degisikligi geri alindi (git diff temiz), yeniden derlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Arayüz: MCP Sunucu Yönetimi ve Tool Kataloğu Rozetleri

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

## MT-MCP-048 — `mcp.tsx` formu: OAuth açılınca `authorizationConfigurationKey` alanı OTOMATİK TEMİZLENİR

**Gerçek sonuç**
/agentprism/mcp -> Yeni sunucu formu acildi. Authorization configuration key alanina metin yazildi (AgentPrism:Mcp:TestKeyName). OAuth (Authorization Code) onay kutusu isaretlendi. JS ile dogrulandi: alanin value'su OTOMATIK bosaldi ("") - MT-MCP-005'in sunucu tarafi kuralini form seviyesinde onceden yansitiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-049 — `mcp.tsx` sunucu listesi tablosunda secret DEĞERİ hiç GÖRÜNMEZ

**Gerçek sonuç**
test-sunucu authorizationConfigurationKey: AgentPrism:Mcp:GizliTestAnahtari ile guncellendi. /agentprism/mcp listesindeki Auth sutunu TAM OLARAK "AgentPrism:Mcp:GizliTestAnahtari" (yapilandirma ANAHTARI ADI) gosterdi - hicbir gercek token/sifre DEGERI gorunmedi. MT-MCP-007'nin API seviyesindeki kanitinin arayuz karsiligi dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Güvenlik: Dış Yüzey Doğru Korunuyor, Kayıt API'si DEĞİL

---

## MT-MCP-050 — `/agentprism/mcp` ve `/agentprism/a2a` GRUP SEVİYESİNDE `ExternalInvoke` kapsamını doğru uygular (pozitif kontrol)

**Gerçek sonuç**
HTTP: 403, title: Kapsam yetersiz, detail: Bu uc 'ExternalInvoke' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor. Pozitif kontrol dogrulandi - dis yuzeyin kendisi (/mcp) API anahtari kapsam sistemini DOGRU uyguluyor.

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

## MT-MCP-053 — `AllowRemoteAccess=true` + `ExternalInvoke` kapsamlı anahtar YOKKEN → UYGULAMA BAŞLAMAZ (bağımsız koruma)

**Gerçek sonuç**
**Dokuman duzeltmesi (13-KIRACI-VE-GUVENLIK.md'de zaten kaydedilen ayni duzeltme).** Program.cs'te gecici kod degisikligi GEREKMEDI - AgentPrism:Ui:AllowRemoteAccess anahtari 2026-08-11'de config'e baglandi (commit 419981b). AgentPrism__Ui__AllowRemoteAccess=true ortam degiskeniyle, sistemde hicbir ExternalInvoke kapsamli anahtar yokken baslatildi. Uygulama aciliste `Unhandled exception: System.InvalidOperationException: AllowRemoteAccess acikken MCP disa acilamaz: sistemde 'external:invoke' kapsamli...` ile COKTU (ExternalSurfaceGuard.EnsureRemoteAccessNotCombined, AgentPrismMcpServerExtensions.MapAgentPrismMcpServer) - MT-MCP-052'nin gosterdigi bosluga ragmen, loopback-disi acilma icin ayri, dar tutulmus bir baslangic korumasi dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
